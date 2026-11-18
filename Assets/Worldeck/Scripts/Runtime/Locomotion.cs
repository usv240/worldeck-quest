using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Going places in a world beyond your table. Three ways, all comfortable:
    /// - **Walk** for real (the world follows the ground's hills under your feet).
    /// - **Hands: jump or glide.** Hold a hand out, palm down, pointing where you want to go: a glowing arc lands on
    ///   the ground. A quick pinch jumps there (a short blink); pinch and hold glides that way, as the stick does, until
    ///   you let go.
    /// - **Controllers:** the left stick glides (with a soft dark ring at the edge of view while moving), the
    ///   right stick turns in 30° steps, or push it forward to aim a jump and let go to jump.
    /// The camera rig moves through the world; your real room stays where it is. Back at camp (or with "Back to
    /// camp") the rig returns home, so your real table and its world twin line up again.
    /// </summary>
    public sealed class Locomotion : MonoBehaviour
    {
        public Transform Rig, Head;
        public HandInput Hands;
        public WorldBuilder Builder;
        public Material Glow, Vignette;
        [Tooltip("Set by the director: a world is open, not frozen, nothing in transition.")]
        public bool Allowed;
        public Func<int, bool> Busy;
        public float Speed = 2.4f, SnapDegrees = 30f, MinJump = 1.2f, MaxJump = 18f;
        [Tooltip("How far from camp you can go (set per world).")]
        public float Limit = 60f;
        public float CampRadius = 2.0f;
        [Tooltip("Comfort: the stick takes 2.5 m blink-steps where you look instead of gliding (nothing ever slides).")]
        public bool StepMove;
        [Tooltip("Hands get around with Meta's own teleport gesture (MetaTeleport); our palm-down aim is off.")]
        public bool UseMetaHands;
        public float StepLength = 2.5f;
        float nextStep;
        bool snapHeight;
        [Header("Tests and editor")]
        public bool SimulateSticks;
        public Vector2 SimLeftStick, SimRightStick;
        float lastStickLog = -10f;

        Vector2 Stick(bool left)
        {
            if (SimulateSticks) return left ? SimLeftStick : SimRightStick;
            var c = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if (!OVRInput.IsControllerConnected(c)) return Vector2.zero;
            return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c);
        }

        public Vector3 Camp { get; private set; }
        public bool Away => Rig != null && ((Rig.position - homePos).sqrMagnitude > 0.0025f || Quaternion.Angle(Rig.rotation, homeRot) > 1f);
        public float FromCamp => Head == null ? 0f : Flat(Head.position - Camp).magnitude;
        public int Jumps { get; private set; }
        public int Turns { get; private set; }
        public float Glided { get; private set; }
        public bool Aiming(int h) => h >= 0 && h < aimHand.Length && aimHand[h] >= AimDelay;
        public event Action<string> Moved;
        public event Action CameHome;

        // The hand held out, still, for this long before a pinch jumps: reaching for something is not aiming.
        const float AimDelay = 0.55f;
        readonly Vector3[] aimLast = new Vector3[3];
        bool wasAllowed;
        Vector3 homePos;
        Quaternion homeRot = Quaternion.identity;
        bool homeSet, leftCamp;
        float campGround, floorY, waterBelow = float.NegativeInfinity;
        readonly float[] aimHand = new float[3];
        float stickAim, lastTurn = -1f, vignette, blink, blinkTarget;
        bool turnArmed = true;
        Vector3 target;
        bool targetOk;
        int aimingWith = -1;
        readonly List<Transform> dots = new List<Transform>();
        GameObject ring, vignetteGo, blinkGo;
        Renderer vignetteR, blinkR, ringR;
        MaterialPropertyBlock block;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void Start() => CaptureHome();

        void CaptureHome()
        {
            if (Rig == null || homeSet) return;
            homePos = Rig.position;
            homeRot = Rig.rotation;
            homeSet = true;
        }

        /// <summary>A world opened around a camp at <paramref name="camp"/> (the table): reset the travel.</summary>
        public void Begin(Vector3 camp, float limit, string biome)
        {
            CaptureHome();
            Home(false, "world opened");
            Camp = new Vector3(camp.x, 0f, camp.z);
            Limit = limit;
            leftCamp = false;
            Jumps = Turns = 0; Glided = 0f;
            campGround = Builder != null ? Builder.GroundAt(camp.x, camp.z) : 0f;
            floorY = campGround;
            // On a beach, the sea starts where the sand dips 15 cm under the camp's floor.
            waterBelow = biome == "beach" ? campGround - 0.15f : float.NegativeInfinity;
            Trace.Info("Move", "ready", "limit_m", limit, "biome", biome);
        }

        /// <summary>Back to where the world met your room (instantly, or with a blink).</summary>
        public void Home(bool withBlink, string why)
        {
            if (Rig == null || !homeSet) return;
            bool was = Away;
            Rig.SetPositionAndRotation(homePos, homeRot);
            leftCamp = false;
            if (withBlink) Blink();
            if (was) { Trace.Info("Move", "home", "why", why); CameHome?.Invoke(); }
        }

        void Update()
        {
            EnsureVisuals();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool moving = false;
            if (Allowed != wasAllowed) { wasAllowed = Allowed; Trace.Info("Move", Allowed ? "movement on" : "movement off"); }
            if (!Allowed || Rig == null || Head == null || Hands == null)
            {
                for (int i = 0; i < aimHand.Length; i++) aimHand[i] = 0f;
                stickAim = 0f;
                ShowArc(false);
            }
            else
            {
                moving = Controllers(dt);
                if (!UseMetaHands) moving |= HandAim(dt);
                Follow(dt);
                // Walked or jumped back into camp after being away: home again, so the real table lines up.
                if (Away && FromCamp > CampRadius + 0.4f) leftCamp = true;
                if (Away && leftCamp && FromCamp < CampRadius) Home(true, "back at camp");
            }
            vignette = Mathf.MoveTowards(vignette, moving ? 1f : 0f, dt * (moving ? 8f : 3f));
            blink = Mathf.MoveTowards(blink, blinkTarget, dt * 9f);
            if (blink >= blinkTarget - 0.001f) blinkTarget = 0f;
            SetAlpha(vignetteR, vignette);
            SetAlpha(blinkR, blink);
            if (vignetteGo != null) vignetteGo.SetActive(vignette > 0.01f);
            if (blinkGo != null) blinkGo.SetActive(blink > 0.01f);
        }

        // ---------- controllers ----------

        /// <summary>
        /// Comfortable stick movement (what makes people queasy is acceleration, sideways and backward sliding,
        /// and seeing the whole world slide): you go **where you look**, at one steady speed that starts and stops
        /// at once; no strafing (left/right on either stick turns in 30° steps); pulling back takes slow steps
        /// backwards; while moving, the edge of view closes to a tunnel.
        /// </summary>
        bool Controllers(float dt)
        {
            bool moving = false;
            Vector2 l = Stick(true), r = Stick(false);
            if (l.y > 0.45f || l.y < -0.6f)
            {
                if (Time.unscaledTime - lastStickLog > 5f) { lastStickLog = Time.unscaledTime; Trace.Info("Move", "left stick", "x", l.x, "y", l.y, "from_camp_m", FromCamp); }
                Vector3 f = Flat(Head.forward).normalized;
                if (StepMove)
                {
                    if (Time.unscaledTime >= nextStep)
                    {
                        nextStep = Time.unscaledTime + 0.6f;   // each step blinks: at most 1.7 a second
                        if (TryMove(l.y > 0f ? f * StepLength : -f * StepLength * 0.5f, "step")) Blink(0.75f);
                    }
                }
                else
                {
                    Vector3 move = l.y > 0f ? f * Speed * dt : -f * Speed * 0.5f * dt;
                    moving = TryMove(move, "glide");
                }
            }
            // Turn in steps with either stick (never smooth: smooth turning is the worst for comfort).
            float tx = Mathf.Abs(r.x) > Mathf.Abs(l.x) ? r.x : l.x;
            float ty = Mathf.Abs(r.x) > Mathf.Abs(l.x) ? r.y : l.y;
            if (Mathf.Abs(tx) < 0.35f) turnArmed = true;
            if (turnArmed && Mathf.Abs(tx) > 0.75f && Mathf.Abs(ty) < 0.5f)
            {
                turnArmed = false;
                Rig.RotateAround(Head.position, Vector3.up, Mathf.Sign(tx) * SnapDegrees);
                Turns++;
                lastTurn = Time.unscaledTime;
                Blink(0.6f);
                Moved?.Invoke("turn");
            }
            // Right stick forward: aim a jump; let go to jump.
            if (r.y > 0.7f && Mathf.Abs(r.x) < 0.6f)
            {
                stickAim += dt;
                var hs = Hands[1];
                Vector3 dir = (hs.Aim.sqrMagnitude > 0.5f ? hs.Aim : Head.forward + Vector3.up * 0.2f).normalized;
                Arc(hs.Tracked ? hs.Palm : Head.position + Vector3.down * 0.3f, dir, 1);
            }
            else if (stickAim > 0f)
            {
                if (stickAim > 0.15f && targetOk) Jump("stick");
                stickAim = 0f;
                if (aimingWith == 1) ShowArc(false);
            }
            if (moving) leftCamp |= FromCamp > CampRadius + 1.5f;
            return moving;
        }

        // ---------- hands: point palm-down; a quick pinch jumps, pinch and hold glides ----------

        /// <summary>A pinch held this long (seconds) glides instead of jumping.</summary>
        public const float HoldToGlide = 0.6f;
        float pinchSince = -1f;
        int pinchHand = -1;
        /// <summary>Gliding by hand right now (for tests and the log).</summary>
        public bool HandGliding { get; private set; }

        bool HandAim(float dt)
        {
            int best = -1;
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                bool pose = false;
                if (s.Tracked && !s.Controller && (Busy == null || !Busy(h)))
                {
                    Vector3 fromHead = s.Palm - Head.position;
                    Vector3 flat = Flat(fromHead);
                    pose = s.PalmNormal.y < -0.5f && flat.magnitude > 0.3f && fromHead.y < -0.08f && fromHead.y > -0.65f &&
                           Vector3.Dot(flat.normalized, Flat(Head.forward).normalized) > 0.3f;
                }
                // Keep aiming through the pinch itself (the hand changes shape a little as it pinches).
                // A moving hand is reaching, not aiming (once the arc shows, small movements steer it).
                float speed = dt > 0f ? Vector3.Distance(s.Palm, aimLast[h]) / dt : 0f;
                aimLast[h] = s.Palm;
                if (pose && aimHand[h] < AimDelay && speed > 0.35f) pose = false;
                if (pose || (aimHand[h] >= AimDelay && s.Tracked && s.PinchStrength > 0.3f)) aimHand[h] += dt; else aimHand[h] = 0f;
                if (aimHand[h] >= AimDelay && (best < 0 || aimHand[h] > aimHand[best])) best = h;
            }
            if (best < 0 || (pinchHand >= 0 && best != pinchHand && pinchSince > 0f))
            {
                if (pinchHand >= 0) { pinchSince = -1f; pinchHand = -1; if (HandGliding) Trace.Info("Move", "hand glide stopped", "why", "hand lost"); HandGliding = false; }
                if (best < 0) { if (aimingWith >= 0 && aimingWith != 1 || stickAim <= 0f) ShowArc(false); return false; }
            }
            var hand = Hands[best];
            Vector3 shoulder = Head.position + Vector3.down * 0.2f + Flat(Head.right).normalized * (best == 0 ? -0.17f : 0.17f);
            Vector3 dir = (hand.Palm - shoulder).normalized + Vector3.up * 0.25f;
            if (!HandGliding) Arc(hand.Palm, dir.normalized, best);
            if (Hands.PinchDown(best)) { pinchSince = Time.unscaledTime; pinchHand = best; }
            if (pinchHand != best || pinchSince < 0f) return false;
            bool held = hand.Pinching || hand.PinchStrength > 0.6f;
            if (held)
            {
                if (Time.unscaledTime - pinchSince < HoldToGlide) return false;
                // Held: glide where the hand points, at the stick's steady speed (the edge of view closes while moving).
                if (!HandGliding) { HandGliding = true; ShowArc(false); Trace.Info("Move", "hand glide", "from_camp_m", FromCamp); }
                Vector3 g = Flat(dir);
                bool moved = g.sqrMagnitude > 1e-4f && TryMove(g.normalized * Speed * dt, "glide");
                if (moved) leftCamp |= FromCamp > CampRadius + 1.5f;
                return moved;
            }
            // Let go: a quick pinch jumps to the arc; the end of a glide just stops.
            if (!HandGliding && targetOk) { Jump("hand"); aimHand[best] = 0f; }
            else if (HandGliding) Trace.Info("Move", "hand glide stopped", "from_camp_m", FromCamp);
            HandGliding = false;
            pinchSince = -1f; pinchHand = -1;
            return false;
        }

        /// <summary>A thrown arc from <paramref name="from"/>: where it lands on the ground is the jump target.</summary>
        void Arc(Vector3 from, Vector3 dir, int hand)
        {
            if (Builder == null) return;
            aimingWith = hand;
            const float v0 = 10.5f, g = 9.8f, step = 0.035f;
            Vector3 p = from, v = dir * v0;
            var pts = new List<Vector3>(48) { p };
            bool hit = false;
            for (int i = 0; i < 70; i++)
            {
                Vector3 next = p + v * step;
                v += Vector3.down * g * step;
                float ground = Builder.GroundAt(next.x, next.z);
                if (next.y <= ground)
                {
                    float k = Mathf.InverseLerp(p.y - Builder.GroundAt(p.x, p.z), next.y - ground, 0f);
                    next = Vector3.Lerp(p, next, k);
                    next.y = Builder.GroundAt(next.x, next.z);
                    pts.Add(next);
                    hit = true;
                    break;
                }
                p = next;
                pts.Add(p);
            }
            target = pts[pts.Count - 1];
            float dist = Flat(target - Head.position).magnitude;
            targetOk = hit && dist >= MinJump && dist <= MaxJump && Inside(target) && !float.IsNaN(target.x);
            ShowArc(true);
            // Dots along the arc, flowing towards the target.
            for (int i = 0; i < dots.Count; i++)
            {
                float u = Mathf.Repeat((i + Time.unscaledTime * 3f) / dots.Count, 1f);
                float fi = u * (pts.Count - 1);
                int a = Mathf.Clamp(Mathf.FloorToInt(fi), 0, pts.Count - 1), b = Mathf.Min(a + 1, pts.Count - 1);
                dots[i].position = Vector3.Lerp(pts[a], pts[b], fi - a);
                dots[i].localScale = Vector3.one * (targetOk ? 0.035f : 0.02f);
                dots[i].gameObject.SetActive(true);
            }
            if (ring != null)
            {
                ring.SetActive(true);
                ring.transform.position = target + Vector3.up * 0.02f;
                float s = (targetOk ? 0.55f : 0.35f) * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f));
                ring.transform.localScale = new Vector3(s, 1f, s);
                block ??= new MaterialPropertyBlock();
                ringR.GetPropertyBlock(block);
                block.SetColor("_Tint", targetOk ? new Color(1.4f, 1.15f, 0.8f) : new Color(0.6f, 0.25f, 0.2f));
                ringR.SetPropertyBlock(block);
            }
        }

        void ShowArc(bool on)
        {
            if (!on) aimingWith = -1;
            if (ring != null && ring.activeSelf != on) ring.SetActive(on);
            if (!on) foreach (var d in dots) if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
        }

        /// <summary>A jump to <paramref name="point"/> (Meta's teleport, tests): false if it is too near, too far or off the edge.</summary>
        public bool JumpTo(Vector3 point, string how)
        {
            if (Rig == null || Head == null || !CanLandAt(point)) return false;
            target = point;
            targetOk = true;
            Jump(how);
            return true;
        }

        /// <summary>Somewhere you may land: on the ground, not in the sea, within the world's edge, not right under you.</summary>
        public bool CanLandAt(Vector3 p)
        {
            if (Head == null || float.IsNaN(p.x)) return false;
            float dist = Flat(p - Head.position).magnitude;
            return dist >= MinJump * 0.75f && dist <= MaxJump * 1.2f && Inside(p);
        }

        /// <summary>
        /// Takes you to <paramref name="point"/> facing <paramref name="facing"/> (the tour): any distance within the
        /// world's edge, with the comfort blink. False if it is outside.
        /// </summary>
        public bool TeleportTo(Vector3 point, Vector3 facing, string how)
        {
            if (Rig == null || Head == null || float.IsNaN(point.x) || !Inside(point)) return false;
            facing.y = 0f;
            Vector3 look = Head.forward; look.y = 0f;
            if (facing.sqrMagnitude > 1e-4f && look.sqrMagnitude > 1e-4f)
                Rig.RotateAround(Head.position, Vector3.up, Vector3.SignedAngle(look, facing, Vector3.up));
            target = point;
            targetOk = true;
            Jump(how);
            return true;
        }

        /// <summary>A step turn around your head (Meta's turn gesture, or the sticks).</summary>
        public void TurnBy(float degrees, string how)
        {
            if (Rig == null || Head == null) return;
            Rig.RotateAround(Head.position, Vector3.up, degrees);
            Turns++;
            lastTurn = Time.unscaledTime;
            Blink(0.6f);
            Trace.Info("Move", "turned", "how", how, "deg", Mathf.Round(degrees));
            Moved?.Invoke("turn");
        }

        void Jump(string how)
        {
            Vector3 delta = Flat(target - Head.position);
            Rig.position += delta;
            snapHeight = true;
            Jumps++;
            Blink();
            ShowArc(false);
            if (FromCamp > CampRadius + 1.5f) leftCamp = true;
            Trace.Info("Move", "jumped", "how", how, "m", delta.magnitude, "from_camp_m", FromCamp);
            Sfx.Instance?.Ui("ack", 0.4f);
            Moved?.Invoke("jump");
        }

        // ---------- the ground ----------

        bool Inside(Vector3 p)
        {
            if (Flat(p - Camp).magnitude > Limit) return false;
            if (Builder != null && Builder.GroundAt(p.x, p.z) < waterBelow) return false;
            return true;
        }

        bool TryMove(Vector3 move, string how)
        {
            Vector3 next = Head.position + move;
            bool back = !Inside(Head.position) && Flat(next - Camp).sqrMagnitude < Flat(Head.position - Camp).sqrMagnitude;
            if (!Inside(next) && !back)
            {
                // Slide along the edge rather than stop dead.
                Vector3 alongX = new Vector3(move.x, 0f, 0f), alongZ = new Vector3(0f, 0f, move.z);
                if (Inside(Head.position + alongX)) move = alongX;
                else if (Inside(Head.position + alongZ)) move = alongZ;
                else return false;
            }
            Rig.position += move;
            if (how == "step") snapHeight = true;
            Glided += move.magnitude;
            Moved?.Invoke(how);
            return true;
        }

        /// <summary>The real floor rides on the world's ground: hills lift you, hollows lower you.</summary>
        void Follow(float dt)
        {
            if (Builder == null || !Away) return;
            float ground = Builder.GroundAt(Head.position.x, Head.position.z);
            float want = homePos.y + (ground - campGround);
            var p = Rig.position;
            p.y = snapHeight ? want : Mathf.Lerp(p.y, want, 1f - Mathf.Exp(-dt * 2.5f));
            snapHeight = false;
            Rig.position = p;
        }

        // ---------- comfort ----------

        public void Blink(float strength = 1f) { blinkTarget = Mathf.Max(blinkTarget, strength); blink = Mathf.Max(blink, strength * 0.9f); }

        void EnsureVisuals()
        {
            if (Head == null || Vignette == null || vignetteGo != null) return;
            vignetteGo = Util.MeshObject("Comfort vignette", Head, VignetteMesh(false), Vignette);
            vignetteGo.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            vignetteR = vignetteGo.GetComponent<Renderer>();
            blinkGo = Util.MeshObject("Blink", Head, VignetteMesh(true), Vignette);
            blinkGo.transform.localPosition = new Vector3(0f, 0f, 0.11f);
            blinkR = blinkGo.GetComponent<Renderer>();
            vignetteGo.SetActive(false); blinkGo.SetActive(false);
            if (Glow != null)
            {
                ring = Util.MeshObject("Jump target", transform, RingMesh(), Glow);
                ringR = ring.GetComponent<Renderer>();
                ring.SetActive(false);
                for (int i = 0; i < 18; i++)
                {
                    var d = Effects.Halo("Arc dot", transform, Glow, 0.03f, new Color(1f, 0.82f, 0.55f, 0.85f));
                    d.SetActive(false);
                    dots.Add(d.transform);
                }
            }
        }

        void SetAlpha(Renderer r, float a)
        {
            if (r == null) return;
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetFloat("_Alpha", a);
            r.SetPropertyBlock(block);
        }

        /// <summary>The vignette: a disc in front of the eyes, clear in the middle, dark at the rim (or all dark: the blink).</summary>
        static Mesh VignetteMesh(bool solid)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            const int seg = 40;
            float[] radii = { 0f, 0.054f, 0.088f, 0.6f };
            float[] alpha = solid ? new[] { 1f, 1f, 1f, 1f } : new[] { 0f, 0f, 1f, 1f };
            for (int r = 0; r < radii.Length; r++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radii[r]);
                    c.Add(new Color(0f, 0f, 0f, alpha[r]));
                }
            for (int r = 0; r + 1 < radii.Length; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = r * seg + i, b = r * seg + (i + 1) % seg, d = (r + 1) * seg + i, e = (r + 1) * seg + (i + 1) % seg;
                    t.AddRange(new[] { a, d, b, b, d, e });
                }
            var m = new Mesh { name = solid ? "blink" : "vignette" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        static Mesh RingMesh()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var uv = new List<Vector2>();
            const int seg = 40;
            var col = new Color(1f, 0.78f, 0.45f, 0.9f);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * 0.4f); v.Add(d * 0.5f);
                c.Add(col); c.Add(col);
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 3, k + 1, k, k + 2, k + 3 }); }
            }
            var m = new Mesh { name = "jump-ring" };
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}

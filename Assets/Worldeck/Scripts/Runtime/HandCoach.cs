using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Shows instead of telling: a see-through hand (GhostHand) acts out the gesture right where it is needed,
    /// a few times, with one short line. Press a button, pick something up and carry it, roast on the stick,
    /// turn a palm up for the menu, push the fire, tap the wrist button, tap the table. The owner stops it
    /// as soon as the player does the thing; it fades when a real hand comes close, and it gives up after
    /// <see cref="MaxLoops"/> rounds so it never nags.
    /// </summary>
    public sealed class HandCoach : MonoBehaviour
    {
        public Transform Head;
        public HandInput Hands;
        public Material Ghost, Panel;
        /// <summary>True while its demos must show over a card drawn on top of everything (How it works).</summary>
        public System.Func<bool> OverCards;
        Material ghost;   // its own copy of Ghost, so its draw order can change
        bool over;
        public Font Font;
        public int MaxLoops = 4;

        public enum Demo { None, Poke, Carry, Roast, PalmUp, PushDown, Wrist, Tap, Jump }
        public Demo Playing { get; private set; } = Demo.None;
        public int Shown { get; private set; }

        GhostHand right, left;
        Transform carry;
        Renderer carryR;
        Caption label;
        Vector3 a, b;
        float started;
        bool useLeft;
        MaterialPropertyBlock block;
        int loopsNow = 4;

        void Start() => Ensure();

        void Ensure()
        {
            if (right != null || Ghost == null) return;
            ghost = new Material(Ghost) { name = "Coach ghost" };
            right = GhostHand.Make("Coach hand R", transform, ghost, false);
            left = GhostHand.Make("Coach hand L", transform, ghost, true);
            if (Font != null)
            {
                label = Caption.Make("Coach line", transform, Font, Panel, Head, 14f, 24f);
                label.Priority = 3;
                label.LineChars = 30;
            }
        }

        /// <summary>
        /// Plays <paramref name="demo"/> at <paramref name="from"/> (and <paramref name="to"/> for carrying and
        /// roasting). <paramref name="carryMesh"/> is shown as a ghost copy riding the pinch.
        /// </summary>
        public void Show(Demo demo, Vector3 from, Vector3 to = default, string line = null, Mesh carryMesh = null, float carryScale = 1f, int loops = 0)
        {
            Ensure();
            if (right == null || Head == null) return;
            Stop("");
            Playing = demo;
            loopsNow = loops > 0 ? loops : MaxLoops;
            a = from; b = to;
            started = Time.unscaledTime;
            Shown++;
            // The hand nearest the spot does the showing (a thing on your left is shown by the left hand).
            Vector3 local = Head.InverseTransformPoint(from);
            useLeft = demo == Demo.Wrist ? false : local.x < -0.08f;
            if (carryMesh != null)
            {
                var go = Util.MeshObject("Coach carry", transform, carryMesh, ghost != null ? ghost : Ghost);
                go.transform.localScale = Vector3.one * carryScale;
                carry = go.transform;
                carryR = go.GetComponent<Renderer>();
            }
            if (label != null && !string.IsNullOrEmpty(line))
            {
                Vector3 at = demo == Demo.Carry || demo == Demo.Roast ? Vector3.Lerp(from, to, 0.5f) : from;
                label.transform.position = at + Vector3.up * (demo == Demo.PalmUp || demo == Demo.Wrist ? 0.16f : 0.12f);
                label.Say(line, 0f, new Color(1f, 0.9f, 0.75f));
            }
            Trace.Info("Coach", "showing", "demo", demo.ToString(), "at", from, "to", to, "hand", useLeft ? "left" : "right");
        }

        public void Stop(string why = "done")
        {
            if (Playing != Demo.None && why.Length > 0)
                Trace.Info("Coach", "stopped", "demo", Playing.ToString(), "why", why, "seconds", Time.unscaledTime - started);
            Playing = Demo.None;
            if (right != null) right.gameObject.SetActive(false);
            if (left != null) left.gameObject.SetActive(false);
            if (carry != null) Util.Kill(carry.gameObject);
            carry = null;
            if (label != null) label.Clear();
        }

        static float Period(Demo d) => d switch
        {
            Demo.Poke => 2.4f, Demo.Tap => 2.6f, Demo.Carry => 4.4f, Demo.Roast => 4.2f,
            Demo.PalmUp => 3.2f, Demo.PushDown => 3.4f, Demo.Wrist => 3.4f, Demo.Jump => 3.2f, _ => 3f
        };

        static float Seg(float u, float from, float to) => Util.Ease(Mathf.InverseLerp(from, to, u));

        void LateUpdate()
        {
            bool wantOver = OverCards != null && OverCards();
            if (ghost != null && wantOver != over) { over = wantOver; ghost.renderQueue = over ? 4002 : Ghost.renderQueue; }
            if (Playing == Demo.None || Head == null) return;
            float period = Period(Playing);
            float t = Time.unscaledTime - started;
            if (t >= period * loopsNow) { Stop("shown " + loopsNow + " times"); return; }
            float u = (t % period) / period;
            var hand = useLeft ? left : right;
            var other = useLeft ? right : left;
            hand.gameObject.SetActive(true);
            other.gameObject.SetActive(Playing == Demo.Wrist);
            float alpha = Seg(u, 0f, 0.1f) * (1f - Seg(u, 0.88f, 1f));
            Vector3 toHead = Head.position - a;
            Vector3 flat = new Vector3(toHead.x, 0f, toHead.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : -Vector3.forward;
            float side = useLeft ? -1f : 1f;
            Vector3 sideways = Vector3.Cross(Vector3.up, -flat) * side;   // towards the showing hand's side

            switch (Playing)
            {
                case Demo.Poke:
                {
                    Vector3 d = toHead.normalized;
                    hand.Point = 1f; hand.Pinch = 0f; hand.Curl = 0.9f;
                    float inn = Seg(u, 0.12f, 0.45f), push = Seg(u, 0.45f, 0.55f), back = Seg(u, 0.6f, 0.85f);
                    Vector3 start = a + d * 0.13f + sideways * 0.04f + Vector3.down * 0.03f;
                    Vector3 p = Vector3.Lerp(start, a + d * 0.004f, inn);
                    p = Vector3.Lerp(p, a - d * 0.008f, push * (1f - back));
                    p = Vector3.Lerp(p, a + d * 0.1f, back);
                    var rot = Quaternion.LookRotation(-d, Vector3.up) * Quaternion.Euler(-8f, 0f, -20f * side);
                    hand.PlaceBy(p, rot, GhostHand.Anchor.IndexTip);
                    break;
                }
                case Demo.Tap:
                {
                    // Two taps on a surface, from above.
                    hand.Point = 1f; hand.Pinch = 0f; hand.Curl = 0.9f;
                    float tap = Mathf.Max(Bump(u, 0.25f, 0.4f), Bump(u, 0.5f, 0.65f));
                    Vector3 p = a + Vector3.up * Mathf.Lerp(0.07f, 0.004f, tap) + flat * 0.01f;
                    p = Vector3.Lerp(a + Vector3.up * 0.14f + flat * 0.05f, p, Seg(u, 0.05f, 0.22f));
                    var rot = Quaternion.LookRotation(Vector3.down * 0.8f - flat * 0.6f, Vector3.up * 0.6f + flat * 0.8f);
                    hand.PlaceBy(p, rot, GhostHand.Anchor.IndexTip);
                    break;
                }
                case Demo.Carry:
                case Demo.Roast:
                {
                    bool roast = Playing == Demo.Roast;
                    Vector3 dirAB = b - a; dirAB.y = 0f;
                    dirAB = dirAB.sqrMagnitude > 1e-4f ? dirAB.normalized : -flat;
                    // Roasting: the hand stops short of the fire, so the marshmallow (31 cm along) is over it.
                    Vector3 end = roast ? b - dirAB * 0.29f + Vector3.up * 0.12f : b + Vector3.up * 0.03f;
                    float reach = Seg(u, 0.1f, 0.3f), close = Seg(u, 0.3f, 0.4f), move = Seg(u, 0.42f, 0.74f);
                    float open = roast ? 0f : Seg(u, 0.76f, 0.84f), away = roast ? 0f : Seg(u, 0.84f, 0.95f);
                    hand.Point = 0f; hand.Curl = 0.15f;
                    hand.Pinch = Mathf.Clamp01(close - open);
                    Vector3 approach = a + sideways * 0.07f + Vector3.up * 0.05f + flat * 0.05f;
                    Vector3 p = Vector3.Lerp(approach, a, reach);
                    Vector3 mid = Vector3.Lerp(a, end, 0.5f) + Vector3.up * 0.09f;
                    Vector3 arc = Bezier(a, mid, end, move);
                    if (move > 0f) p = arc;
                    if (away > 0f) p = Vector3.Lerp(end, end + Vector3.up * 0.08f + flat * 0.06f, away);
                    Vector3 fingers = (roast ? dirAB : (end - Head.position).normalized * 0.6f + Vector3.down * 0.4f).normalized;
                    var rot = Quaternion.LookRotation(fingers, Vector3.up) * Quaternion.Euler(18f, 0f, -35f * side);
                    hand.PlaceBy(p, rot, GhostHand.Anchor.PinchPoint);
                    if (carry != null)
                    {
                        Vector3 at = u < 0.42f ? a : u < 0.76f || roast ? hand.PinchPoint : end;
                        carry.position = at;
                        if (roast) carry.rotation = Quaternion.LookRotation(dirAB + Vector3.down * 0.15f, Vector3.up);
                        SetCarryAlpha(alpha * 0.8f);
                    }
                    break;
                }
                case Demo.PalmUp:
                {
                    hand.Point = 0f; hand.Pinch = 0f; hand.Curl = 0.08f;
                    float roll = Seg(u, 0.15f, 0.45f);
                    Vector3 fwd = -flat;
                    var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-10f, 0f, -180f * roll * side);
                    hand.PlaceBy(a + Vector3.up * 0.02f * roll, rot, GhostHand.Anchor.Palm);
                    break;
                }
                case Demo.Jump:
                {
                    // Hand out in front, palm down, pointing ahead and a little down; then a pinch.
                    hand.Point = 0f; hand.Curl = 0.15f;
                    hand.Pinch = Seg(u, 0.55f, 0.6f) * (1f - Seg(u, 0.66f, 0.7f));   // a quick pinch: held, it glides
                    Vector3 fwd = -flat;
                    float reach = Seg(u, 0.1f, 0.4f);
                    var rot = Quaternion.LookRotation((fwd + Vector3.down * 0.35f).normalized, Vector3.up);
                    hand.PlaceBy(a + fwd * (0.08f * reach) + Vector3.down * 0.02f * reach, rot, GhostHand.Anchor.Palm);
                    break;
                }
                case Demo.PushDown:
                {
                    // Palm down over the fire, three quick pushes.
                    hand.Point = 0f; hand.Pinch = 0f; hand.Curl = 0.05f;
                    float y = 0.26f;
                    for (int k = 0; k < 3; k++)
                    {
                        float s0 = 0.12f + k * 0.24f;
                        y -= 0.13f * Bump(u, s0, s0 + 0.2f, 0.3f);
                    }
                    var rot = Quaternion.LookRotation(-flat, Vector3.up);
                    hand.PlaceBy(a + Vector3.up * y, rot, GhostHand.Anchor.Palm);
                    break;
                }
                case Demo.Wrist:
                {
                    // The left hand held like reading a watch; the right index taps the back of its wrist.
                    var l = left;
                    l.Point = 0f; l.Pinch = 0f; l.Curl = 0.25f;
                    var lrot = Quaternion.LookRotation(Quaternion.AngleAxis(70f, Vector3.up) * -flat, Vector3.up) * Quaternion.Euler(0f, 0f, -25f);
                    l.PlaceBy(a, lrot, GhostHand.Anchor.WristBack);
                    l.Alpha = alpha;
                    Vector3 target = l.WristBack + Vector3.up * 0.012f;
                    right.Point = 1f; right.Pinch = 0f; right.Curl = 0.9f;
                    float inn = Seg(u, 0.25f, 0.5f), back = Seg(u, 0.62f, 0.82f);
                    Vector3 start = target + Vector3.up * 0.09f + sideways * 0.08f + flat * 0.04f;
                    Vector3 p = Vector3.Lerp(Vector3.Lerp(start, target, inn), start, back);
                    var rrot = Quaternion.LookRotation((target - start).normalized, flat);
                    right.PlaceBy(p, rrot, GhostHand.Anchor.IndexTip);
                    right.Alpha = alpha;
                    right.gameObject.SetActive(true);
                    break;
                }
            }

            // Fade when a real hand is near the ghost, so the two never fight for the same spot.
            float near = 1f;
            if (Hands != null)
                for (int h = 0; h < Hands.Count; h++)
                {
                    var s = Hands[h];
                    if (!s.Tracked) continue;
                    float dist = Vector3.Distance(s.IndexTip, hand.IndexTip);
                    near = Mathf.Min(near, Mathf.InverseLerp(0.05f, 0.16f, dist));
                }
            if (Playing != Demo.Wrist) hand.Alpha = alpha * Mathf.Lerp(0.2f, 1f, near);
        }

        static float Bump(float u, float from, float to, float peakAt = 0.5f)
        {
            if (u <= from || u >= to) return 0f;
            float k = (u - from) / (to - from);
            return k < peakAt ? Util.Ease(k / peakAt) : 1f - Util.Ease((k - peakAt) / (1f - peakAt));
        }

        static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float s = 1f - t;
            return s * s * p0 + 2f * s * t * p1 + t * t * p2;
        }

        void SetCarryAlpha(float alpha)
        {
            if (carryR == null) return;
            block ??= new MaterialPropertyBlock();
            carryR.GetPropertyBlock(block);
            block.SetFloat("_Alpha", alpha);
            carryR.SetPropertyBlock(block);
        }
    }
}

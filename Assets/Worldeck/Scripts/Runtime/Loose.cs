using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Things in a world you can simply pick up, move, put down anywhere or toss: pebbles by the fire, shells on
    /// the beach, ice in the snow, moon rocks that float in space, flowers in the meadow. Pinch one to hold it;
    /// let go to put it down (it drops onto your real table or floor) or throw it (the speed of your hand
    /// carries over). In space nothing falls; under the sea things sink slowly. Freeze stops them mid-air.
    /// Anything that ends up out of reach (on the floor, far away) floats back to where it started.
    /// </summary>
    public sealed class Loose : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material World, Glow;
        [Tooltip("The camera rig: hand speed is measured in its space, so moving through the world is not a throw.")]
        public Transform Rig;
        public float GrabRadius = 0.075f;
        public float HoverRadius = 0.13f;
        readonly bool[] wasFist = new bool[4], byFist = new bool[4];
        readonly float[] fistAt = { -10f, -10f, -10f, -10f };

        /// <summary>From the pinch point, or from the middle of a closing hand, whichever is nearer.</summary>
        static float HandTo(HandInput.State s, Vector3 p)
        {
            float d = Vector3.Distance(p, s.Pinch);
            if (s.Palm != Vector3.zero && !s.Controller) d = Mathf.Min(d, Vector3.Distance(p, s.Palm + s.PalmNormal * 0.035f) - 0.02f);
            return d;
        }
        public Func<int, bool> Blocked;   // hand index → something else owns this pinch
        public bool Frozen;

        public sealed class Thing
        {
            public Transform T;
            public Vector3 Home, Velocity, Spin;
            public float Radius;
            public int HeldBy = -1;
            public bool Resting = true;
            public float RestSince, ReturnAt = -1f, HoverK;
            public string Kind;
        }

        public readonly List<Thing> Things = new List<Thing>();
        public int Grabs { get; private set; }
        public int Throws { get; private set; }
        public event Action<Thing> Grabbed;
        public event Action<Thing, bool> Released;   // thrown?

        RoomModel room;
        Transform root;
        string biome = "";
        float gravity = 9.81f, drag;
        readonly Thing[] held = new Thing[3];
        readonly Quaternion[] gripRot = new Quaternion[3];
        // The hand's recent path (about the last 0.1 s, whatever the frame rate) for the throw speed.
        readonly Vector3[][] trail = { new Vector3[32], new Vector3[32], new Vector3[32] };
        readonly float[][] trailT = { new float[32], new float[32], new float[32] };
        readonly int[] trailN = new int[3];

        public bool Holding(int h) => h >= 0 && h < held.Length && held[h] != null;

        /// <summary>True if hand <paramref name="h"/> is close enough to a loose thing that a pinch would pick it up.</summary>
        /// <summary>How far the hand's pinch is outside the grab reach of the nearest loose thing (negative: inside).</summary>
        public float Nearest(int h)
        {
            if (Hands == null || Things.Count == 0) return float.MaxValue;
            var s = Hands[h];
            if (!s.Tracked) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var t in Things) if (t.HeldBy < 0) best = Mathf.Min(best, Vector3.Distance(t.T.position, s.Pinch) - t.Radius - GrabRadius);
            return best;
        }

        public bool Near(int h)
        {
            if (Hands == null || Things.Count == 0) return false;
            var s = Hands[h];
            if (!s.Tracked) return false;
            foreach (var t in Things) if (t.HeldBy < 0 && Vector3.Distance(t.T.position, s.Pinch) < GrabRadius + t.Radius) return true;
            return false;
        }

        /// <summary>What each world leaves lying about (prop kinds from Props.Build), and their size in metres.</summary>
        public static (string kind, float size)[] KindsFor(string b) => b switch
        {
            "campfire" => new[] { ("stone", 0.055f), ("stone", 0.045f), ("log_small", 0.09f) },
            "beach" => new[] { ("shell_rock", 0.06f), ("stone", 0.05f), ("stone", 0.04f) },
            "desert" => new[] { ("stone", 0.055f), ("crystal_cluster", 0.06f), ("stone", 0.04f) },
            "snow" => new[] { ("ice", 0.06f), ("snow_rock", 0.055f), ("snow_rock", 0.045f) },
            "space" => new[] { ("rock", 0.06f), ("rock", 0.045f), ("crystal_cluster", 0.055f) },
            "reef" => new[] { ("shell", 0.06f), ("shell_rock", 0.06f), ("stone", 0.045f) },
            "city" => new[] { ("lantern", 0.08f), ("flower", 0.07f) },
            "meadow" => new[] { ("flower", 0.08f), ("flower", 0.07f), ("stone", 0.045f) },
            _ => new[] { ("stone", 0.05f) }
        };

        public void Begin(Recipe r, RoomModel roomModel)
        {
            Stop();
            if (r == null || roomModel == null || roomModel.Table == null) return;
            room = roomModel;
            biome = r.Biome;
            gravity = biome == "space" ? 0f : biome == "reef" ? 1.4f : 9.81f;
            drag = biome == "space" ? 0.35f : biome == "reef" ? 2.2f : 0.05f;
            root = new GameObject("Loose things").transform;
            root.SetParent(transform, false);
            var table = room.Table;
            var colors = WorldColors.For(r);
            var rng = new Rng(r.Seed * 31 + 7);
            var kinds = KindsFor(biome);
            // Along the near edge of the table, left and right of you (the middle is the build's).
            Vector3 headLocal = table.ToLocal(Head != null ? Head.position : room.Head);
            float nearZ = Mathf.Sign(headLocal.z == 0f ? -1f : headLocal.z) * (table.Size.z * 0.5f - 0.07f);
            for (int i = 0; i < kinds.Length; i++)
            {
                float side = i == 0 ? -1f : i == 1 ? 1f : 0f;
                float x = side * Mathf.Max(0.05f, table.Size.x * 0.5f - 0.08f);
                float z = i == 2 ? nearZ * 0.35f + Mathf.Sign(nearZ) * 0.02f : nearZ;
                if (i == 2) x = (table.Size.x * 0.5f - 0.06f) * (rng.Next01() < 0.5f ? -0.55f : 0.55f);
                Vector3 local = new Vector3(x, table.Size.y, z);
                if (Mathf.Abs(local.x) > table.Size.x * 0.5f || Mathf.Abs(local.z) > table.Size.z * 0.5f) continue;
                var (kind, size) = kinds[i];
                var mb = new MeshBuilder();
                Props.Build(mb, kind, colors, rng);
                var b = mb.Bounds;
                float scale = size / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                var go = Util.MeshObject("Loose " + kind, root, mb.ToMesh("loose-" + kind), World);
                go.transform.localScale = Vector3.one * scale;
                // Pivot at the mesh's base: sit it on the table top.
                Vector3 baseOffset = new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale;
                var pivot = new GameObject("Loose " + kind).transform;
                pivot.SetParent(root, false);
                go.transform.SetParent(pivot, false);
                go.transform.localPosition = baseOffset - Vector3.up * size * 0.5f;
                float floatUp = biome == "space" ? 0.06f + 0.04f * i : 0f;
                Vector3 home = table.ToWorld(local) + Vector3.up * (size * 0.5f + floatUp);
                pivot.position = home;
                pivot.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                Things.Add(new Thing { T = pivot, Home = home, Radius = size * 0.5f, Kind = kind, RestSince = Time.time });
            }
            Trace.Info("Loose", "things to play with", "count", Things.Count, "biome", biome, "gravity", gravity);
        }

        public void Stop()
        {
            Things.Clear();
            for (int i = 0; i < held.Length; i++) held[i] = null;
            if (root != null) Util.Kill(root.gameObject);
            root = null;
        }

        void Update()
        {
            if (Things.Count == 0 || Hands == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // held things follow the hand even while time is frozen
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                bool fistNow = s.Tracked && s.Fist && !s.Pointing;
                if (fistNow && !wasFist[h]) fistAt[h] = Time.unscaledTime;
                wasFist[h] = fistNow;
                // The whole hand picks up too; a hand that closed a moment before it arrived still takes the thing.
                bool fistDown = fistNow && Time.unscaledTime - fistAt[h] < Maker.FistReach;
                if (held[h] != null)
                {
                    var t = held[h];
                    if (!s.Tracked || !(byFist[h] ? s.Grip || s.Pinching : s.Pinching)) { Release(h, s.Tracked); continue; }
                    Remember(h, s.Pinch);
                    t.T.position = Vector3.Lerp(t.T.position, s.Pinch, 1f - Mathf.Exp(-udt * 35f));
                    t.T.rotation = Quaternion.Slerp(t.T.rotation, s.PalmRot * gripRot[h], 1f - Mathf.Exp(-udt * 25f));   // turns with the hand
                    continue;
                }
                if (!s.Tracked || (Blocked != null && Blocked(h))) continue;
                Thing nearest = null;
                float best = HoverRadius;
                foreach (var t in Things)
                {
                    if (t.HeldBy >= 0) continue;
                    float d = HandTo(s, t.T.position) - t.Radius;
                    if (d < best) { best = d; nearest = t; }
                }
                if (nearest == null) continue;
                nearest.HoverK = 1f;
                if (Hints.Instance != null && Hints.Instance.Wants("loose", 2))
                    Hints.Instance.Request("loose", nearest.T.position + Vector3.up * (nearest.Radius + 0.05f), "Pinch or grab it.\nToss it if you like.", 1);
                if ((Hands.PinchDown(h) || fistDown) && best < GrabRadius * (s.Controller ? 1.4f : 1f))
                {
                    byFist[h] = !Hands.PinchDown(h) && !s.Pinching;
                    if (byFist[h]) fistAt[h] = -10f;
                    held[h] = nearest;
                    gripRot[h] = Quaternion.Inverse(s.PalmRot) * nearest.T.rotation;
                    nearest.HeldBy = h;
                    nearest.Resting = false;
                    nearest.ReturnAt = -1f;
                    nearest.Velocity = Vector3.zero;
                    trailN[h] = 0;
                    Remember(h, s.Pinch);
                    Grabs++;
                    Hints.Instance?.Learned("loose");
                    Sfx.Instance?.At("grab", nearest.T.position, 0.6f, 1.15f);
                    Trace.Info("Loose", "picked up", "kind", nearest.Kind, "hand", h, "how", byFist[h] ? "whole hand" : "pinch");
                    Grabbed?.Invoke(nearest);
                }
            }
            foreach (var t in Things)
            {
                t.HoverK = Mathf.MoveTowards(t.HoverK, 0f, dt * 4f);
                if (t.HeldBy >= 0) continue;
                Simulate(t, dt);
            }
        }

        void Remember(int h, Vector3 p)
        {
            var tr = trail[h]; var tt = trailT[h];
            int n = trailN[h];
            if (n == tr.Length) { for (int i = 1; i < n; i++) { tr[i - 1] = tr[i]; tt[i - 1] = tt[i]; } n--; }
            tr[n] = Rig != null ? Rig.InverseTransformPoint(p) : p; tt[n] = Time.unscaledTime; trailN[h] = n + 1;
        }

        void Release(int h, bool tracked)
        {
            var t = held[h];
            held[h] = null;
            if (t == null) return;
            t.HeldBy = -1;
            int n = trailN[h];
            Vector3 v = Vector3.zero;
            if (tracked && n >= 2)
            {
                // From the oldest sample in the last 0.1 s to the newest.
                int first = n - 1;
                while (first > 0 && trailT[h][n - 1] - trailT[h][first - 1] <= 0.1f) first--;
                float span = trailT[h][n - 1] - trailT[h][first];
                if (span > 1e-3f) v = (trail[h][n - 1] - trail[h][first]) / span;
                if (Rig != null) v = Rig.TransformDirection(v);
            }
            if (v.magnitude > 5f) v = v.normalized * 5f;
            bool thrown = v.magnitude > 0.9f;
            t.Velocity = v * (thrown ? 1.15f : 0.5f);
            t.Spin = UnityEngine.Random.insideUnitSphere * (thrown ? 360f : 60f);
            t.Resting = false;
            if (thrown) Throws++;
            Trace.Info("Loose", thrown ? "thrown" : "put down", "kind", t.Kind, "speed", v.magnitude);
            if (thrown) Sfx.Instance?.At("stoke_whoosh", t.T.position, 0.35f, 1.4f);
            Released?.Invoke(t, thrown);
        }

        void Simulate(Thing t, float dt)
        {
            if (Frozen) return;
            if (t.ReturnAt > 0f)
            {
                if (Time.time < t.ReturnAt) return;
                t.T.position = Vector3.Lerp(t.T.position, t.Home, 1f - Mathf.Exp(-dt * 3f));
                t.T.rotation = Quaternion.Slerp(t.T.rotation, Quaternion.Euler(0f, t.T.eulerAngles.y, 0f), dt * 3f);
                if (Vector3.Distance(t.T.position, t.Home) < 0.004f) { t.ReturnAt = -1f; t.Resting = true; t.RestSince = Time.time; t.Velocity = Vector3.zero; }
                return;
            }
            if (t.Resting)
            {
                // At rest out of reach (on the floor, behind you): float back after a few seconds.
                if (Time.time - t.RestSince > 3f && !InReach(t.T.position) && InReach(t.Home) && Vector3.Distance(t.T.position, t.Home) > 0.05f) GoHome(t, "out of reach");
                return;
            }
            t.Velocity += Vector3.down * gravity * dt;
            t.Velocity *= Mathf.Exp(-drag * dt);
            Vector3 p = t.T.position + t.Velocity * dt;
            t.T.rotation = Quaternion.Euler(t.Spin * dt) * t.T.rotation;
            t.Spin *= Mathf.Exp(-dt * 1.5f);

            // Land on the highest surface under it: furniture tops (including the table), then the floor.
            float ground = room.FloorY;
            foreach (var piece in room.Pieces)
            {
                var l = piece.ToLocal(p);
                if (Mathf.Abs(l.x) <= piece.Size.x * 0.5f && Mathf.Abs(l.z) <= piece.Size.z * 0.5f && piece.TopY <= t.T.position.y - t.Radius + 0.02f)
                    ground = Mathf.Max(ground, piece.TopY);
            }
            if (room.Table != null)
            {
                var tl = room.Table.ToLocal(p);
                if (Mathf.Abs(tl.x) <= room.Table.Size.x * 0.5f && Mathf.Abs(tl.z) <= room.Table.Size.z * 0.5f && room.Table.TopY <= t.T.position.y - t.Radius + 0.02f)
                    ground = Mathf.Max(ground, room.Table.TopY);
            }
            if (p.y - t.Radius < ground && t.Velocity.y <= 0f && gravity > 0f)
            {
                p.y = ground + t.Radius;
                float impact = -t.Velocity.y;
                t.Velocity = new Vector3(t.Velocity.x * 0.55f, impact * 0.3f, t.Velocity.z * 0.55f);
                if (impact > 0.4f) Sfx.Instance?.At("place_1", p, Mathf.Clamp01(impact * 0.25f), 1.2f);
                if (t.Velocity.magnitude < 0.12f)
                {
                    t.Velocity = Vector3.zero;
                    t.Resting = true;
                    t.RestSince = Time.time;
                }
            }
            else if (gravity <= 0f)
            {
                // Space: glide; bounce gently off the table top; come to rest when slow.
                if (p.y - t.Radius < ground) { p.y = ground + t.Radius; t.Velocity.y = Mathf.Abs(t.Velocity.y) * 0.5f; }
                if (t.Velocity.magnitude < 0.01f) { t.Velocity = Vector3.zero; t.Resting = true; t.RestSince = Time.time; }
            }
            // Walls: bounce back into the room.
            foreach (var w in room.Walls)
            {
                float d = Vector3.Dot(p - w.A, w.Normal);
                if (d < t.Radius && Vector3.Dot(t.Velocity, w.Normal) < 0f)
                {
                    p += w.Normal * (t.Radius - d);
                    t.Velocity = Vector3.Reflect(t.Velocity, w.Normal) * 0.5f;
                }
            }
            t.T.position = p;
            float far = Head != null ? Vector3.Distance(p, Head.position) : 0f;
            if (far > 4f || p.y < room.FloorY - 0.2f || float.IsNaN(p.x)) GoHome(t, "lost");
        }

        bool InReach(Vector3 p)
        {
            if (Head == null) return true;
            Vector3 d = p - Head.position;
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat > 3f) return true;   // you're off exploring: things stay where they are
            return flat < 0.75f && d.y > -0.85f;
        }

        void GoHome(Thing t, string why)
        {
            t.ReturnAt = Time.time + 0.6f;
            t.Resting = false;
            t.Velocity = Vector3.zero;
            Trace.Info("Loose", "floating back", "kind", t.Kind, "why", why);
            Sfx.Instance?.At("item_appear", t.Home, 0.4f);
        }
    }
}

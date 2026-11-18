using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// "Say anything": makes the things the player asks for. A thing comes from the app's own library (a lantern, a
    /// snowman) or is assembled from up to 28 simple coloured shapes that Juniper's helper described (a piano, a duck).
    /// It forms in front of you, on your real table, on the floor or where you point; you can pinch it and move it;
    /// "bigger", "blue" and "remove it" change it. At most twelve things exist at once; all go when you leave.
    /// </summary>
    public sealed class Maker : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material World, Glow, Shadow, Ghost;
        /// <summary>How many outlines of your sofa the last life size laid out (for tests).</summary>
        public int SofasShown { get; private set; }
        [Tooltip("Let go this close above a table or the floor (metres), a thing settles onto it; higher, it stays floating.")]
        public float SettleBelow = 0.22f;
        public const int MaxThings = 12;

        public sealed class Thing
        {
            public string Name;
            /// <summary>The library or model kind it was made as ("lion"), or "" when built from parts.</summary>
            public string Kind = "";
            public Transform T;
            public float Size, Shown;
            public Renderer R;
            /// <summary>Where it was when last picked up: let go at your face, it glides back there.</summary>
            public Vector3 PickedAt;
            public bool Return;
            /// <summary>A fingertip was inside it last frame (a touch is a fingertip coming in).</summary>
            public bool TipIn;
            public float TouchedAt = -10f;
            /// <summary>When an open fingertip came in (-1: none waiting).</summary>
            public float TipAt = -1f;
            /// <summary>Its soft shadow on whatever is under it.</summary>
            public Renderer Shade;
        }

        public readonly List<Thing> Things = new List<Thing>();
        public int Made { get; private set; }
        /// <summary>Juniper's helper is thinking: a shimmer waits where the thing will form.</summary>
        public bool Working;
        /// <summary>Hand index → something else owns this pinch.</summary>
        public Func<int, bool> Blocked;
        /// <summary>Hand index → a pinch here is reaching for something else: do not pull a far thing to it.</summary>
        public Func<int, bool> FarBlocked;

        static readonly HashSet<string> BuildKinds = new HashSet<string> { "fire", "sandcastle", "sun_crystal", "snowman", "star_map", "anemone_glow", "jukebox", "blossom_tree" };

        public static bool Knows(string kind) => Wishes.Knows(kind);

        readonly Thing[] held = new Thing[3];
        // Where the thing sat in the hand when it was picked up: it turns with the hand from then on.
        readonly Vector3[] gripPos = new Vector3[3];
        readonly Quaternion[] gripRot = new Quaternion[3];
        readonly Quaternion[] lastRot = { Quaternion.identity, Quaternion.identity, Quaternion.identity };
        // How fast each hand is turning (degrees a second about up), smoothed over about six frames.
        readonly float[] spinRate = new float[3];
        /// <summary>Let go turning at least this fast (degrees a second) and it keeps spinning.</summary>
        public const float SpinFlick = 300f;

        /// <summary>The axis × angle (degrees) the hand turned through between two rotations.</summary>
        static Vector3 SpinAxis(Quaternion from, Quaternion to)
        {
            (to * Quaternion.Inverse(from)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            return float.IsNaN(axis.x) ? Vector3.zero : axis * ang;
        }

        /// <summary>How far off the pointing line (degrees) a thing can be and still be pulled to the hand.</summary>
        public const float PointCone = 15f;

        /// <summary>The made thing the hand points at: along the line from the shoulder through the hand, up to 5 m.</summary>
        public Thing Pointed(int h)
        {
            if (Hands == null || Head == null || h < 0 || h >= Hands.Count) return null;
            var s = Hands[h];
            if (!s.Tracked) return null;
            Vector3 right = Head.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            Vector3 shoulder = Head.position + Vector3.down * 0.2f + right * (h == 0 ? -0.17f : h == 1 ? 0.17f : 0f);
            Vector3 dir = (s.Pinch - shoulder).normalized;
            Thing best = null;
            float bestCos = Mathf.Cos(PointCone * Mathf.Deg2Rad);
            foreach (var t in Things)
            {
                if (t.T == null || held[0] == t || held[1] == t) continue;
                Vector3 to = Centre(t) - s.Pinch;
                float d = to.magnitude;
                if (d < 0.05f || d > 5f) continue;
                // Bigger things are easier to point at.
                float cos = Vector3.Dot(to / d, dir) + Mathf.Clamp01(t.Size / Mathf.Max(0.3f, d)) * 0.03f;
                if (cos > bestCos) { bestCos = cos; best = t; }
            }
            return best;
        }
        // Both hands on one thing: turn it and resize it.
        Thing two;
        float twoDist, twoTarget, twoSize;
        Vector3 twoDir, twoOffset;
        Quaternion twoRot;
        public int Turns { get; private set; }
        public int Resizes { get; private set; }
        GameObject shimmer;
        MaterialPropertyBlock block;

        public bool Holding(int h) => h >= 0 && h < held.Length && held[h] != null;
        /// <summary>True in a world (the walls gone): life size is then real size.</summary>
        public Func<bool> InWorld;
        /// <summary>Let go this close to the eyes (metres), a thing goes back to where it was picked up.</summary>
        public const float FaceReach = 0.42f;
        Thing lastTouched;

        /// <summary>"It": the thing last held or looked at, or else the thing made last.</summary>
        public Thing Last()
        {
            if (lastTouched != null && lastTouched.T != null && Things.Contains(lastTouched)) return lastTouched;
            return Things.Count > 0 ? Things[Things.Count - 1] : null;
        }

        /// <summary>This is now the thing "it" means (held, or looked at).</summary>
        public void Touch(Thing t) { if (t != null) lastTouched = t; }
        public Thing HeldBy(int h) => h >= 0 && h < held.Length ? held[h] : null;

        /// <summary>How far the hand's pinch is outside the grab reach of the nearest made thing it could pick up (negative: inside).</summary>
        public float Nearest(int h)
        {
            if (Hands == null || h < 0 || h >= Hands.Count) return float.MaxValue;
            var s = Hands[h];
            if (!s.Tracked) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var t in Things)
            {
                if (t.T == null || IsHeld(t)) continue;
                best = Mathf.Min(best, Outside(t, s.Pinch));
            }
            return best;
        }

        public int Touches { get; private set; }
        public bool IsHeld(Thing t) => t != null && (held[0] == t || held[1] == t || held[2] == t);
        /// <summary>What the last change has to say for itself ("Life size: 2.5 m."), or "".</summary>
        public string Said = "";
        /// <summary>A flick this fast (m/s) as you let go throws a thing away.</summary>
        public const float TossSpeed = 2.2f;
        public int Tossed { get; private set; }
        // Each hand's speed, smoothed over a few frames; a tracking jump is not a flick.
        readonly Vector3[] lastPinch = new Vector3[3];
        readonly Vector3[] handVel = new Vector3[3];
        readonly bool[] seen = new bool[3];
        /// <summary>How long a hand may be out of the cameras' view before what it carries is let go (seconds).</summary>
        public const float LostGrace = 0.6f;
        readonly bool[] wasFist = new bool[3];
        readonly float[] fistAt = { -10f, -10f, -10f };
        /// <summary>A hand that closed this long before it reached a thing still takes it (people close as they reach).</summary>
        public const float FistReach = 0.6f;
        // How each hand picked its thing up: it stays held that way (a pinch is let go by opening the pinch, even
        // with the other fingers curled; a whole-hand grip by opening the hand).
        readonly bool[] byFist = new bool[3];
        readonly float[] seenAt = new float[3];
        /// <summary>Things picked up with a whole-hand grip, not a pinch (for tests and the log).</summary>
        public int Gripped { get; private set; }

        void StartTwo(Thing t)
        {
            var a = Hands[0]; var b = Hands[1];
            two = t;
            twoDist = Mathf.Max(0.03f, Vector3.Distance(a.Pinch, b.Pinch));
            twoDir = Flat(b.Pinch - a.Pinch);
            twoRot = t.T.rotation;
            twoOffset = t.T.position - (a.Pinch + b.Pinch) * 0.5f;
            var ms = t.T.GetComponent<MadeScale>();
            twoTarget = ms != null ? ms.Target : t.T.localScale.x;
            twoSize = t.Size;
            Hints.Instance?.Learned("turn");
            Trace.Info("Maker", "two hands", "name", t.Name);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>Both hands pinching one thing: it turns as the hands turn round each other and grows as they part.</summary>
        void TwoHands()
        {
            if (two == null) return;
            var a = Hands[0]; var b = Hands[1];
            if (two.T == null || held[0] != two || held[1] != two || !a.Tracked || !b.Tracked) { two = null; return; }
            float ratio = Mathf.Clamp(Vector3.Distance(a.Pinch, b.Pinch) / twoDist, 0.25f, 4f);
            float size = Mathf.Clamp(twoSize * ratio, ActAnswer.MinSize, Mathf.Max(ActAnswer.MaxSize, twoSize));
            ratio = size / Mathf.Max(0.001f, twoSize);
            float yaw = Vector3.SignedAngle(twoDir, Flat(b.Pinch - a.Pinch), Vector3.up);
            var turn = Quaternion.AngleAxis(yaw, Vector3.up);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 25f);
            two.T.rotation = Quaternion.Slerp(two.T.rotation, turn * twoRot, k);
            two.T.position = Vector3.Lerp(two.T.position, (a.Pinch + b.Pinch) * 0.5f + turn * twoOffset * ratio, k);
            if (Mathf.Abs(size - two.Size) > 0.002f) Resizes++;
            two.Size = size;
            var ms = two.T.GetComponent<MadeScale>();
            if (ms != null) ms.Target = twoTarget * ratio; else two.T.localScale = Vector3.one * twoTarget * ratio;
        }

        public bool Near(int h)
        {
            if (Hands == null || Things.Count == 0) return false;
            var s = Hands[h];
            if (!s.Tracked) return false;
            foreach (var t in Things) if (t.T != null && Outside(t, s.Pinch) < 0f) return true;
            return false;
        }

        public static Vector3 Centre(Thing t) => t.T.position + Vector3.up * (t.Size * 0.4f);

        /// <summary>How close a pinch must be to take a thing: its size, but never more than 30 cm (a life-size T. rex
        /// must not take every pinch around it).</summary>
        public static float Reach(Thing t) => Mathf.Clamp(t.Size * 0.45f, 0.07f, 0.3f);

        /// <summary>
        /// How far a point is outside a thing's grab region (negative: inside). The region is its reach round its
        /// middle, or, for things you can hold (up to 80 cm), anywhere within 3 cm of its body: a long bone is taken
        /// by its end, a deer by its head.
        /// </summary>
        public static float Outside(Thing t, Vector3 p)
        {
            float d = Vector3.Distance(Centre(t), p) - Reach(t);
            if (t.R != null && t.Size <= 0.8f)
            {
                var b = t.R.bounds;
                d = Mathf.Min(d, Mathf.Sqrt(b.SqrDistance(p)) - 0.03f);
            }
            return d;
        }

        /// <summary>The hand is at, or within <paramref name="margin"/> of, something you made: it is reaching for it.</summary>
        public bool CloseTo(int h, float margin)
        {
            if (Hands == null || h < 0 || h >= Hands.Count) return false;
            var s = Hands[h];
            if (!s.Tracked) return false;
            foreach (var t in Things) if (t.T != null && (Outside(t, s.Pinch) < margin || Outside(t, s.IndexTip) < margin)) return true;
            return false;
        }

        /// <summary>When something made was last touched or let go (unscaled time): no jumping right after.</summary>
        public float LastHandled { get; private set; } = -10f;
        /// <summary>The middle of the table is the world's own (its fire, its sandcastle): things keep this far from it (0: none).</summary>
        [System.NonSerialized] public float TableKeepOut;

        /// <summary>A fingertip must stay open inside a thing this long (seconds) to touch it; a hand closing to pinch is a grab.</summary>
        public const float TouchDelay = 0.4f;
        /// <summary>The same thing hops again no sooner than this after a touch (seconds).</summary>
        public const float TouchAgain = 2.5f;

        public List<string> Names()
        {
            var n = new List<string>();
            foreach (var t in Things) if (!n.Contains(t.Name)) n.Add(t.Name);
            return n;
        }

        /// <summary>Builds the mesh of a thing: a library object, or the parts. Its base sits at y = 0, centred.</summary>
        public static Mesh BuildMesh(ActItem a, WorldColors colors, int seed, out float largest)
        {
            colors.ModelTint = 0f;   // a lion on Mars is still a lion's colour
            var mb = new MeshBuilder();
            if (a.Kind.Length > 0 && BuildKinds.Contains(a.Kind)) Builds.Result(mb, a.Kind, colors, new Rng(seed), 0.4f);
            else if (a.Kind.Length > 0) Props.Build(mb, a.Kind, colors, new Rng(seed));
            else
                foreach (var p in a.Parts)
                {
                    var c = new Color(p.R, p.G, p.B, 1f);
                    mb.Push(new Vector3(p.X, p.Y, p.Z), Quaternion.Euler(p.Rx, p.Ry, p.Rz), new Vector3(p.Sx, p.Sy, p.Sz));
                    switch (p.Shape)
                    {
                        case "box": mb.Box(Vector3.zero, Vector3.one, c, null, 0f, true); break;
                        case "cylinder": mb.Cylinder(new Vector3(0f, -0.5f, 0f), 0.5f, 0.5f, 1f, 14, c, true, true); break;
                        case "cone": mb.Cylinder(new Vector3(0f, -0.5f, 0f), 0.5f, 0f, 1f, 14, c, true, true); break;
                        default:
                            // A rounder ball than the rock's icosahedron: two of them, turned against each other.
                            mb.Ico(Vector3.zero, Vector3.one * 0.5f, c, null, 0f);
                            mb.Push(Vector3.zero, Quaternion.Euler(36f, 58f, 0f), Vector3.one * 0.985f);
                            mb.Ico(Vector3.zero, Vector3.one * 0.5f, c, null, 0f);
                            mb.Pop();
                            break;
                    }
                    mb.Pop();
                }
            var b = mb.Bounds;
            largest = Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            // Re-centre: base on y = 0, middle on the axis.
            var mesh = mb.ToMesh("made-" + (a.Kind.Length > 0 ? a.Kind : "parts"));
            var v = mesh.vertices;
            var off = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            for (int i = 0; i < v.Length; i++) v[i] += off;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A spot kept on a piece's top (its own frame), far enough in for a thing this big.</summary>
        static Vector3 OnTop(RoomModel.Piece piece, Vector3 p, float size)
        {
            Vector3 local = Quaternion.Inverse(piece.Rotation) * (p - piece.TopCenter);
            float hx = Mathf.Max(0f, piece.Size.x * 0.5f - size * 0.45f), hz = Mathf.Max(0f, piece.Size.z * 0.5f - size * 0.45f);
            local = new Vector3(Mathf.Clamp(local.x, -hx, hx), 0f, Mathf.Clamp(local.z, -hz, hz));
            return piece.TopCenter + piece.Rotation * local;
        }

        /// <summary>How many made things stand on this piece's top.</summary>
        int CountOn(RoomModel.Piece piece)
        {
            if (piece == null) return 0;
            int n = 0;
            foreach (var t in Things)
            {
                if (t.T == null) continue;
                Vector3 local = Quaternion.Inverse(piece.Rotation) * (t.T.position - piece.TopCenter);
                if (Mathf.Abs(local.x) <= piece.Size.x * 0.5f && Mathf.Abs(local.z) <= piece.Size.z * 0.5f && Mathf.Abs(local.y) < 0.15f) n++;
            }
            return n;
        }

        /// <summary>The piece a place name puts things on (sofa, shelf, bed, or the table), or null.</summary>
        static RoomModel.Piece SurfaceOf(string place, RoomModel room)
        {
            if (room == null) return null;
            if (place == "sofa" || place == "shelf" || place == "bed")
            {
                string label = place == "sofa" ? "COUCH" : place == "shelf" ? "STORAGE" : "BED";
                foreach (var p in room.Pieces) if (!p.Virtual && string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase)) return p;
                return room.Table;
            }
            return place == "table" ? room.Table : null;
        }

        /// <summary>A spot moved sideways, right then left, until nothing already made stands on it.</summary>
        Vector3 Clear(Vector3 p, float size)
        {
            Vector3 right = Head != null ? Head.right : Vector3.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            Vector3 start = p;
            for (int k = 0; k < 8; k++)
            {
                Thing hit = null;
                foreach (var o in Things)
                {
                    if (o.T == null) continue;
                    Vector3 d = o.T.position - p; d.y = 0f;
                    if (d.magnitude < (o.Size + size) * 0.5f) { hit = o; break; }
                }
                if (hit == null) return p;
                float step = (hit.Size + size) * 0.5f + 0.03f;
                p = start + right * step * (k / 2 + 1) * (k % 2 == 0 ? 1f : -1f);
            }
            return p;
        }

        /// <summary>Things asked for on the table that formed in front of you instead: no reachable, clear part of the table.</summary>
        public int InFront { get; private set; }
        /// <summary>How far from you (flat, metres) a thing on the table may stand and still be picked up without leaning.</summary>
        public const float HandyReach = 0.75f;

        /// <summary>The point is inside another piece of scanned furniture (a table pushed half under a bed).</summary>
        static bool Buried(RoomModel room, RoomModel.Piece on, Vector3 p)
        {
            if (room == null) return false;
            foreach (var piece in room.Pieces)
                if (piece != on && !piece.Virtual && piece.Distance(p + Vector3.up * 0.05f) < 0.001f) return true;
            return false;
        }

        /// <summary>
        /// The spot on a table top nearest to arm's length from you: clear of other furniture, of things already
        /// standing there, and of the middle (the world's own build) when there is anywhere else.
        /// </summary>
        Vector3 NearYou(RoomModel.Piece table, RoomModel room, Vector3 usual, Vector3 head, float size)
        {
            float hx = Mathf.Max(0f, table.Size.x * 0.5f - size * 0.45f), hz = Mathf.Max(0f, table.Size.z * 0.5f - size * 0.45f);
            int nx = Mathf.Clamp(Mathf.CeilToInt(hx * 2f / 0.06f), 1, 40), nz = Mathf.Clamp(Mathf.CeilToInt(hz * 2f / 0.06f), 1, 40);
            float keep = 0.2f + size * 0.5f;
            Vector3 best = usual; float bestCost = float.MaxValue;
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    var local = new Vector3(Mathf.Lerp(-hx, hx, i / (float)nx), 0f, Mathf.Lerp(-hz, hz, j / (float)nz));
                    Vector3 p = table.TopCenter + table.Rotation * local;
                    Vector3 d = p - head; d.y = 0f;
                    float cost = Mathf.Abs(d.magnitude - 0.5f);
                    if (local.magnitude < keep) cost += 1f;
                    if (Buried(room, table, p)) cost += 5f;
                    foreach (var o in Things)
                    {
                        if (o.T == null) continue;
                        Vector3 g = o.T.position - p; g.y = 0f;
                        if (g.magnitude < (o.Size + size) * 0.5f) { cost += 2f; break; }
                    }
                    if (cost < bestCost) { bestCost = cost; best = p; }
                }
            return best;
        }

        /// <summary>Where a place name is, for the n-th of several things.</summary>
        public Vector3 Spot(string place, RoomModel room, int index, int count, float size)
        {
            Vector3 f = Head != null ? Head.forward : Vector3.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 head = Head != null ? Head.position : Vector3.up;
            float spread = (index - (count - 1) * 0.5f) * Mathf.Max(0.12f, size * 1.15f);
            float floor = room != null ? room.FloorY : head.y - 1.2f;
            var table = room != null ? room.Table : null;
            RoomModel.Piece piece = null;
            if (room != null && (place == "sofa" || place == "shelf" || place == "bed"))
            {
                string label = place == "sofa" ? "COUCH" : place == "shelf" ? "STORAGE" : "BED";
                foreach (var p in room.Pieces) if (!p.Virtual && string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase)) { piece = p; break; }
            }
            if (piece != null) return OnTop(piece, piece.TopCenter + right * spread, size);
            switch (place)
            {
                case "table":
                case "sofa": case "shelf": case "bed":
                    if (table != null)
                    {
                        Vector3 toYou = head - table.TopCenter; toYou.y = 0f;
                        toYou = toYou.sqrMagnitude > 1e-4f ? toYou.normalized : -f;
                        // The middle of the table holds the world's own build (the fire, the sandcastle): things
                        // stand beside it along the table's long side, one end then the other, a little towards you,
                        // and never off the edge.
                        bool longX = table.Size.x >= table.Size.z;
                        Vector3 along = table.Rotation * (longX ? Vector3.right : Vector3.forward);
                        Vector3 across = table.Rotation * (longX ? Vector3.forward : Vector3.right);
                        if (Vector3.Dot(across, toYou) < 0f) across = -across;
                        float halfAcross = (longX ? table.Size.z : table.Size.x) * 0.5f;
                        float aside = (0.2f + size * 0.5f + (index / 2) * Mathf.Max(0.12f, size * 1.15f)) * (index % 2 == 0 ? 1f : -1f);
                        Vector3 usual = OnTop(table, table.TopCenter + along * aside + across * Mathf.Max(0f, halfAcross - size * 0.5f) * 0.5f, size);
                        // A long table from its far end, or a table half under the bed: the usual spot is out of
                        // reach or inside other furniture. Then it stands on the part of the table nearest you.
                        Vector3 flat = usual - head; flat.y = 0f;
                        return flat.magnitude <= HandyReach && !Buried(room, table, usual) ? usual : NearYou(table, room, usual, head, size);
                    }
                    break;
                case "floor": return new Vector3(head.x, floor, head.z) + f * (0.55f + size * 0.4f) + right * spread;
                case "left":
                case "right":
                {
                    // Beside you, far enough out that a resting hand is not inside it; on the table if the table
                    // reaches there, not hanging in the air above it.
                    Vector3 side = right * ((place == "left" ? -1f : 1f) * (0.4f + size * 0.5f));
                    Vector3 at = head + f * 0.4f + side + Vector3.down * 0.25f + f * spread;
                    float under = SurfaceUnder(at, head.y);
                    if (under > floor + 0.25f && under < at.y) at.y = under;
                    return at;
                }
                case "there":
                    if (Hands != null)
                        for (int h = 0; h < 2; h++)
                        {
                            var s = Hands[h];
                            if (!s.Tracked) continue;
                            Vector3 dir = (s.IndexTip - s.Palm).normalized;
                            if (dir.y < -0.05f)
                            {
                                float d = (s.IndexTip.y - floor) / -dir.y;
                                if (d > 0.2f && d < 6f) return s.IndexTip + dir * d + right * spread;
                            }
                        }
                    break;
            }
            // In front of you, at arm's length, a little below the eyes.
            return head + f * (0.42f + size * 0.3f) + Vector3.down * (0.12f + size * 0.5f) + right * spread;
        }

        /// <summary>Makes the thing (count of them) and returns how many were made.</summary>
        public int Make(ActItem a, RoomModel room, Recipe recipe)
        {
            if (a == null || recipe == null) return 0;
            if (room != null) lastRoom = room;
            var colors = WorldColors.For(recipe);
            int n = Mathf.Clamp(a.Count, 1, ActAnswer.MaxCount), made = 0;
            // Already on the table: the next thing takes the next spot there.
            int onTop = a.Place == "table" ? CountOn(SurfaceOf("table", room)) : 0;
            for (int i = 0; i < n; i++)
            {
                var mesh = BuildMesh(a, colors, Made * 7 + i + 1, out float largest);
                if (mesh.vertexCount == 0) continue;
                while (Things.Count >= MaxThings) Remove(Things[0]);
                var go = Util.MeshObject("Made " + a.Name, transform, mesh, World);
                float size = Mathf.Clamp(a.Size, ActAnswer.MinSize, ActAnswer.MaxSize);
                if (a.Place == "table" && room != null && room.Table != null)
                {
                    // The table's middle holds the world's own build: a thing on it is small enough to stand beside it.
                    float half = Mathf.Max(room.Table.Size.x, room.Table.Size.z) * 0.5f;
                    size = Mathf.Min(size, Mathf.Max(0.12f, (half - 0.16f) / 0.95f));
                }
                var spot = Clear(Spot(a.Place, room, onTop + i, onTop + n, size), size);
                var top = SurfaceOf(a.Place, room);
                Vector3 stand = top != null ? OnTop(top, spot, size) : spot;
                // Stepping aside from what already stands there must not put it out of reach.
                if (a.Place == "table" && top != null && Head != null)
                {
                    Vector3 flat = stand - Head.position; flat.y = 0f;
                    if (flat.magnitude > HandyReach || Buried(room, top, stand)) stand = NearYou(top, room, stand, Head.position, size);
                    // No part of the table is both reachable and clear from here (you are across the room from it,
                    // or the scan has it under the sofa): it forms in front of you instead, on whatever is there.
                    flat = stand - Head.position; flat.y = 0f;
                    if (flat.magnitude > 1f || Buried(room, top, stand))
                    {
                        Vector3 front = Spot("front", room, i, n, size);
                        // Not inside a tall plant or a cupboard either: nearer you, then to either side.
                        Vector3 back = Head.position - front; back.y = 0f;
                        Vector3 side = Vector3.Cross(Vector3.up, back.normalized);
                        foreach (var shift in new[] { Vector3.zero, back * 0.4f, side * 0.25f, -side * 0.25f, back * 0.4f + side * 0.25f, back * 0.4f - side * 0.25f, back * 0.7f })
                        {
                            stand = front + shift;
                            stand.y = Mathf.Max(stand.y, SurfaceUnder(stand, Head.position.y));
                            if (!Buried(room, null, stand)) break;
                        }
                        // Still in something (a counter higher than your eyes, in a cramped seat): on top of it, never in it.
                        if (Buried(room, null, stand))
                            foreach (var piece in room.Pieces)
                                if (!piece.Virtual && piece.Distance(stand + Vector3.up * 0.05f) < 0.001f) { stand.y = piece.TopY + 0.001f; break; }
                        InFront++;
                    }
                }
                go.transform.position = stand;
                Vector3 look = Head != null ? Head.position - go.transform.position : Vector3.back; look.y = 0f;
                if (look.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                var t = new Thing { Name = a.Name, Kind = a.Kind ?? "", T = go.transform, Size = size, R = go.GetComponent<Renderer>() };
                t.T.localScale = Vector3.one * 0.001f;
                go.AddComponent<MadeScale>().Init(size / largest);
                if (LifeSize.IsCreature(t.Kind))
                {
                    // Legs that step when it walks (to your hand, about the table).
                    if (Gait.Rig(mesh, t.Kind)) go.AddComponent<Gait>().Size = () => t.Size;
                    var alive = go.AddComponent<Alive>();
                    alive.Head = Head;
                    alive.Busy = () => IsHeld(t) || t.Return || t.T.GetComponent<Hop>() != null;
                    alive.CanStepTo = SameGround;
                    alive.Size = () => t.Size;
                    alive.Called = () => Offered(t);
                    alive.Arrived += () =>
                    {
                        Came++;
                        LastHandled = Time.unscaledTime;
                        Hop.On(t.T, t.Size);
                        Sfx.Instance?.At("touch", Centre(t), 0.6f);
                        Trace.Info("Maker", "came to a hand", "name", t.Name);
                    };
                }
                Things.Add(t);
                Made++; made++;
                var burst = Effects.Burst(transform, Glow, new Color(1f, 0.85f, 0.55f));
                burst.transform.position = go.transform.position + Vector3.up * size * 0.4f;
                burst.Play();
                Destroy(burst.gameObject, 2f);
                Sfx.Instance?.At("item_appear", go.transform.position, 0.9f);
            }
            if (made > 0) Sfx.Instance?.Ui("build_complete", 0.5f);
            Trace.Info("Maker", "made", "kind", a.Kind.Length > 0 ? a.Kind : "parts", "parts", a.Parts.Count, "count", made, "place", a.Place, "size_m", a.Size);
            return made;
        }

        Thing Find(string target)
        {
            if (Things.Count == 0) return null;
            if (string.IsNullOrEmpty(target) || target == "last" || target == "it" || target == "that") return Last();
            for (int i = Things.Count - 1; i >= 0; i--) if (Things[i].Name == target) return Things[i];
            for (int i = Things.Count - 1; i >= 0; i--) if (Things[i].Name.Contains(target) || target.Contains(Things[i].Name)) return Things[i];
            return Last();
        }

        /// <summary>Bigger or smaller, a new colour, or gone. Returns false if there is nothing to change.</summary>
        public bool Change(ActItem a, RoomModel room = null)
        {
            Said = "";
            if (a.Spin != 0)
            {
                var st = a.Target == "all" ? null : Find(a.Target);
                var list = st != null ? new List<Thing> { st } : new List<Thing>(Things);
                foreach (var x in list) { if (x.T == null) continue; if (a.Spin > 0) Spinner.On(x.T, 50f, 0f); else { var sp = x.T.GetComponent<Spinner>(); if (sp != null) Destroy(sp); } }
                return list.Count > 0;
            }
            if (a.Target == "all" && a.Remove) { bool any = Things.Count > 0; Clear(); return any; }
            if (a.Remove && a.Every)
            {
                // "Remove the pumpkins": every one of them.
                var gone = Things.FindAll(x => !string.IsNullOrEmpty(x.Name) && (x.Name == a.Target || x.Name.Contains(a.Target) || a.Target.Contains(x.Name)));
                foreach (var x in gone) Remove(x);
                if (gone.Count > 0) Sfx.Instance?.Ui("ui_select", 0.6f);
                Trace.Info("Maker", "removed", "name", a.Target, "count", gone.Count);
                return gone.Count > 0;
            }
            if (a.LifeSize) return MakeLifeSize(Find(a.Target), room);
            var t = Find(a.Target);
            if (t == null) return false;
            if (a.Remove) { Remove(t); Sfx.Instance?.Ui("ui_select", 0.6f); return true; }
            if (Mathf.Abs(a.Scale - 1f) > 0.001f)
            {
                var ms = t.T.GetComponent<MadeScale>();
                float now = ms != null ? ms.Target : t.T.localScale.x;
                float unit = now / Mathf.Max(0.001f, t.Size);
                t.Size = Mathf.Clamp(t.Size * a.Scale, ActAnswer.MinSize, Mathf.Max(ActAnswer.MaxSize, t.Size));
                if (ms != null) ms.Target = unit * t.Size;
            }
            if (a.Color.Length == 7 && ColorUtility.TryParseHtmlString(a.Color, out var c) && t.R != null)
            {
                block ??= new MaterialPropertyBlock();
                t.R.GetPropertyBlock(block);
                // The tint multiplies the thing's own colours; brightened so a tint reads as that colour.
                block.SetColor("_Tint", Color.Lerp(c, Color.white, 0.15f) * 1.6f);
                t.R.SetPropertyBlock(block);
            }
            Trace.Info("Maker", "changed", "scale", a.Scale, "color", a.Color);
            return true;
        }

        /// <summary>
        /// As big as it is in life, up to what a room holds (LifeSize.RoomMax). Anything bigger than half a metre stands
        /// on the floor in front of you, side-on if it is long, far enough away not to be in you.
        /// </summary>
        /// <summary>This thing at its real size now (after arriving where it lives).</summary>
        public bool LifeSizeOf(Thing t, RoomModel room) => MakeLifeSize(t, room);

        bool MakeLifeSize(Thing t, RoomModel room)
        {
            if (t == null || t.T == null) return false;
            if (room != null) lastRoom = room; else room = lastRoom;
            bool world = InWorld != null && InWorld();
            float life = world ? LifeSize.InWorld(t.Kind) : LifeSize.InRoom(t.Kind);
            t.Return = false;
            lastTouched = t;
            if (life <= 0f) { Said = "I don't know how big that is in life."; return false; }
            for (int k = 0; k < held.Length; k++) if (held[k] == t) held[k] = null;
            if (two == t) two = null;
            var sp = t.T.GetComponent<Spinner>();
            if (sp != null) Destroy(sp);
            var ms = t.T.GetComponent<MadeScale>();
            float now = ms != null ? ms.Target : t.T.localScale.x;
            float unit = now / Mathf.Max(0.001f, t.Size);
            t.Size = life;
            if (ms != null) ms.Target = unit * life; else t.T.localScale = Vector3.one * unit * life;
            if (life > 0.5f)
            {
                Vector3 head = Head != null ? Head.position : Vector3.up;
                Vector3 f = Head != null ? Head.forward : Vector3.forward; f.y = 0f;
                f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                float floor = room != null ? room.FloorY : head.y - 1.2f;
                var mf = t.T.GetComponent<MeshFilter>();
                var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
                bool longOne = mb.z > mb.x * 1.3f && mb.z >= mb.y;
                float depth = life * (longOne ? Mathf.Clamp01(mb.x / Mathf.Max(1e-4f, mb.z)) : Mathf.Clamp01(mb.z / Mathf.Max(1e-4f, Mathf.Max(mb.x, mb.y))));
                float dist = Mathf.Max(0.9f + Mathf.Min(life, LifeSize.RoomMax) * 0.35f, depth * 0.5f + 0.6f);
                // Bigger than a room: it stands out past your furniture, where the walls were.
                if (world && life > LifeSize.RoomMax && room != null) dist = Mathf.Max(dist, room.OuterRadius + depth * 0.5f + 0.5f);
                t.T.position = new Vector3(head.x, floor, head.z) + f * dist;
                // Long things (a lion, a train) are shown side-on, the whole of them in view; tall ones face you.
                Vector3 look = -f;
                t.T.rotation = Quaternion.LookRotation(longOne ? Quaternion.AngleAxis(90f, Vector3.up) * look : look, Vector3.up);
            }
            float real = LifeSize.Metres(t.Kind);
            string against = Measure(real);
            if (real <= life + 0.01f) Said = "Life size: " + LifeSize.Length(real) + "." + (against.Length > 0 ? "\n" + against : "");
            else if (world) Said = "In life it's " + LifeSize.Length(real) + ".\nThis is as big as the world here allows.";
            else Said = "In life it's " + LifeSize.Length(real) + ": it would go through your walls.\nHere it's " + LifeSize.Length(life) + ". Open a world to see it whole.";
            Sfx.Instance?.At("reveal_thump", t.T.position, 0.7f);
            ShowSofas(t, real, room);
            Trace.Info("Maker", "life size", "name", t.Name, "kind", t.Kind, "size_m", life, "life_m", real);
            return true;
        }

        /// <summary>How long an open hand must rest still before a small animal takes it as an offer (seconds).</summary>
        public const float OfferDelay = 0.5f;
        /// <summary>How many times an animal has walked over to an offered hand (for tests and the log).</summary>
        public int Came { get; private set; }
        readonly float[] offer = new float[3];
        readonly Vector3[] lastPalm = new Vector3[3];

        /// <summary>The hand is at the level of the ground this small animal stands on, and near it: how far (metres), or -1.</summary>
        float OfferRange(int h, Thing t)
        {
            if (t == null || t.T == null || t.Size > 0.7f || !LifeSize.IsCreature(t.Kind) || IsHeld(t)) return -1f;
            Vector3 p = Hands[h].Palm;
            float dy = p.y - t.T.position.y;
            Vector3 d = p - t.T.position; d.y = 0f;
            return dy < -0.05f || dy > 0.14f || d.magnitude > 0.6f ? -1f : d.magnitude;
        }

        /// <summary>
        /// An open hand resting still on the ground a small animal stands on, within its reach: where the hand is
        /// (at the animal's own height), or null. The animal walks over to it.
        /// </summary>
        public Vector3? Offered(Thing t)
        {
            if (Hands == null || t == null || t.T == null) return null;
            Vector3? at = null; float best = float.MaxValue;
            for (int h = 0; h < Hands.Count && h < offer.Length; h++)
            {
                if (offer[h] < OfferDelay) continue;
                float far = OfferRange(h, t);
                if (far < 0f || far >= best) continue;
                best = far;
                at = new Vector3(Hands[h].Palm.x, t.T.position.y, Hands[h].Palm.z);
            }
            return at;
        }

        /// <summary>This open hand is held out beside a small animal (so it is not aiming a jump).</summary>
        public bool Offering(int h)
        {
            if (Hands == null || h < 0 || h >= Hands.Count || h >= offer.Length) return false;
            var s = Hands[h];
            if (!s.Tracked || s.Pinching) return false;
            foreach (var t in Things) if (OfferRange(h, t) >= 0f) return true;
            return false;
        }

        /// <summary>A fingertip coming into a made thing (not pinching): it hops, and an animal turns to you.</summary>
        void Touch()
        {
            foreach (var t in Things)
            {
                if (t.T == null || t.R == null || IsHeld(t)) { if (t != null) t.TipIn = false; continue; }
                bool inside = false;
                var b = t.R.bounds;
                b.Expand(0.01f);
                for (int h = 0; h < Hands.Count; h++)
                {
                    var s = Hands[h];
                    // An open, resting fingertip: a hand closing (to pinch, or round it) is on its way to pick it up.
                    if (s.Tracked && !s.Pinching && s.PinchStrength < 0.25f && !s.Grip && b.Contains(s.IndexTip)) inside = true;
                }
                // A touch is a fingertip that comes in and stays open a moment: a hand closing to pinch is a grab.
                if (inside && !t.TipIn) t.TipAt = Time.unscaledTime;
                if (!inside) t.TipAt = -1f;
                if (inside && t.TipAt > 0f && Time.unscaledTime - t.TipAt >= TouchDelay && Time.unscaledTime - t.TouchedAt > TouchAgain)
                {
                    t.TipAt = -1f;
                    t.TouchedAt = Time.unscaledTime;
                    Touches++;
                    LastHandled = Time.unscaledTime;
                    Hop.On(t.T, t.Size);
                    Sfx.Instance?.At("touch", Centre(t), 0.7f);
                    Trace.Info("Maker", "touched", "name", t.Name);
                }
                t.TipIn = inside;
            }
        }

        /// <summary>Things let go at your face go back to where they were picked up.</summary>
        void Glide()
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f);
            foreach (var t in Things)
            {
                if (!t.Return || t.T == null) continue;
                if (IsHeld(t)) { t.Return = false; continue; }
                t.T.position = Vector3.Lerp(t.T.position, t.PickedAt, k);
                if (Vector3.Distance(t.T.position, t.PickedAt) < 0.004f) { t.T.position = t.PickedAt; t.Return = false; }
            }
        }

        /// <summary>
        /// How big something is against a real piece of your furniture ("As long as 6 of your sofas."), or "" when no
        /// sofa or table was scanned. Big things are measured against the sofa, small ones against the table.
        /// </summary>
        public string Measure(float life)
        {
            var room = lastRoom;
            if (room == null || life <= 0f) return "";
            RoomModel.Piece sofa = null, table = room.Table != null && !room.Table.Virtual ? room.Table : null;
            foreach (var p in room.Pieces)
                if (!p.Virtual && string.Equals(p.Label, "COUCH", StringComparison.OrdinalIgnoreCase)) { sofa = p; break; }
            var use = life >= 1.2f && sofa != null ? sofa : table ?? sofa;
            if (use == null) return "";
            return LifeSize.Compare(life, use == sofa ? "sofa" : "table", Mathf.Max(use.Size.x, use.Size.z));
        }

        RoomModel lastRoom;
        /// <summary>The room things were last made in (null before the first).</summary>
        public RoomModel Room => lastRoom;
        MaterialPropertyBlock shadeBlock;

        /// <summary>
        /// Made things belong to the room: each casts a soft shadow on the table, the furniture or the floor under it,
        /// and one let go just above a surface settles onto it. Higher up it stays where you left it, floating.
        /// </summary>
        void Ground()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var t in Things)
            {
                if (t.T == null || t.R == null) continue;
                var b = t.R.bounds;
                Vector3 c = b.center;
                // What it is over: any top its middle is above (so a thing lowered into the table is still "on" it).
                float surface = SurfaceUnder(c, c.y - 0.03f);
                float gap = b.min.y - surface;
                bool free = !IsHeld(t) && !t.Return && t.T.GetComponent<Hop>() == null && t.T.GetComponent<Thrown>() == null;
                if (free && gap > 0.004f && gap < SettleBelow)
                {
                    float fall = Mathf.Min(gap, dt * (0.35f + gap * 8f));
                    t.T.position += Vector3.down * fall;
                    gap -= fall;
                }
                else if (free && gap < -0.004f && t.Size <= 1.5f)
                {
                    // Let go with its feet inside the table (or the floor): it comes up to stand on top.
                    float rise = Mathf.Min(-gap, dt * (0.5f + -gap * 10f));
                    t.T.position += Vector3.up * rise;
                    gap += rise;
                }
                if (free && Mathf.Abs(gap) < 0.03f)
                {
                    // Standing on something: upright (it keeps the way it faces), and out of the world's own build.
                    var e = t.T.eulerAngles;
                    var up = Quaternion.Euler(0f, e.y, 0f);
                    if (Quaternion.Angle(t.T.rotation, up) > 0.5f && t.T.GetComponent<Spinner>() == null) t.T.rotation = Quaternion.RotateTowards(t.T.rotation, up, 240f * dt);
                    var table = lastRoom != null ? lastRoom.Table : null;
                    if (TableKeepOut > 0f && table != null && Mathf.Abs(surface - table.TopY) < 0.01f)
                    {
                        Vector3 off = t.T.position - table.TopCenter; off.y = 0f;
                        float keep = TableKeepOut + Mathf.Min(0.2f, Mathf.Max(b.extents.x, b.extents.z));
                        if (off.magnitude < keep)
                        {
                            Vector3 dir = off.sqrMagnitude > 1e-5f ? off.normalized : (Head != null ? Flat(Head.position - table.TopCenter) : Vector3.right);
                            Vector3 want = table.TopCenter + dir * keep;
                            Vector3 local = Quaternion.Inverse(table.Rotation) * (want - table.TopCenter);
                            // Only if there is table left out there: on a small table it stays where it is.
                            if (Mathf.Abs(local.x) <= table.Size.x * 0.5f && Mathf.Abs(local.z) <= table.Size.z * 0.5f)
                                t.T.position += dir * Mathf.Min(keep - off.magnitude, dt * 0.5f);
                        }
                    }
                }
                if (Shadow == null) continue;
                if (t.Shade == null) t.Shade = Util.MeshObject("Shadow " + t.Name, transform, Util.Quad(Color.white, "Shadow"), Shadow).GetComponent<Renderer>();
                // Darkest touching the surface, gone half a metre up.
                float k = Mathf.Clamp01(1f - Mathf.Max(0f, gap) / 0.6f);
                bool show = k > 0.02f && gap > -0.08f;
                t.Shade.enabled = show;
                if (!show) continue;
                var st = t.Shade.transform;
                st.SetPositionAndRotation(new Vector3(c.x, surface + 0.002f, c.z), Quaternion.Euler(90f, 0f, 0f));
                float spread = 1f + (1f - k) * 0.5f;
                st.localScale = new Vector3(Mathf.Max(0.03f, b.size.x * 1.3f + 0.02f) * spread, Mathf.Max(0.03f, b.size.z * 1.3f + 0.02f) * spread, 1f);
                shadeBlock ??= new MaterialPropertyBlock();
                shadeBlock.SetColor("_Color", new Color(0f, 0f, 0f, 0.5f * k * k));
                t.Shade.SetPropertyBlock(shadeBlock);
            }
        }

        /// <summary>The height of what is under a point: the top of the table or furniture it is over and above, or the floor.</summary>
        /// <summary>A step from one point to another keeps a thing on the same ground: standing now, and the same surface there.</summary>
        public bool SameGround(Vector3 from, Vector3 to)
        {
            float here = SurfaceUnder(from + Vector3.up * 0.02f, from.y + 0.01f);
            if (Mathf.Abs(from.y - here) > 0.02f) return false;   // not standing on anything (held up, or floating)
            // Not into the world's own build in the middle of the table.
            var tb = lastRoom != null ? lastRoom.Table : null;
            if (TableKeepOut > 0f && tb != null && Mathf.Abs(here - tb.TopY) < 0.01f)
            {
                Vector3 o = to - tb.TopCenter; o.y = 0f;
                if (o.magnitude < TableKeepOut + 0.08f) return false;
            }
            // Not into another thing.
            foreach (var other in Things)
            {
                if (other.T == null || Vector3.Distance(other.T.position, from) < 0.01f) continue;
                Vector3 d = other.T.position - to; d.y = 0f;
                if (d.magnitude < Mathf.Min(0.25f, other.Size * 0.4f) + 0.05f && Mathf.Abs(other.T.position.y - from.y) < 0.2f) return false;
            }
            float there = SurfaceUnder(to + Vector3.up * 0.02f, from.y + 0.01f);
            if (Mathf.Abs(there - here) > 0.01f) return false;
            // A little way in from the edge of a table top.
            float inner = SurfaceUnder(to + (to - from).normalized * 0.05f + Vector3.up * 0.02f, from.y + 0.01f);
            return Mathf.Abs(inner - here) <= 0.01f;
        }

        float SurfaceUnder(Vector3 p, float bottom)
        {
            var room = lastRoom;
            float best = room != null ? room.FloorY : (Head != null ? Head.position.y - 1.2f : 0f);
            if (room == null) return best;
            foreach (var piece in room.Pieces) best = Over(piece, p, bottom, best);
            return Over(room.Table, p, bottom, best);
        }

        static float Over(RoomModel.Piece piece, Vector3 p, float bottom, float best)
        {
            if (piece == null) return best;
            float top = piece.TopY;
            if (top > bottom + 0.03f || top <= best) return best;
            var local = Quaternion.Inverse(piece.Rotation) * (p - piece.Center);
            return Mathf.Abs(local.x) <= piece.Size.x * 0.5f && Mathf.Abs(local.z) <= piece.Size.z * 0.5f ? top : best;
        }

        /// <summary>
        /// "As long as 6 of your sofas", shown: faint outlines of your own scanned sofa laid end to end on the floor
        /// beside the life-size thing, for a few seconds.
        /// </summary>
        void ShowSofas(Thing t, float life, RoomModel room)
        {
            SofasShown = 0;
            if (Ghost == null || room == null || t == null || t.T == null || life < 1.2f) return;
            RoomModel.Piece sofa = null;
            foreach (var p in room.Pieces) if (!p.Virtual && string.Equals(p.Label, "COUCH", StringComparison.OrdinalIgnoreCase)) { sofa = p; break; }
            if (sofa == null) return;
            float len = Mathf.Max(0.4f, Mathf.Max(sofa.Size.x, sofa.Size.z)), depth = Mathf.Max(0.3f, Mathf.Min(sofa.Size.x, sofa.Size.z));
            int n = Mathf.Clamp(Mathf.RoundToInt(life / len), 1, 8);
            Vector3 head = Head != null ? Head.position : Vector3.up;
            Vector3 toYou = head - t.T.position; toYou.y = 0f;
            toYou = toYou.sqrMagnitude > 1e-4f ? toYou.normalized : Vector3.back;
            Vector3 along = Vector3.Cross(Vector3.up, toYou).normalized;
            float floor = room.FloorY;
            var bounds = t.R != null ? t.R.bounds : new Bounds(t.T.position, Vector3.one * t.Size);
            float near = Mathf.Max(0.3f, Mathf.Min(bounds.extents.x, bounds.extents.z));
            Vector3 start = new Vector3(t.T.position.x, floor, t.T.position.z) + toYou * (near + depth * 0.5f + 0.15f) - along * (len * (n - 1) * 0.5f);
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0f, sofa.Size.y * 0.5f, 0f), new Vector3(len * 0.96f, sofa.Size.y, depth), new Color(0.62f, 0.86f, 1f));
            var mesh = mb.ToMesh("sofa outline");
            for (int i = 0; i < n; i++)
            {
                var go = Util.MeshObject("Your sofa " + (i + 1), transform, mesh, Ghost);
                go.transform.SetPositionAndRotation(start + along * (len * i), Quaternion.FromToRotation(Vector3.right, along));
                Destroy(go, 7f);
            }
            SofasShown = n;
            Trace.Info("Maker", "measured in your sofas", "count", n, "sofa_m", len, "life_m", life);
        }

        /// <summary>Flicked away: it flies off the way it was thrown and is gone.</summary>
        void Toss(Thing t, Vector3 velocity)
        {
            for (int k = 0; k < held.Length; k++) if (held[k] == t) held[k] = null;
            if (two == t) two = null;
            Things.Remove(t);
            if (t.Shade != null) Util.Kill(t.Shade.gameObject);
            if (t.T == null) return;
            var ms = t.T.GetComponent<MadeScale>();
            if (ms != null) Destroy(ms);
            var sp = t.T.GetComponent<Spinner>();
            if (sp != null) Destroy(sp);
            t.T.gameObject.AddComponent<Thrown>().Go(velocity);
            Tossed++;
            Sfx.Instance?.At("reveal_whoosh", t.T.position, 0.7f);
            Hints.Instance?.Learned("toss");
            Trace.Info("Maker", "thrown away", "name", t.Name, "speed", velocity.magnitude);
        }

        void Remove(Thing t)
        {
            for (int h = 0; h < held.Length; h++) if (held[h] == t) held[h] = null;
            Things.Remove(t);
            if (t.Shade != null) Util.Kill(t.Shade.gameObject);
            if (t.T != null) Util.Kill(t.T.gameObject);
        }

        public void Clear()
        {
            while (Things.Count > 0) Remove(Things[Things.Count - 1]);
            Working = false;
        }

        void Update()
        {
            // The shimmer where the next thing will form.
            if (Working && shimmer == null && Glow != null) shimmer = Effects.Halo("Making", transform, Glow, 0.3f, new Color(1f, 0.8f, 0.5f) * 0.7f);
            if (shimmer != null)
            {
                shimmer.SetActive(Working);
                if (Working)
                {
                    shimmer.transform.position = Spot("front", null, 0, 1, 0.2f);
                    shimmer.transform.localScale = Vector3.one * (0.26f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f));
                }
            }
            Glide();
            Ground();
            if (Hands == null) return;
            // An open hand held still is an offer: a small animal on the same ground walks over to it.
            for (int h = 0; h < Hands.Count && h < offer.Length; h++)
            {
                var s = Hands[h];
                // Still means still: a hand on its way somewhere (or a jump in tracking) starts the wait again.
                float speed = Vector3.Distance(s.Palm, lastPalm[h]) / Mathf.Max(1e-3f, Time.unscaledDeltaTime);
                lastPalm[h] = s.Palm;
                bool open = s.Tracked && !s.Pinching && s.PinchStrength < 0.4f && held[h] == null && speed < 0.25f;
                offer[h] = open ? offer[h] + Time.unscaledDeltaTime : 0f;
            }
            Touch();
            TwoHands();
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                if (h < seenAt.Length && s.Tracked) seenAt[h] = Time.unscaledTime;
                if (h < seen.Length)
                {
                    if (s.Tracked)
                    {
                        float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
                        Vector3 step = s.Pinch - lastPinch[h];
                        if (!seen[h] || step.magnitude > 0.25f) handVel[h] = Vector3.zero;
                        else handVel[h] = Vector3.Lerp(handVel[h], step / dt, 1f - Mathf.Exp(-dt * 30f));
                        lastPinch[h] = s.Pinch;
                        seen[h] = true;
                    }
                    else { seen[h] = false; handVel[h] = Vector3.zero; }
                }
                if (held[h] != null)
                {
                    var t = held[h];
                    // The hand left the cameras' view for a moment (looking away, reaching down): still holding.
                    if (!s.Tracked && t.T != null && h < seenAt.Length && Time.unscaledTime - seenAt[h] < LostGrace) continue;
                    bool holding = h < byFist.Length && byFist[h] ? s.Grip || s.Pinching : s.Pinching;
                    if (!s.Tracked || !holding || t.T == null)
                    {
                        // Let go with a twist of the wrist and it keeps turning (revolves), slowing down.
                        if (t.T != null && s.Tracked && two != t && h < handVel.Length && handVel[h].magnitude > TossSpeed)
                        {
                            Toss(t, handVel[h]);
                            continue;
                        }
                        if (t.T != null && s.Tracked && two != t)
                        {
                            // The turn over the last few frames, not one: a jittery release is not a flick.
                            float spin = spinRate[h];
                            if (Mathf.Abs(spin) > SpinFlick) Spinner.On(t.T, Mathf.Clamp(spin, -720f, 720f), 0.6f);
                        }
                        // Let go up at your face (after looking at it): it goes back where it came from, not hang in your view.
                        if (t.T != null && two != t && Head != null && Vector3.Distance(Centre(t), Head.position) < FaceReach) t.Return = true;
                        Trace.Info("Maker", "let go", "name", t.Name, "curl", s.Curl, "by", h < byFist.Length && byFist[h] ? "whole hand" : "pinch", "tracked", s.Tracked);
                        LastHandled = Time.unscaledTime;
                        held[h] = null; if (two == t) two = null; continue;
                    }
                    if (two != null && two == t) continue;   // both hands: TwoHands() moves it
                    float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 25f);
                    t.T.position = Vector3.Lerp(t.T.position, s.Pinch + s.PalmRot * gripPos[h], k);
                    var wantRot = s.PalmRot * gripRot[h];
                    float dtr = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
                    float turning = Vector3.Dot(SpinAxis(lastRot[h], s.PalmRot), Vector3.up) / dtr;
                    spinRate[h] = Mathf.Lerp(spinRate[h], Mathf.Clamp(turning, -2000f, 2000f), 1f - Mathf.Exp(-dtr * 25f));
                    lastRot[h] = s.PalmRot;
                    if (Quaternion.Angle(t.T.rotation, wantRot) > 20f) Turns++;
                    t.T.rotation = Quaternion.Slerp(t.T.rotation, wantRot, k);
                    continue;
                }
                if (s.Tracked) lastRot[h] = s.PalmRot;
                // A whole hand closing round a thing picks it up too (people grab an animal, they don't pinch it).
                bool fistEdge = h < wasFist.Length && s.Tracked && s.Fist && !wasFist[h];
                if (fistEdge) fistAt[h] = Time.unscaledTime;
                bool fistDown = fistEdge || (h < wasFist.Length && s.Tracked && s.Fist && Time.unscaledTime - fistAt[h] < FistReach);
                if (h < wasFist.Length) wasFist[h] = s.Tracked && s.Fist;
                if (!s.Tracked || !(Hands.PinchDown(h) || fistDown) || (Blocked != null && Blocked(h))) continue;
                Thing best = null;
                float bestD = float.MaxValue;
                foreach (var t in Things)
                {
                    if (t.T == null) continue;
                    float d = Outside(t, s.Pinch);
                    if (fistDown) d = Mathf.Min(d, Outside(t, s.Palm));
                    if (d < 0f && d < bestD) { bestD = d; best = t; }
                }
                if (h < byFist.Length) byFist[h] = fistDown && !Hands.PinchDown(h);
                if (best != null && fistDown) { Gripped++; Trace.Info("Maker", "whole-hand grip", "name", best.Name, "curl", s.Curl); }
                // Nothing in reach: whatever the hand points at (up to 5 m) comes to it (a pinch only).
                bool far = false;
                if (best == null && (FarBlocked == null || !FarBlocked(h)))
                {
                    best = Pointed(h);
                    // A whole hand closing while it points at something out of reach brings it too (not at something
                    // near: that hand is closing on the air beside it, or on something else).
                    if (best != null && fistDown && !Hands.PinchDown(h) && Vector3.Distance(Centre(best), s.Pinch) < 0.45f) best = null;
                    far = best != null;
                }
                if (best != null)
                {
                    held[h] = best;
                    spinRate[h] = 0f;
                    var hop = best.T.GetComponent<Hop>();
                    if (hop != null) Destroy(hop);
                    Trace.Info("Maker", "grabbed", "name", best.Name, "hand", h, "far", far);
                    lastTouched = best;
                    if (!best.Return) best.PickedAt = best.T.position;
                    best.Return = false;
                    var sp = best.T.GetComponent<Spinner>();
                    if (sp != null) Destroy(sp);
                    var inv = Quaternion.Inverse(s.PalmRot);
                    gripPos[h] = far ? inv * (Vector3.down * best.Size * 0.45f) : inv * (best.T.position - s.Pinch);
                    if (far) { Trace.Info("Maker", "pulled to hand", "name", best.Name); Hints.Instance?.Learned("point"); }
                    gripRot[h] = inv * best.T.rotation;
                    Sfx.Instance?.At("grab", s.Pinch, 0.7f);
                    int other = h == 0 ? 1 : h == 1 ? 0 : -1;
                    if (other >= 0 && held[other] == best) StartTwo(best);
                    else if (Hints.Instance != null && Hints.Instance.Wants("turn", 2))
                        Hints.Instance.Request("turn", best.T.position + Vector3.up * (best.Size * 0.6f + 0.05f), "Turn your hand to turn it.\nBoth hands: turn it and resize it.", 1);
                    else if (Hints.Instance != null && Hints.Instance.Wants("inspect", 2))
                        Hints.Instance.Request("inspect", best.T.position + Vector3.up * (best.Size * 0.6f + 0.05f), "Bring it close to look at it.\nFlick it away to throw it out.", 1);
                }
            }
        }
    }
}

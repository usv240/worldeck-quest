using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>One piece to gather: floats at its spot, glows when a hand comes near, rides the pinch.</summary>
    public sealed class GatherItem : MonoBehaviour
    {
        public Vector3 Home;
        public int Index;
        public bool Placed;
        public int HeldBy = -1;
        public float Radius = 0.06f;
        public GameObject Halo;
        public float Appear;        // 0..1 entrance
        public float ReturnAt = -1f; // time to start drifting home after a miss
        /// <summary>When it last came to a hand that closed twice short of it (-10: never).</summary>
        public float CameAt = -10f;

        Vector3 holdOffset;
        float hover;
        float bobPhase;

        void Awake() { bobPhase = UnityEngine.Random.value * 6f; }

        public void Grab(int hand, Vector3 pinch)
        {
            HeldBy = hand;
            holdOffset = transform.position - pinch;
            if (holdOffset.magnitude > 0.05f) holdOffset = holdOffset.normalized * 0.05f;
            ReturnAt = -1f;
        }

        public void Follow(Vector3 pinch)
        {
            holdOffset = Vector3.Lerp(holdOffset, Vector3.zero, Time.deltaTime * 6f);
            transform.position = Vector3.Lerp(transform.position, pinch + holdOffset, 1f - Mathf.Exp(-Time.deltaTime * 30f));
        }

        public void SetHover(bool on) => hover = Mathf.MoveTowards(hover, on ? 1f : 0f, Time.deltaTime * 8f);

        void Update()
        {
            if (Appear < 1f) Appear = Mathf.Min(1f, Appear + Time.deltaTime / 0.5f);
            float s = Util.Pop(Appear) * (1f + 0.08f * hover);
            if (!Placed) transform.localScale = Vector3.one * s;
            if (HeldBy < 0 && !Placed)
            {
                if (ReturnAt > 0f && Time.time >= ReturnAt)
                {
                    transform.position = Vector3.Lerp(transform.position, Home, 1f - Mathf.Exp(-Time.deltaTime * 3f));
                    if (Vector3.Distance(transform.position, Home) < 0.005f) ReturnAt = -1f;
                }
                else if (ReturnAt < 0f)
                {
                    var p = Home + Vector3.up * (0.01f * Mathf.Sin(Time.time * 1.6f + bobPhase));
                    transform.position = Vector3.Lerp(transform.position, p, 1f - Mathf.Exp(-Time.deltaTime * 8f));
                    transform.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
                }
            }
            if (Halo != null)
            {
                Halo.SetActive(!Placed);
                Halo.transform.localScale = Vector3.one * (0.16f + 0.06f * hover + 0.02f * Mathf.Sin(Time.time * 2.4f + bobPhase)) / Mathf.Max(0.01f, transform.localScale.x);
            }
        }
    }


    /// <summary>
    /// The gather-and-build task of a world: N themed pieces appear within reach, the player grabs each
    /// and puts it on the real table, the table builds the result. No timer and no failing: a missed drop
    /// floats home. Records how long it took.
    /// </summary>
    public sealed class Gather : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material World, Glow;
        public BuildSite Site;
        public float GrabRadius = 0.08f;
        public float HoverRadius = 0.14f;
        /// <summary>
        /// A whole hand closing takes a piece this far from the pinch point: a fist is bigger than a pinch (Nov 15
        /// on the headset: hands closed round pieces 11 and 15 cm off and missed, 7 misses for 5 pieces).
        /// </summary>
        public const float FistGrabRadius = 0.13f;
        /// <summary>Whole-hand grabs this session (people close a hand round a piece more often than they pinch it).</summary>
        public int Gripped { get; private set; }
        /// <summary>Pieces that came to a hand after it closed twice short of them.</summary>
        public int Came { get; private set; }
        readonly int[] missItem = { -1, -1, -1, -1 };
        readonly float[] missAt = { -10f, -10f, -10f, -10f };
        readonly bool[] wasFist = new bool[4], byFist = new bool[4];
        readonly float[] fistAt = { -10f, -10f, -10f, -10f };

        /// <summary>From the pinch point, or from the middle of a closing hand, whichever is nearer.</summary>
        static float HandTo(HandInput.State s, Vector3 p)
        {
            float d = Vector3.Distance(p, s.Pinch);
            if (s.Palm != Vector3.zero && !s.Controller) d = Mathf.Min(d, Vector3.Distance(p, s.Palm + s.PalmNormal * 0.035f) - 0.02f);
            return d;
        }

        public bool Running { get; private set; }
        public int Total { get; private set; }
        public int PlacedCount { get; private set; }
        public float StartedAt { get; private set; }
        public float Seconds => Running || PlacedCount > 0 ? (finishedAt > 0 ? finishedAt : Time.time) - StartedAt : 0f;
        public readonly List<GatherItem> Items = new List<GatherItem>();
        public event Action<GatherItem> Placed;
        public event Action Completed;
        public event Action<GatherItem> Grabbed;

        RoomModel room;
        RoomModel.Piece table;
        readonly List<Vector3> sockets = new List<Vector3>();
        readonly GatherItem[] held = new GatherItem[3];
        float finishedAt;
        int reserved;   // sockets promised to pieces on their way (PlacedCount counts only those that landed)
        GatherItem gazed;
        float gazeStart;
        /// <summary>No hands or controllers: look at a piece to bring it to the table.</summary>
        public bool EyesOnly;
        Recipe recipe;
        Transform root;
        GameObject dropRing;
        readonly List<Transform> dots = new List<Transform>();
        GatherItem hoverItem;
        MaterialPropertyBlock ringBlock;

        /// <summary>Where the next piece goes on the table.</summary>
        public Vector3 NextSocket => sockets.Count > 0 ? sockets[Mathf.Min(reserved, sockets.Count - 1)] : (table != null ? table.TopCenter : Vector3.zero);
        public bool AnyHeld => held[0] != null || held[1] != null || held[2] != null;

        public bool Holding(int hand) => hand >= 0 && hand < held.Length && held[hand] != null;
        /// <summary>Hand index → something else owns this pinch (a made thing nearer the hand).</summary>
        public Func<int, bool> Blocked;

        /// <summary>How far the hand's pinch is outside the grab reach of the nearest piece (negative: inside).</summary>
        public float Nearest(int h)
        {
            if (!Running || Hands == null) return float.MaxValue;
            var s = Hands[h];
            if (!s.Tracked) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var it in Items) if (!it.Placed && it.HeldBy < 0) best = Mathf.Min(best, Vector3.Distance(it.transform.position, s.Pinch) - GrabRadius);
            return best;
        }

        /// <summary>
        /// A piece floating in front of a row of choices hides it, and a hand reaching for a piece just behind one
        /// would press it on the way (a shell over "What can I do here?" in the Nov 15 captures). For a moment after a
        /// row appears, a piece in its way drifts just clear of it: above, below or to a side, whichever is the
        /// shortest move to a spot the gather would have chosen itself (<see cref="ClearOf"/>). Returns how many moved.
        /// </summary>
        public int KeepClearOf(Vector3 eye, IReadOnlyList<PokeButton> row)
        {
            if (!Running || row == null || row.Count == 0 || row[0] == null) return 0;
            // Once the row has found its place (its first frames are still under Juniper), and only then.
            float age = Time.unscaledTime - row[0].BornAt;
            if (age < 0.1f || age > 1.5f) return 0;
            if (!RowView.Of(eye, row, out var view)) return 0;
            Vector3 site = table != null ? table.TopCenter : NextSocket;
            int moved = 0;
            foreach (var it in Items)
            {
                if (it == null || it.Placed || it.HeldBy >= 0 || !view.Hides(it.Home, it.Radius)) continue;
                if (Time.time - it.CameAt < 5f) continue;   // it just came to your hand: it stays there
                otherHomes.Clear();
                foreach (var o in Items) if (o != null && o != it && !o.Placed) otherHomes.Add(o.HeldBy >= 0 ? o.transform.position : o.Home);
                var to = ClearOf(room, view, it.Home, it.Radius, site, otherHomes);
                if (!to.HasValue) continue;
                Trace.Info("Gather", "a piece moved clear of the choices", "piece", it.Index, "cm", Mathf.RoundToInt(Vector3.Distance(to.Value, it.Home) * 100f));
                it.Home = to.Value;
                moved++;
            }
            return moved;
        }

        readonly List<Vector3> otherHomes = new List<Vector3>();

        /// <summary>
        /// A row of choices seen from the eyes, as one rectangle in a frame facing them (x right, y up, at the row's
        /// distance). Made from the buttons on show, or from where buttons would be (the room sweep).
        /// </summary>
        public struct RowView
        {
            public Vector3 Eye, Fwd, Right, Up;
            public float Depth, MinX, MaxX, MinY, MaxY;

            public static bool Of(Vector3 eye, IReadOnlyList<PokeButton> row, out RowView v)
            {
                v = default;
                if (row == null || row.Count == 0 || row[0] == null) return false;
                var at = new List<Vector3>();
                var size = new List<Vector2>();
                foreach (var b in row) if (b != null) { at.Add(b.transform.position); size.Add(b.Size); }
                return Of(eye, at, size, row[0].transform.right, out v);
            }

            public static bool Of(Vector3 eye, IList<Vector3> at, IList<Vector2> size, Vector3 rowRight, out RowView v)
            {
                v = default;
                if (at == null || at.Count == 0) return false;
                Vector3 center = Vector3.zero;
                foreach (var p in at) center += p;
                center /= at.Count;
                Vector3 fwd = center - eye;
                float depth = fwd.magnitude;
                if (depth < 0.1f) return false;
                fwd /= depth;
                Vector3 right = Vector3.ProjectOnPlane(rowRight, fwd);
                if (right.sqrMagnitude < 1e-6f) return false;
                right.Normalize();
                v.Eye = eye; v.Fwd = fwd; v.Right = right; v.Up = Vector3.Cross(fwd, right); v.Depth = depth;
                v.MinX = v.MinY = float.MaxValue;
                v.MaxX = v.MaxY = float.MinValue;
                for (int i = 0; i < at.Count; i++)
                {
                    Vector3 o = at[i] - center;
                    float x = Vector3.Dot(o, v.Right), y = Vector3.Dot(o, v.Up);
                    v.MinX = Mathf.Min(v.MinX, x - size[i].x * 0.5f); v.MaxX = Mathf.Max(v.MaxX, x + size[i].x * 0.5f);
                    v.MinY = Mathf.Min(v.MinY, y - size[i].y * 0.5f); v.MaxY = Mathf.Max(v.MaxY, y + size[i].y * 0.5f);
                }
                return true;
            }

            /// <summary>Where p falls on the row, seen from the eyes, and k (the row's distance over p's). False unless
            /// p is at least 8 cm from the eyes and at most 15 cm behind the row.</summary>
            public bool Project(Vector3 p, out float x, out float y, out float k)
            {
                Vector3 d = p - Eye;
                float z = Vector3.Dot(d, Fwd);
                x = y = k = 0f;
                if (z < 0.08f || z > Depth + 0.15f) return false;
                k = Depth / z;
                x = Vector3.Dot(d, Right) * k;
                y = Vector3.Dot(d, Up) * k;
                return true;
            }

            /// <summary>True if a piece this size at p covers part of the row from the eyes, or floats just behind it
            /// (a hand reaching for it would go through the buttons).</summary>
            public bool Hides(Vector3 p, float radius)
            {
                if (!Project(p, out float x, out float y, out float k)) return false;
                float m = (radius + 0.02f) * k;
                return x >= MinX - m && x <= MaxX + m && y >= MinY - m && y <= MaxY + m;
            }
        }

        /// <summary>How far pieces float from the eyes, horizontally (0.58 m and more was out of a seated reach on the headset).</summary>
        public const float NearReach = 0.31f, FarReach = 0.58f;

        /// <summary>
        /// Where a piece at <paramref name="home"/> goes so that it no longer hides the row: the shortest move out to a
        /// side, below or above, to a spot the gather would have chosen itself (<see cref="SpotOk"/>, within the reach,
        /// between the table and the eyes). Null when it is not in the way, or when no such spot is near (it stays).
        /// </summary>
        public static Vector3? ClearOf(RoomModel room, RowView row, Vector3 home, float radius, Vector3 site, IList<Vector3> others)
        {
            if (!row.Hides(home, radius) || !row.Project(home, out float px, out float py, out float k)) return null;
            Vector3 eye = row.Eye;
            Band(room, eye, out float low, out float high);
            float m = (radius + 0.02f) * k;
            Vector3? best = null;
            float bestMove = float.MaxValue;
            // Just past the row's edge at the piece's own distance, then 4, 8 and 12 cm further in case that is in the
            // furniture or out of reach; each pulled into the reach and the heights pieces float at.
            for (int c = 0; c < 4; c++)
                for (int step = 0; step < 4; step++)
                {
                    float extra = (0.01f + 0.04f * step) * k;
                    float x = c == 0 ? row.MinX - m - extra : c == 1 ? row.MaxX + m + extra : px;
                    float y = c == 2 ? row.MinY - m - extra : c == 3 ? row.MaxY + m + extra : py;
                    Vector3 p = IntoReach(eye, eye + (row.Fwd * row.Depth + row.Right * x + row.Up * y) / k, low, high);
                    float move = Vector3.Distance(p, home);
                    if (move >= bestMove || row.Hides(p, radius) || !SpotOk(room, eye, p, site, others)) continue;
                    best = p;
                    bestMove = move;
                }
            return best;
        }

        static Vector3 IntoReach(Vector3 eye, Vector3 p, float low, float high)
        {
            p.y = Mathf.Clamp(p.y, low, high);
            Vector3 flat = new Vector3(p.x - eye.x, 0f, p.z - eye.z);
            float d = flat.magnitude;
            if (d > 1e-4f && (d < NearReach || d > FarReach))
            {
                flat *= Mathf.Clamp(d, NearReach, FarReach) / d;
                p.x = eye.x + flat.x;
                p.z = eye.z + flat.z;
            }
            return p;
        }

        /// <summary>The heights pieces float at: from a little above the table to 15 cm below the eyes, never at or
        /// above them (the room sweep found high counters pushing pieces up there).</summary>
        public static void Band(RoomModel room, Vector3 head, out float low, out float high)
        {
            var table = room != null ? room.Table : null;
            high = head.y - 0.15f;
            low = Mathf.Min((table != null ? table.TopY : head.y - 0.6f) + 0.12f, head.y - 0.32f);
            if (high < low + 0.08f) low = high - 0.08f;
        }

        /// <summary>
        /// A spot a piece may float at, where it first appears and wherever it moves to: out of the furniture and the
        /// table (the virtual one too), 20 cm from the walls and on your side of them, 22 cm from the build, and 16 cm
        /// from the other pieces.
        /// </summary>
        public static bool SpotOk(RoomModel room, Vector3 head, Vector3 p, Vector3 site, IList<Vector3> others)
        {
            if (room != null)
            {
                if (room.InsideFurniture(p, 0.07f)) return false;
                if (room.Table != null && room.Table.Contains(p, 0.07f)) return false;
                if (room.DistanceToWalls(p) < 0.2f) return false;
                if (room.AcrossWall(head, p)) return false;
            }
            if (Vector3.Distance(new Vector3(p.x, site.y, p.z), site) < 0.22f) return false;
            if (others != null) foreach (var q in others) if (Vector3.Distance(p, q) < 0.16f) return false;
            return true;
        }

        /// <summary>True if hand <paramref name="h"/> is pinching near an item (so a pinch there is a grab, not a button).</summary>
        public bool Near(int h)
        {
            if (!Running || Hands == null) return false;
            var s = Hands[h];
            if (!s.Tracked) return false;
            foreach (var it in Items) if (!it.Placed && Vector3.Distance(it.transform.position, s.Pinch) < HoverRadius) return true;
            return false;
        }

        public void Begin(Recipe r, RoomModel roomModel, int count)
        {
            Stop();
            recipe = r;
            room = roomModel;
            table = room.Table;
            Total = Mathf.Clamp(count, 1, 8);
            PlacedCount = 0;
            reserved = 0;
            gazed = null;
            finishedAt = 0f;
            root = new GameObject("Gather").transform;
            root.SetParent(transform, false);

            // Sockets on the table top, a ring around the build.
            sockets.Clear();
            float ringR = Mathf.Clamp(Mathf.Min(table.Size.x, table.Size.z) * 0.32f, 0.09f, 0.2f);
            for (int i = 0; i < Total; i++)
            {
                float a = (i / (float)Total) * Mathf.PI * 2f + 0.3f;
                var local = new Vector3(Mathf.Cos(a) * ringR, table.Size.y + 0.035f, Mathf.Sin(a) * ringR * 0.75f);
                sockets.Add(table.ToWorld(local));
            }

            var spots = Spots(room, Head != null ? Head.position : room.Head, Head != null ? Head.forward : room.Forward, Total, r.Seed);
            var colors = WorldColors.For(r);
            var rng = new Rng(r.Seed * 19 + 3);
            string kind = r.Def.Item.Replace(' ', '_');
            for (int i = 0; i < spots.Count; i++)
            {
                var mb = new MeshBuilder();
                Props.Build(mb, kind, colors, rng);
                var go = Util.MeshObject("Item " + kind, root, mb.ToMesh(kind), World);
                go.transform.position = spots[i];
                go.transform.localScale = Vector3.one * 0.001f;
                var it = go.AddComponent<GatherItem>();
                it.Home = spots[i];
                it.Index = i;
                it.Appear = -i * 0.35f;   // one after another
                it.Halo = Effects.Halo("Halo", go.transform, Glow, 0.16f, WorldColors.Hex(r.Def.Palette.Emissive, Color.white) * 0.45f);
                Items.Add(it);
            }
            Total = Items.Count;
            Running = Total > 0;
            StartedAt = Time.time;
            Trace.Info("Gather", "pieces placed around you", "count", Total, "kind", kind, "table", table.Virtual ? "virtual" : table.Label, "first_at", spots.Count > 0 ? spots[0] : Vector3.zero);
            // How far each is from your eyes (flat, and below them): what "out of reach" looked like, if it comes up again.
            if (Head != null)
            {
                var far = new System.Text.StringBuilder();
                foreach (var sp in spots)
                {
                    Vector3 d = sp - Head.position;
                    far.Append(new Vector2(d.x, d.z).magnitude.ToString("0.00")).Append('/').Append((-d.y).ToString("0.00")).Append(' ');
                }
                Trace.Info("Gather", "piece distances (flat m / below eyes m)", "each", far.ToString().Trim());
            }
            if (Running) Sfx.Instance?.At("item_appear", spots[0], 0.8f);
        }

        // Which task the pieces belong to: a piece still gliding to the table when the world closes stops there.
        int run;

        public void Stop()
        {
            run++;
            Running = false;
            Items.Clear();
            for (int i = 0; i < held.Length; i++) held[i] = null;
            if (root != null) Util.Kill(root.gameObject);
            root = null;
            dropRing = null;
            dots.Clear();
            hoverItem = null;
        }

        void Update()
        {
            if (!Running || Hands == null) return;
            hoverItem = null;
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                bool fistNow = s.Tracked && s.Fist && !s.Pointing;
                bool fistEdge = fistNow && !wasFist[h];
                wasFist[h] = fistNow;
                if (fistEdge) fistAt[h] = Time.unscaledTime;
                // A hand that closed a moment before it reached the piece still takes it (people close as they reach).
                bool fistDown = fistNow && Time.unscaledTime - fistAt[h] < Maker.FistReach;
                if (held[h] != null)
                {
                    var it = held[h];
                    // Forgiving hands (what playtests of hand games keep finding): the piece stays in your hand
                    // through a short tracking dropout, and a pinch has to read open for a moment to let go.
                    float now = Time.unscaledTime;
                    if (!s.Tracked) { if (lostAt[h] < 0f) lostAt[h] = now; if (now - lostAt[h] > LostGrace) Release(h); continue; }
                    lostAt[h] = -1f;
                    bool holding = byFist[h] ? s.Grip || s.Pinching : s.Pinching;
                    if (!holding) { if (openAt[h] < 0f) openAt[h] = now; if (now - openAt[h] >= OpenGrace) { Release(h); continue; } }
                    else openAt[h] = -1f;
                    it.Follow(s.Pinch);
                    continue;
                }
                lostAt[h] = -1f; openAt[h] = -1f;
                if (!s.Tracked) continue;
                GatherItem nearest = null;
                float best = HoverRadius;
                foreach (var it in Items)
                {
                    if (it.Placed || it.HeldBy >= 0 || it.Appear < 0.6f) continue;
                    float d = HandTo(s, it.transform.position);
                    if (d < best) { best = d; nearest = it; }
                }
                foreach (var it in Items) if (it.HeldBy < 0 && !it.Placed) it.SetHover(it == nearest);
                if (nearest != null) hoverItem = nearest;
                bool closing = Hands.PinchDown(h) || fistDown;
                // A whole hand takes what it closes round: a glowing (hovered) piece. A pinch needs to be closer.
                float reach = fistDown && !Hands.PinchDown(h) ? FistGrabRadius : GrabRadius;
                // A hand that closed on nothing, with a piece not far off: worth knowing about (which pieces can't be had).
                if ((Hands.PinchDown(h) || fistEdge) && (nearest == null || best >= reach))
                {
                    float near = float.MaxValue; int which = -1;
                    foreach (var it in Items) { if (it.Placed || it.HeldBy >= 0) continue; float d = HandTo(s, it.transform.position); if (d < near) { near = d; which = it.Index; } }
                    if (which >= 0 && near >= reach && near < 0.5f)
                    {
                        // The same piece closed on twice from a distance: it comes to that hand (it was out of reach).
                        if (missItem[h] == which && Time.unscaledTime - missAt[h] < 12f && Time.unscaledTime - missAt[h] > 0.4f)
                        {
                            foreach (var it in Items)
                            {
                                if (it.Index != which || it.Placed || it.HeldBy >= 0) continue;
                                Vector3 away = it.transform.position - s.Pinch;
                                it.Home = s.Pinch + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up) * 0.05f;
                                it.ReturnAt = -1f;
                                it.CameAt = Time.time;
                                Came++;
                                Trace.Info("Gather", "a piece came to the hand", "item", which, "was_cm", near * 100f);
                                Sfx.Instance?.At("grab", it.transform.position, 0.35f, 1.3f);
                            }
                            missItem[h] = -1;
                        }
                        else { missItem[h] = which; missAt[h] = Time.unscaledTime; }
                    }
                    if (which >= 0 && near < 0.45f && Trace.Every("gather-miss-" + h, 0.7f))
                        Trace.Info("Gather", "closed a hand near a piece and missed", "item", which, "dist_cm", near * 100f, "how", fistEdge ? "whole hand" : "pinch",
                                   "from_head_m", Head != null ? Vector3.Distance(Items[Mathf.Min(which, Items.Count - 1)].transform.position, Head.position) : 0f);
                }
                if (nearest != null && closing && best < reach * (s.Controller ? 1.4f : 1f) && (Blocked == null || !Blocked(h)))
                {
                    byFist[h] = !Hands.PinchDown(h) && !s.Pinching;
                    if (byFist[h]) { Gripped++; fistAt[h] = -10f; }
                    held[h] = nearest;
                    nearest.Grab(h, s.Pinch);
                    Trace.Info("Gather", "grabbed", "item", nearest.Index, "hand", h, "controller", s.Controller, "dist_cm", best * 100f, "how", byFist[h] ? "whole hand" : "pinch");
                    Sfx.Instance?.At("grab", nearest.transform.position, 0.8f, Mathf.Pow(0.1f / 0.08f, 0.3f));
                    Hints.Instance?.Learned("pinch");
                    Grabbed?.Invoke(nearest);
                }
            }
            if (EyesOnly) GazeCollect();
            DropGuide();
        }

        /// <summary>
        /// Eyes only (no hands, no controllers): look at a glowing piece for 1.4 s and it flies to its spot on
        /// the table, so the task can be finished without hands.
        /// </summary>
        /// <summary>The way out of the first task if pinching isn't working: every piece flies to the table by itself.</summary>
        public void AutoPlace()
        {
            if (!Running) return;
            int n = 0;
            foreach (var it in Items)
            {
                if (it.Placed || it.HeldBy >= 0) continue;
                it.Placed = true;
                StartCoroutine(Snap(it, sockets[Mathf.Min(reserved++, sockets.Count - 1)]));
                n++;
            }
            Trace.Info("Gather", "pieces brought over (no pickup in time)", "count", n);
        }

        void GazeCollect()
        {
            if (Head == null) return;
            GatherItem best = null;
            float bestDot = Mathf.Cos(8f * Mathf.Deg2Rad);
            foreach (var it in Items)
            {
                if (it.Placed || it.HeldBy >= 0 || it.Appear < 0.6f) continue;
                float d = Vector3.Dot(Head.forward, (it.transform.position - Head.position).normalized);
                if (d > bestDot) { bestDot = d; best = it; }
            }
            if (best != gazed) { gazed = best; gazeStart = Time.unscaledTime; }
            if (gazed == null) return;
            gazed.SetHover(true);
            float k = (Time.unscaledTime - gazeStart) / 1.4f;
            Hints.Instance?.Request("gaze collect", gazed.transform.position + Vector3.up * 0.075f, k < 0.5f ? "Keep looking…" : "Here it comes", 2);
            if (k < 1f) return;
            var pick = gazed;
            gazed = null;
            pick.Placed = true;
            Vector3 socket = sockets[Mathf.Min(reserved++, sockets.Count - 1)];
            Trace.Info("Gather", "collected by looking", "item", pick.Index, "placed_after", PlacedCount + 1);
            Sfx.Instance?.At("grab", pick.transform.position, 0.6f);
            StartCoroutine(Snap(pick, socket));
        }

        /// <summary>
        /// Shows where things go: while you hold a piece, a glowing ring marks its spot on your table and a trail
        /// of sparks runs from the piece to it; over the table the ring grows and the label says "Let go!".
        /// Before that, the piece under your hand says "Pinch to pick it up".
        /// </summary>
        void DropGuide()
        {
            if (root == null || table == null) return;
            if (dropRing == null && Glow != null)
            {
                dropRing = Util.MeshObject("Drop ring", root, FlatRing(), Glow);
                for (int i = 0; i < 7; i++) dots.Add(Effects.Halo("Path spark", root, Glow, 0.016f, new Color(1f, 0.8f, 0.5f, 0.7f)).transform);
            }
            int h = -1;
            for (int i = 0; i < held.Length; i++) if (held[i] != null) { h = i; break; }
            bool holding = h >= 0 && Running && sockets.Count > 0;
            if (dropRing != null) dropRing.SetActive(holding);
            foreach (var d in dots) if (d != null) d.gameObject.SetActive(false);
            var hints = Hints.Instance;
            if (!holding)
            {
                if (hoverItem != null && hints != null && hints.Wants("pinch", 3))
                    hints.Request("pinch", hoverItem.transform.position + Vector3.up * 0.075f, "Pinch it, or close\nyour hand on it", 2);
                return;
            }
            var it = held[h];
            Vector3 socket = sockets[Mathf.Min(reserved, sockets.Count - 1)];
            Vector3 ringAt = new Vector3(socket.x, table.TopY + 0.004f, socket.z);
            bool over = OverTable(it.transform.position);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            if (dropRing != null)
            {
                dropRing.transform.position = ringAt;
                dropRing.transform.rotation = table.Rotation;
                float size = (over ? 0.12f : 0.09f) + 0.012f * pulse;
                dropRing.transform.localScale = new Vector3(size, 1f, size);
                ringBlock ??= new MaterialPropertyBlock();
                var r = dropRing.GetComponent<Renderer>();
                r.GetPropertyBlock(ringBlock);
                ringBlock.SetColor("_Tint", over ? new Color(1.7f, 1.4f, 1f, 1f) : new Color(0.85f, 0.7f, 0.5f, 1f));
                r.SetPropertyBlock(ringBlock);
            }
            if (!over)
            {
                Vector3 from = it.transform.position, to = ringAt + Vector3.up * 0.02f;
                Vector3 mid = (from + to) * 0.5f + Vector3.up * 0.08f;
                for (int i = 0; i < dots.Count; i++)
                {
                    float t = Mathf.Repeat(i / (float)dots.Count + Time.time * 0.55f, 1f);
                    var d = dots[i];
                    if (d == null) continue;
                    d.gameObject.SetActive(true);
                    float s1 = 1f - t;
                    d.position = s1 * s1 * from + 2f * s1 * t * mid + t * t * to;
                    d.localScale = Vector3.one * (0.018f * Mathf.Sin(t * Mathf.PI) + 0.002f);
                }
            }
            if (hints != null && hints.Wants("drop", 3))
                hints.Request("drop", over ? ringAt + Vector3.up * 0.1f : it.transform.position + Vector3.up * 0.08f,
                              over ? "Let go!" : "Carry it to the glowing ring\non your table", 3);
        }

        static Mesh flatRing;
        /// <summary>A flat ring (outer radius 0.5, inner 0.4) lying in XZ, for the Glow material (uv centre: no falloff).</summary>
        static Mesh FlatRing()
        {
            if (flatRing != null) return flatRing;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var uv = new List<Vector2>();
            const int seg = 48;
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
            flatRing = new Mesh { name = "drop-ring" };
            flatRing.SetVertices(v); flatRing.SetColors(c); flatRing.SetUVs(0, uv); flatRing.SetTriangles(t, 0);
            flatRing.RecalculateBounds();
            return flatRing;
        }

        /// <summary>How long a held piece waits for a hand that lost tracking, and for a pinch that reads open.</summary>
        public const float LostGrace = 0.3f, OpenGrace = 0.1f;
        readonly float[] lostAt = { -1f, -1f, -1f, -1f }, openAt = { -1f, -1f, -1f, -1f };

        void Release(int h)
        {
            if (h < lostAt.Length) { lostAt[h] = -1f; openAt[h] = -1f; }
            var it = held[h];
            held[h] = null;
            if (it == null) return;
            if (Hands != null) Trace.Info("Gather", "let go", "item", it.Index, "curl", Hands[h].Curl, "by", h < byFist.Length && byFist[h] ? "whole hand" : "pinch");
            it.HeldBy = -1;
            if (OverTable(it.transform.position))
            {
                Vector3 socket = sockets[Mathf.Min(reserved++, sockets.Count - 1)];
                it.Placed = true;
                Trace.Info("Gather", "released over the table", "item", it.Index, "placed_after", PlacedCount + 1);
                StartCoroutine(Snap(it, socket));
            }
            else
            {
                var l = table.ToLocal(it.transform.position);
                Trace.Info("Gather", "released off the table", "item", it.Index, "over_x", l.x, "over_z", l.z, "above_top_m", l.y - table.Size.y);
                it.ReturnAt = Time.time + 1f;
            }
        }

        /// <summary>
        /// Released over the table, generously: people let go well above it and a little past its edge (on the
        /// headset, misses were up to 47 cm up and up to 0.8 m from the middle), so up to 80 cm above the top and 60 cm
        /// past its edge counts, and the piece flies to its ring.
        /// </summary>
        /// <summary>How far past the table's edge a release still counts (metres).</summary>
        public const float Reach = 0.6f;

        public bool OverTable(Vector3 p)
        {
            if (table == null) return false;
            var l = table.ToLocal(p);
            return Mathf.Abs(l.x) <= table.Size.x * 0.5f + Reach && Mathf.Abs(l.z) <= table.Size.z * 0.5f + Reach &&
                   l.y >= table.Size.y - 0.1f && l.y <= table.Size.y + 0.8f;
        }

        System.Collections.IEnumerator Snap(GatherItem it, Vector3 socket)
        {
            int mine = run;
            Vector3 from = it.transform.position;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / 0.25f);
                Vector3 p = Vector3.Lerp(from, socket, Util.Ease(t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.05f);
                it.transform.position = p;
                yield return null;
                if (it == null || mine != run) yield break;   // the world closed while it was on its way
            }
            // Squash and spring back: sound and shape stand in for haptics.
            float sq = 0f;
            while (sq < 1f)
            {
                sq = Mathf.Min(1f, sq + Time.deltaTime / 0.18f);
                float y = Mathf.Lerp(0.8f, 1f, Util.Pop(sq));
                it.transform.localScale = new Vector3(1f + (1f - y) * 0.5f, y, 1f + (1f - y) * 0.5f);
                yield return null;
                if (it == null || mine != run) yield break;
            }
            PlacedCount++;
            Hints.Instance?.Learned("drop");
            Sfx.Instance?.At("place_" + Mathf.Clamp(PlacedCount, 1, 6), socket, 0.9f);
            var burst = Effects.Burst(it.transform, Glow, Color.white);
            burst.Play();
            if (Site != null) Site.Step(0.15f + 0.85f * PlacedCount / Total);
            Placed?.Invoke(it);
            if (PlacedCount >= Total)
            {
                Running = false;
                finishedAt = Time.time;
                if (Site != null) Site.Finish();
                Completed?.Invoke();
            }
        }

        // ---------- where pieces appear ----------

        /// <summary>
        /// Spots within a seated reach: 0.31–0.58 m from the head horizontally, at the heights of <see cref="Band"/>,
        /// within ±60° of forward (a cramped seat with fewer than three: up to ±100°), and each one <see cref="SpotOk"/> (out of the furniture, clear of the walls,
        /// the build and each other). Deterministic for a seed.
        /// </summary>
        public static List<Vector3> Spots(RoomModel room, Vector3 head, Vector3 forward, int count, int seed)
        {
            var list = new List<Vector3>();
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : room.Forward;
            var table = room.Table;
            Vector3 site = table != null ? table.TopCenter : head + forward * 0.5f;
            Band(room, head, out float low, out float high);
            var rng = new Rng(seed * 97 + 41);
            for (int tries = 0; list.Count < count && tries < 600; tries++)
            {
                // The narrow design area first (research E), widening only when the seat is cramped.
                // Never wider than 60 degrees either side (FoV-aware: the room sweep had found pieces at 95).
                float span = tries < 300 ? 45f : 60f;
                float ang = rng.Range(-span, span);
                if (count > 1 && tries < 200)
                {
                    // Spread evenly first: each piece gets its own slice of the arc.
                    int slot = list.Count;
                    float a0 = -38f + 76f * slot / Mathf.Max(1, count - 1);
                    ang = a0 + rng.Range(-8f, 8f);
                }
                // Nearer than they were: pieces 0.58 m and more from the eyes were out of a seated reach on the headset.
                float dist = tries < 300 ? rng.Range(0.31f, 0.46f) : tries < 450 ? rng.Range(0.31f, 0.52f) : rng.Range(0.31f, 0.58f);
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * forward;
                Vector3 p = head + dir * dist;
                p.y = rng.Range(low, high);
                if (!SpotOk(room, head, p, site, list)) continue;
                list.Add(p);
            }
            // A cramped seat (a wall close in front of you): wider to the sides rather than no task at all. The room
            // sweep found a seat with no piece once pieces stopped going past walls and beyond 60 degrees.
            int want = Mathf.Min(3, count);
            for (int tries = 0; list.Count < want && tries < 600; tries++)
            {
                float ang = rng.Range(-100f, 100f);
                float dist = rng.Range(NearReach, FarReach);
                Vector3 p = head + Quaternion.Euler(0f, ang, 0f) * forward * dist;
                p.y = rng.Range(low, high);
                if (!SpotOk(room, head, p, site, list)) continue;
                list.Add(p);
            }
            return list;
        }
    }
}

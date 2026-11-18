using System;
using System.Collections;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The storm, with your hands: meteors streak in towards you; a glowing ring shows where each one will arrive;
    /// hold your open palm (or a controller) in the ring and a shield at your hand blocks it. No console, nothing to
    /// read. Miss one and it gets through. When it's over, rewind and try again: the same storm, your choices.
    /// With no hands at all, looking straight at a ring blocks it. Every view is drawn from the simulation's clock
    /// (Core/Sim), so rewinding time flies the meteors back out.
    /// </summary>
    public sealed class Emergency : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material World, Glow, GlowFlat, Panel;
        public Font Font;
        public WorldLook Look;
        public float BlockRadius = 0.17f;
        /// <summary>The first meteor gets through by design (the player's first storm with furniture). Off after a rewind.</summary>
        public bool Teach;
        bool Teaching => Teach && defended.Count > 0;

        public Sim Sim { get; private set; }
        public bool Running { get; private set; }
        public bool Rewinding { get; private set; }
        public int Rewinds { get; private set; }
        public event Action<int> Phase;
        public event Action<Strike, StrikeResult> Struck;
        public event Action<int> Broke, Repaired;
        public event Action<int> Ended;
        public event Action Rewound;
        /// <summary>A ring has just appeared: the strike's index and its kind (Sim.Single or Sim.Big).</summary>
        public event Action<int, int> Incoming;
        /// <summary>Set before <see cref="Begin"/>: how far into her story (her third chapter ends with a big one).</summary>
        public int Level;
        /// <summary>Set before <see cref="Begin"/>: keys of furniture still dark from her last storm.</summary>
        public HashSet<string> PreDark;
        /// <summary>The piece (by its plain name) the big one should be aimed at, set before Begin; Focused is the one it is.</summary>
        public string Focus = "";
        public string Focused { get; private set; } = "";
        /// <summary>Set before <see cref="Begin"/>: her storm (what goes dark stays dark when it is over).</summary>
        public bool Remember;
        readonly bool[] preDark = new bool[4];
        bool Dark(int k) => k >= 0 && k < preDark.Length && preDark[k];
        /// <summary>A big one: one palm must stay in its ring this long (or two palms, at once).</summary>
        public const float BigHold = 1f;

        Transform root, flash;
        RoomModel room;
        readonly List<Transform> rocks = new List<Transform>();
        readonly List<Transform> rings = new List<Transform>();
        readonly List<Renderer> ringR = new List<Renderer>();
        readonly Transform[] shields = new Transform[3];
        PokeButton bRewind;
        Caption status;
        float viewT, flashUntil;
        Vector3 centre;
        MaterialPropertyBlock block;
        bool ended;
        readonly HashSet<int> whooshed = new HashSet<int>(), pinged = new HashSet<int>();
        readonly List<TrailRenderer> trails = new List<TrailRenderer>();
        RoomSkins skins;
        readonly List<RoomSkins.Skin> defended = new List<RoomSkins.Skin>();
        /// <summary>How many of your furniture pieces the storm is aimed at (0: no scan, the meteors come at you).</summary>
        public int Defended => defended.Count;
        /// <summary>The top of defended piece k (for the runway lights).</summary>
        /// <summary>The k-th defended piece of your room (its size and turn, for the runway lights), or null.</summary>
        public RoomModel.Piece DefendedPiece(int k) => k >= 0 && k < defended.Count ? defended[k].Piece : null;

        /// <summary>
        /// The top of the k-th defended piece as you see it: its world skin (a bench, a pine trunk) can stand taller
        /// than the real piece, so its lights go on the skin's top, not hidden inside it. Falls back to the real top.
        /// </summary>
        public float SeenTopY(int k)
        {
            if (k < 0 || k >= defended.Count || defended[k].Piece == null) return centre.y;
            float y = defended[k].Piece.TopY;
            var go = defended[k].Go;
            if (go != null)
            {
                var piece = defended[k].Piece;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    // Only what stands over the piece's own footprint (not a skin's far-reaching branches).
                    var b = r.bounds;
                    if (Vector3.Distance(new Vector3(b.center.x, 0f, b.center.z), new Vector3(piece.Center.x, 0f, piece.Center.z)) > Mathf.Max(piece.Size.x, piece.Size.z)) continue;
                    y = Mathf.Max(y, Mathf.Min(b.max.y, piece.TopY + 1.2f));
                }
            }
            return y;
        }
        public Vector3 DefendedTop(int k) => k >= 0 && k < defended.Count && defended[k].Piece != null ? defended[k].Piece.TopCenter : centre;
        /// <summary>How broken defended piece k is right now (0 whole … 1 broken; rewinding mends it).</summary>
        public float PieceBreak(int k) => k >= 0 && k < breakK.Length ? breakK[k] : 0f;
        /// <summary>Pieces broken open right now (the real furniture shows through).</summary>
        public int BrokenPieces { get; private set; }
        /// <summary>The running count, for the line above the dock.</summary>
        public string Score { get; private set; } = "";

        /// <summary>The pieces that came through whole, in the room's words ("sofa and shelf").</summary>
        public string SafePieces()
        {
            var names = new List<string>();
            for (int k = 0; k < defended.Count; k++)
            {
                bool hit = false;
                for (int i = k; i < Sim.Strikes.Count; i += defended.Count) if ((Sim.State.HitMask & (1 << i)) != 0) hit = true;
                string n = Plain(defended[k].Piece.Label);
                if (!hit && !Dark(k) && n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            return names.Count == 0 ? "" : names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        }

        /// <summary>The first piece that was never hit, in the room's words ("sofa"), or "".</summary>
        public string FirstSafe()
        {
            if (Sim == null) return "";
            for (int k = 0; k < defended.Count; k++)
            {
                bool hit = false;
                for (int i = k; i < Sim.Strikes.Count; i += defended.Count) if ((Sim.State.HitMask & (1 << i)) != 0) hit = true;
                string n = Plain(defended[k].Piece.Label);
                if (!hit && !Dark(k) && n.Length > 0) return n;
            }
            return "";
        }

        /// <summary>The keys of the pieces the storm was aimed at, and of those dark at its end (hit, or dark before).</summary>
        public void Keys(out List<string> all, out List<string> dark)
        {
            all = new List<string>(); dark = new List<string>();
            for (int k = 0; k < defended.Count; k++)
            {
                string key = defended[k].Piece.Key;
                if (string.IsNullOrEmpty(key)) continue;
                all.Add(key);
                bool hit = Dark(k);
                if (!hit && Sim != null) for (int i = k; i < Sim.Strikes.Count; i += defended.Count) if ((Sim.State.HitMask & (1 << i)) != 0) hit = true;
                if (hit) dark.Add(key);
            }
        }

        /// <summary>What a strike is aimed at, in the room's words ("sofa"), or "".</summary>
        public string PieceLabel(Strike s)
        {
            int i = Sim != null ? Sim.Strikes.IndexOf(s) : -1;
            if (i < 0 || defended.Count == 0) return "";
            return Plain(defended[i % defended.Count].Piece.Label);
        }

        public Vector3 StationPos(int i) => centre;
        public Vector3 ConsolePos => centre;
        /// <summary>Where the next meteor will arrive (hold a palm here).</summary>
        public Vector3 NextTarget => Sim != null && Sim.State.Next < Sim.Strikes.Count ? Target(Sim.State.Next) : centre;

        public void Begin(RoomModel roomModel, Recipe r, int seed, RoomSkins roomSkins = null) => Begin(roomModel, r, seed, Pace.Standard, roomSkins);

        /// <summary>A piece of your furniture the storm can be aimed at: real, not the work table, and not a door or a
        /// window (a door frame went dark as "furniture" in a recorded run on Nov 16: review 36).</summary>
        bool Aimable(RoomSkins.Skin sk) =>
            sk.Go != null && sk.Piece != null && !sk.Piece.Virtual && sk.Piece != room.Table &&
            sk.Piece.Role != Role.Door && sk.Piece.Role != Role.Window;

        public void Begin(RoomModel roomModel, Recipe r, int seed, Pace pace, RoomSkins roomSkins = null)
        {
            Stop();
            if (roomModel == null || Head == null) return;
            room = roomModel;
            skins = roomSkins;
            defended.Clear();
            // Your real furniture, turned into the world's things, is what the storm is aimed at: the pieces in
            // front of you (within 60 degrees and 4 m; failing that, within 100 degrees and 3 m), nearest first, up to four.
            if (skins != null && room.Scanned)
            {
                foreach (var sk in skins.All)
                {
                    if (!Aimable(sk)) continue;
                    Vector3 to = sk.Piece.TopCenter - room.Head; to.y = 0f;
                    if (to.magnitude < 0.3f || to.magnitude > 4f || Vector3.Angle(to, room.Forward) > 60f) continue;
                    defended.Add(sk);
                }
                // Nothing ahead (a desk facing a wall): the nearest pieces within a quarter turn either side.
                if (defended.Count == 0)
                    foreach (var sk in skins.All)
                    {
                        if (!Aimable(sk)) continue;
                        Vector3 to = sk.Piece.TopCenter - room.Head; to.y = 0f;
                        if (to.magnitude < 0.3f || to.magnitude > 3f || Vector3.Angle(to, room.Forward) > 100f) continue;
                        defended.Add(sk);
                    }
                defended.Sort((x, y) => Vector3.Distance(x.Piece.TopCenter, room.Head).CompareTo(Vector3.Distance(y.Piece.TopCenter, room.Head)));
                if (defended.Count > 4) defended.RemoveRange(4, defended.Count - 4);
                // Her last chapter: the big one is aimed at the piece you lit again (strike i is aimed at piece i mod n).
                Focused = "";
                if (Focus.Length > 0 && Level >= 2 && defended.Count > 0)
                {
                    var probe = new Sim(seed, pace, Level);
                    int bi = -1;
                    for (int i = 0; i < probe.Strikes.Count; i++) if (probe.Strikes[i].Kind == Sim.Big) bi = i;
                    int fi = defended.FindIndex(sk => Plain(sk.Piece.Label) == Focus);
                    if (bi >= 0 && fi >= 0)
                    {
                        int slot = bi % defended.Count;
                        var t = defended[slot]; defended[slot] = defended[fi]; defended[fi] = t;
                        Focused = Focus;
                    }
                }
            }
            // Furniture still dark from her last storm starts dark (one light always stays on for her).
            int darkLeft = defended.Count - 1;
            for (int k = 0; k < preDark.Length; k++)
            {
                preDark[k] = k < defended.Count && darkLeft > 0 && PreDark != null && defended[k].Piece.Key.Length > 0 && PreDark.Contains(defended[k].Piece.Key);
                if (preDark[k]) { darkLeft--; breakK[k] = 1f; }
            }
            Sim = new Sim(seed, pace, Level);
            Sim.Blocker = Blocks;
            Sim.Struck += OnStruck;
            Sim.PhaseChanged += p => Phase?.Invoke(p);
            root = new GameObject("Emergency").transform;
            root.SetParent(transform, false);
            centre = room.Head + Vector3.down * 0.08f;
            Build(r);
            Running = true;
            ended = false;
            Sim.Begin();
            viewT = 0f;
            int big = 0, dark = 0;
            foreach (var st in Sim.Strikes) if (st.Kind == Sim.Big) big++;
            for (int k = 0; k < preDark.Length; k++) if (preDark[k]) dark++;
            Trace.Info("Emergency", "begun", "defending", defended.Count, "seed", seed, "strikes", Sim.Strikes.Count, "storm_ends_s", Sim.StormEnd, "level", Level, "big", big, "dark_before", dark);
        }

        public void Stop()
        {
            Running = false; Rewinding = false;
            // Her storm: what ended dark stays dark (your real furniture shows) after the storm is put away.
            List<RoomModel.Piece> keepDark = null;
            if (Remember && Sim != null && skins != null)
            {
                Keys(out _, out var darkKeys);
                keepDark = new List<RoomModel.Piece>();
                foreach (var sk in defended) if (sk != null && sk.Piece != null && darkKeys.Contains(sk.Piece.Key)) keepDark.Add(sk.Piece);
            }
            Mend();
            if (keepDark != null) foreach (var piece in keepDark) skins.Dim(piece, true);
            defended.Clear();
            for (int k = 0; k < preDark.Length; k++) preDark[k] = false;
            StopAllCoroutines();
            if (root != null) Util.Kill(root.gameObject);
            root = null; rocks.Clear(); rings.Clear(); ringR.Clear(); trails.Clear(); coveredAt.Clear(); coverSince.Clear(); bothAt.Clear(); fills.Clear();
            for (int i = 0; i < shields.Length; i++) shields[i] = null;
            if (Look != null) { Look.Power = 1f; Look.Alarm = 0f; Look.Desaturate = 0f; }
            Sim = null;
        }

        /// <summary>Where strike i arrives: in front of you, within easy reach of a seated arm.</summary>
        Vector3 Target(int i) => centre + Dir(i) * 0.48f;
        /// <summary>Where strike <paramref name="i"/>'s ring is (the demo player picks the piece in view by it).</summary>
        public Vector3 TargetOf(int i) => Target(i);

        /// <summary>Where a meteor comes from, seen from its ring: up in the sky ahead, beyond the piece it is aimed at.</summary>
        Vector3 From(int i)
        {
            Vector3 d = Dir(i); d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : room.Forward;
            return (d * 1.0f + Vector3.up * 0.45f).normalized;
        }

        const float FallSeconds = 0.12f;
        Color flashTint;
        /// <summary>When each hand was last tracked: its shield stays through a short tracking drop instead of blinking.</summary>
        readonly float[] shieldSeen = { -10f, -10f, -10f, -10f };

        /// <summary>Where a meteor meets your shield: half a metre short of the piece it is aimed at (or its ring, with no furniture).</summary>
        Vector3 End(int i) => defended.Count > 0 ? defended[i % defended.Count].Piece.TopCenter + Vector3.up * 0.1f + Sky(i) * 0.5f : Target(i);

        /// <summary>The way a meteor comes down on a piece: from beyond it, 18 degrees above the horizon.</summary>
        Vector3 Sky(int i)
        {
            Vector3 d = Dir(i); d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : room.Forward;
            return (d + Vector3.up * 0.32f).normalized;
        }

        /// <summary>MRUK's labels in plain words ("COUCH" is a sofa; "OTHER" is just your furniture).</summary>
        public static string Plain(string label)
        {
            switch ((label ?? "").ToUpperInvariant())
            {
                case "COUCH": return "sofa";
                case "TABLE": return "table";
                case "STORAGE": return "shelf";
                case "BED": return "bed";
                case "SCREEN": return "screen";
                case "LAMP": return "lamp";
                case "PLANT": return "plant";
                case "CHAIR": return "chair";
                default: return "furniture";
            }
        }
        Transform mark;
        /// <summary>Where the meteor that is coming next is right now (for tests and pictures).</summary>
        public Vector3 NextRock => Sim != null && Sim.State.Next < rocks.Count ? rocks[Sim.State.Next].position : centre;
        readonly float[] breakK = new float[4];

        Vector3 Dir(int i)
        {
            var s = Sim.Strikes[i];
            if (defended.Count > 0)
            {
                // Towards the piece this meteor is aimed at: your palm goes between you and it.
                var piece = defended[i % defended.Count].Piece;
                Vector3 to = (piece.TopCenter + Vector3.up * 0.1f - centre).normalized;
                to.y = Mathf.Clamp(to.y, -0.3f, 0.45f);
                return to.normalized;
            }
            Vector3 d = Quaternion.AngleAxis(s.Bearing * 0.33f, Vector3.up) * room.Forward;
            return (d + Vector3.up * (0.2f + 0.17f * Mathf.Sin(i * 1.7f))).normalized;
        }

        /// <summary>A hand (or controller) in the ring blocks; with no hands at all, looking at the ring does.</summary>
        /// <summary>How long a palm that covered the ring still counts after it moved (people pull back a moment early).</summary>
        public static float Grace => Gentle ? 0.7f : 0.35f;
        /// <summary>Comfort & access: a bigger catch zone round each ring and twice the grace.</summary>
        public static bool Gentle;
        /// <summary>Comfort & access (Eyes only): looking at a ring shields it even with hands in view.</summary>
        public static bool EyesToo;
        /// <summary>Set while the world is faded back to your room (standing, tracking lost): the storm waits.</summary>
        public bool Held;
        float Catch => BlockRadius * (Gentle ? 1.45f : 1f);
        /// <summary>A big one's ring is larger: room for both palms.</summary>
        float CatchFor(int i) => Catch * (Sim != null && i >= 0 && i < Sim.Strikes.Count && Sim.Strikes[i].Kind == Sim.Big ? 1.35f : 1f);
        readonly Dictionary<int, float> coverSince = new Dictionary<int, float>(), bothAt = new Dictionary<int, float>();
        /// <summary>The ring inside a big one's ring that fills as a palm is held there.</summary>
        readonly Dictionary<int, Transform> fills = new Dictionary<int, Transform>();
        readonly Dictionary<int, float> coveredAt = new Dictionary<int, float>();

        bool Blocks(Strike s)
        {
            int i = Sim.Strikes.IndexOf(s);
            if (i < 0) return false;
            if (Teaching && i == 0) return false;
            if (NeedsTwo(i)) return BigHeld(i);
            if (BlocksNow(i)) return true;
            return coveredAt.TryGetValue(i, out float at) && Time.time - at <= Grace;
        }

        /// <summary>A big one, with hands in view and no Gentle storm: both palms in its ring, or one held there steady.</summary>
        bool NeedsTwo(int i) => Sim.Strikes[i].Kind == Sim.Big && !Gentle && !EyesToo && HandsSeen();

        bool HandsSeen()
        {
            if (Hands == null) return false;
            for (int h = 0; h < Hands.Count; h++) if (Hands[h].Tracked) return true;
            return false;
        }

        /// <summary>Both palms in the ring just now, or one palm that has stayed in it for <see cref="BigHold"/>.</summary>
        bool BigHeld(int i) => BigProgress(i) >= 1f;

        /// <summary>How far a big one's hold has got: 1 with two palms in it, or after a palm has stayed for BigHold.</summary>
        public float BigProgress(int i)
        {
            if (bothAt.TryGetValue(i, out float b) && Time.time - b <= Grace) return 1f;
            if (!coveredAt.TryGetValue(i, out float c) || Time.time - c > Grace || !coverSince.TryGetValue(i, out float s0)) return 0f;
            return Mathf.Clamp01((c - s0) / BigHold);
        }

        /// <summary>How many hands (or controllers) are in strike i's ring now.</summary>
        int HandsIn(int i)
        {
            if (Hands == null) return 0;
            Vector3 target = Target(i);
            float r = CatchFor(i);
            int n = 0;
            for (int h = 0; h < Hands.Count; h++)
            {
                var st = Hands[h];
                if (st.Tracked && (Vector3.Distance(st.Palm, target) < r || Vector3.Distance(st.IndexTip, target) < r)) n++;
            }
            return n;
        }

        bool BlocksNow(int i)
        {
            Vector3 target = Target(i);
            bool any = false;
            if (Hands != null)
                for (int h = 0; h < Hands.Count; h++)
                {
                    var st = Hands[h];
                    if (!st.Tracked) continue;
                    any = true;
                    if (Vector3.Distance(st.Palm, target) < CatchFor(i) || Vector3.Distance(st.IndexTip, target) < CatchFor(i)) return true;
                }
            // No hands seen, or Eyes only: looking at the ring shields it.
            if ((!any || EyesToo) && Head != null && Vector3.Angle(Head.forward, target - Head.position) < 9f) return true;
            return false;
        }

        void Build(Recipe r)
        {
            var colors = WorldColors.For(r);
            for (int i = 0; i < Sim.Strikes.Count; i++)
            {
                var mb = new MeshBuilder();
                Props.Build(mb, "rock", colors, new Rng(i * 7 + 1));
                var b = mb.Bounds;
                float scale = 0.34f * Sim.Strikes[i].Size / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                var go = Util.MeshObject("Meteor " + i, root, mb.ToMesh("meteor" + i), World);
                go.transform.localScale = Vector3.one * scale;
                Effects.Halo("Trail", go.transform, Glow, 0.5f / Mathf.Max(0.01f, scale), new Color(1f, 0.55f, 0.25f) * 0.8f);
                // A tail of fire behind it, so you can see where it comes from.
                var tr = go.AddComponent<TrailRenderer>();
                tr.sharedMaterial = Glow;
                tr.time = 0.45f;
                tr.minVertexDistance = 0.04f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.11f * Sim.Strikes[i].Size), new Keyframe(1f, 0f));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.12f), 0.5f), new GradientColorKey(new Color(0.5f, 0.12f, 0.05f), 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                trails.Add(tr);
                go.SetActive(false);
                rocks.Add(go.transform);
                var ring = Util.MeshObject("Ring " + i, root, RingMesh(), GlowFlat != null ? GlowFlat : Glow);
                ring.transform.position = Target(i);
                ring.transform.rotation = Quaternion.LookRotation(Dir(i), Vector3.up);
                ring.SetActive(false);
                if (Sim.Strikes[i].Kind == Sim.Big)
                {
                    // Fills from the middle as a palm is held in it: what to do, shown by shape (not only colour).
                    var fill = Util.MeshObject("Hold fill " + i, ring.transform, RingMesh(), GlowFlat != null ? GlowFlat : Glow);
                    fill.transform.localScale = Vector3.one * 0.001f;
                    var fr = fill.GetComponent<Renderer>();
                    block ??= new MaterialPropertyBlock();
                    fr.GetPropertyBlock(block);
                    block.SetColor("_Tint", new Color(0.55f, 1.4f, 0.75f));
                    fr.SetPropertyBlock(block);
                    fills[i] = fill.transform;
                }
                rings.Add(ring.transform);
                ringR.Add(ring.GetComponent<Renderer>());
            }
            for (int h = 0; h < shields.Length; h++)
            {
                var sh = Effects.Halo("Hand shield " + h, root, Glow, 0.26f, new Color(0.45f, 0.75f, 1f) * 0.9f);
                sh.SetActive(false);
                shields[h] = sh.transform;
            }
            mark = Effects.Halo("Threat mark", root, Glow, 0.5f, new Color(1f, 0.6f, 0.25f)).transform;
            mark.gameObject.SetActive(false);
            flash = Effects.Halo("Impact flash", root, Glow, 1f, Color.white).transform;
            flash.gameObject.SetActive(false);
            status = Caption.Make("Storm status", root, Font, Panel, Head, 14f, 24f);
            status.Fixed = true;
            status.transform.position = centre + room.Forward * 0.62f + Vector3.up * 0.3f;
            bRewind = PokeButton.Make("rewind", root, Panel, Font, "Rewind", new Vector2(0.13f, 0.05f), 14f);
            Vector3 right = Vector3.Cross(Vector3.up, room.Forward);
            bRewind.transform.position = centre + room.Forward * 0.44f - right * 0.16f + Vector3.down * 0.2f;
            bRewind.transform.rotation = Quaternion.LookRotation(bRewind.transform.position - (centre + Vector3.up * 0.05f), Vector3.up);
            bRewind.Hint = "Go back in time and try again";
            bRewind.Pressed += () => RewindToFirstMiss();
            bRewind.gameObject.SetActive(false);
        }

        static Mesh ring;
        static Mesh RingMesh()
        {
            if (ring != null) return ring;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var uv = new List<Vector2>();
            const int seg = 40;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * 0.43f); v.Add(d * 0.5f);
                c.Add(Color.white); c.Add(Color.white);
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2, k, k + 3, k + 1, k, k + 2, k + 3 }); }
            }
            ring = new Mesh { name = "storm-ring" };
            ring.SetVertices(v); ring.SetColors(c); ring.SetUVs(0, uv); ring.SetTriangles(t, 0);
            ring.RecalculateBounds();
            return ring;
        }

        // ---------- actions kept for voice and the menu ----------

        public bool Divert(string to) => false;
        public bool DoEvade() => false;
        public bool DoRepair(int s) => false;
        public int FirstBroken() => -1;

        /// <summary>Back to three seconds before the first meteor that got through (so you can catch that one).</summary>
        /// <summary>Set by the director: false once rewinding would undo something good (Mara landed safely).</summary>
        public Func<bool> RewindAllowed;
        bool CanRewind => RewindAllowed == null || RewindAllowed();

        public bool RewindToFirstMiss()
        {
            if (Sim == null || !CanRewind) return false;
            for (int i = 0; i < Sim.Strikes.Count; i++)
                if ((Sim.State.HitMask & (1 << i)) != 0) return DoRewind(Mathf.Max(1f, Sim.State.T - Sim.Strikes[i].Time + 3f));
            return DoRewind(30f);
        }

        public bool RewindShown => bRewind != null && bRewind.gameObject.activeSelf;
        /// <summary>Any catch-ring is lit (for tests: none after the storm has ended).</summary>
        public bool AnyRingShown { get { foreach (var r in rings) if (r != null && r.gameObject.activeSelf) return true; return false; } }

        public bool DoRewind(float seconds)
        {
            if (!Running || Rewinding || Sim == null || !CanRewind || !Sim.TryPast(seconds, out var past)) return false;
            StartCoroutine(RewindRoutine(seconds, past.T));
            return true;
        }

        /// <summary>Time runs backwards in front of you (the meteors fly back out), then play resumes from there.</summary>
        IEnumerator RewindRoutine(float seconds, float toT)
        {
            Rewinding = true;
            Rewinds++;
            float fromT = viewT, t = 0f;
            Sfx.Instance?.Ui("world_end", 0.7f);
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 1.3f;
                viewT = Mathf.Lerp(fromT, toT, Util.Ease(t));
                if (Look != null) Look.Desaturate = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                Draw(true);
                yield return null;
            }
            Sim.Rewind(seconds);
            viewT = Sim.State.T;
            if (Look != null) Look.Desaturate = 0f;
            ended = false;
            Teach = false;
            Rewinding = false;
            Trace.Info("Emergency", "rewound", "to_t", Sim.State.T, "phase", Sim.State.Phase, "rewinds", Rewinds);
            Rewound?.Invoke();
        }

        void OnStruck(Strike s, StrikeResult r)
        {
            int i = Mathf.Max(0, Sim.Strikes.IndexOf(s));
            // With no furniture the strike used to land at your ring, half a metre from your eyes, and its flash filled most
            // of the view for a third of a second: it lands two metres out now, the flash is small, and it fades.
            Vector3 hit = r == StrikeResult.Hit && defended.Count > 0 ? defended[i % defended.Count].Piece.TopCenter : defended.Count > 0 ? End(i) : Target(i) + From(i) * 1.5f;
            if (flash != null && !Effects.Calm)
            {
                flash.gameObject.SetActive(true);
                flash.position = hit;
                flash.localScale = Vector3.one * (defended.Count > 0 ? (r == StrikeResult.Hit ? 0.8f : 0.5f) : (r == StrikeResult.Hit ? 0.5f : 0.35f));
                var fr = flash.GetComponent<Renderer>();
                block ??= new MaterialPropertyBlock();
                fr.GetPropertyBlock(block);
                flashTint = r == StrikeResult.Hit ? new Color(1.6f, 0.5f, 0.2f) : new Color(0.5f, 0.85f, 1.5f);
                block.SetColor("_Tint", flashTint);
                fr.SetPropertyBlock(block);
                flashUntil = Time.time + 0.35f;
            }
            Sfx.Instance?.At(r == StrikeResult.Hit ? "reveal_thump" : "golden_chime", hit, r == StrikeResult.Hit ? 1.3f : 0.9f);
            Sfx.Instance?.At("crunch", hit, r == StrikeResult.Hit ? 1.2f : 1f, r == StrikeResult.Hit ? 0.7f : 1.1f);
            if (r != StrikeResult.Hit && Hands != null)
                for (int h = 0; h < 2; h++)
                    if (Hands[h].Tracked && Hands[h].Controller && Vector3.Distance(Hands[h].Palm, hit) < BlockRadius * 1.5f)
                        OVRInput.SetControllerVibration(1f, 0.7f, h == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch);
            Trace.Info("Emergency", "strike", "result", r.ToString(), "n", i + 1, "of", Sim.Strikes.Count, "t", Sim.State.T, "hull", Sim.State.Hull);
            Struck?.Invoke(s, r);
        }

        /// <summary>Every defended piece whole again (the storm is over, or the world is going).</summary>
        void Mend()
        {
            foreach (var sk in defended)
            {
                if (sk == null || sk.Go == null) continue;
                sk.Go.SetActive(true);
                sk.Go.transform.localScale = Vector3.one;
                if (skins != null && skins.Shell != null) skins.Shell.HolderScale(sk.Piece, 0f);
            }
            BrokenPieces = 0;
            for (int k = 0; k < breakK.Length; k++) breakK[k] = 0f;
            if (wallsK > 0f && skins != null && skins.Shell != null) skins.Shell.Radius = skins.Shell.FullRadius + 2f;
            wallsK = wallsWere = 0f; RoomBack = false;
        }

        /// <summary>
        /// A meteor that got through breaks the piece it was aimed at: its world skin goes and your real furniture
        /// shows through. Drawn from the simulation's record of hits, so rewinding time makes the piece whole again.
        /// </summary>
        void LateUpdate()
        {
            if (!Running || Sim == null || defended.Count == 0) return;
            int broken = 0;
            for (int k = 0; k < defended.Count; k++)
            {
                bool hit = Dark(k);
                for (int i = k; i < Sim.Strikes.Count && !hit; i += defended.Count)
                    if ((Sim.State.HitMask & (1 << i)) != 0 && Sim.Strikes[i].Time + FallSeconds <= viewT + 0.01f) { hit = true; break; }
                var sk = defended[k];
                if (sk.Go == null) continue;
                float was = breakK[k];
                breakK[k] = Mathf.MoveTowards(was, hit ? 1f : 0f, Time.unscaledDeltaTime / 0.8f);   // 0.8 s: no sudden light change
                if (hit && was <= 0f)
                {
                    // The moment it breaks: a crack, a burst of light, and the real piece starts to show.
                    Sfx.Instance?.At("marshmallow_ignite", sk.Piece.TopCenter, 1f, 0.7f);
                    Sfx.Instance?.At("reveal_thump", sk.Piece.TopCenter, 0.9f);
                    var burst = Effects.Burst(root, Glow, new Color(1f, 0.7f, 0.4f));
                    burst.transform.position = sk.Piece.TopCenter;
                    burst.Play();
                    Destroy(burst.gameObject, 2f);
                }
                float b = Util.Ease(breakK[k]);
                sk.Go.SetActive(b < 0.98f);
                sk.Go.transform.localScale = Vector3.one * Mathf.Max(0.02f, 1f - b) + new Vector3(0f, 0.25f * Mathf.Sin(b * Mathf.PI), 0f);
                if (skins != null && skins.Shell != null) skins.Shell.HolderScale(sk.Piece, b);
                if (hit) broken++;
            }
            BrokenPieces = broken;
            RoomBack = defended.Count > 0 && broken >= defended.Count && Sim.State.Taken >= 2;
            if (RoomBack && !Rewinding && Sim.State.Phase < Sim.Aftermath) Sim.Surrender();
            wallsK = Mathf.MoveTowards(wallsK, RoomBack ? 1f : 0f, Time.unscaledDeltaTime / 1.2f);
            if (skins != null && skins.Shell != null && (wallsK > 0f || wallsWere > 0f))
                skins.Shell.Radius = Mathf.Lerp(skins.Shell.FullRadius + 2f, 0f, Util.Ease(wallsK));
            wallsWere = wallsK;
        }

        /// <summary>Every defended piece is broken: the walls of your real room have come back.</summary>
        public bool RoomBack { get; private set; }
        float wallsK, wallsWere;

        void Update()
        {
            if (!Running || Sim == null) return;
            if (!Rewinding)
            {
                if (!Held) Sim.Step(Time.deltaTime);   // scaled time: Freeze holds the storm too, and so does the safety fade
                viewT = Sim.State.T;
                if (!ended && Sim.State.Phase == Sim.Aftermath)
                {
                    ended = true;
                    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.Touch);
                    Trace.Info("Emergency", "ended", "outcome", Sim.OutcomeName(Sim.State.Outcome), "blocked", Sim.State.Blocked, "taken", Sim.State.Taken, "rewinds", Rewinds);
                    Ended?.Invoke(Sim.State.Outcome);
                }
            }
            Draw(false);
        }

        void Draw(bool rewinding)
        {
            var st = Sim.State;
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < rocks.Count; i++)
            {
                float until = Sim.Strikes[i].Time - viewT;
                // Sound: a ping as each ring appears, and a rushing whoosh from the meteor as it comes in.
                bool live = !rewinding && st.Phase < Sim.Aftermath;
                if (live && until > 0f && until < 3f) { if (pinged.Add(i)) { Sfx.Instance?.At("item_appear", Target(i), 0.7f, 1.3f); Incoming?.Invoke(i, Sim.Strikes[i].Kind); } }
                else if (until > 3.2f) pinged.Remove(i);
                if (live && until > 0f && until < 1.4f)
                {
                    if (whooshed.Add(i))
                    {
                        // Heard from where it is heading, a metre out from the ring: a far rock is a whisper.
                        Vector3 near = Target(i) + (rocks[i].position - Target(i)).normalized * 1.2f;
                        Sfx.Instance?.At("reveal_whoosh", near, 1.4f, 0.75f);
                        Sfx.Instance?.At("stoke_whoosh", near, 0.9f, 0.6f);
                    }
                }
                else if (until > 1.6f) whooshed.Remove(i);
                bool on = until > 0f && until < 6f && (st.Phase < Sim.Aftermath || rewinding);
                bool falling = defended.Count > 0 && until <= 0f && until > -FallSeconds && (st.HitMask & (1 << i)) != 0;
                if (rocks[i].gameObject.activeSelf != (on || falling))
                {
                    rocks[i].gameObject.SetActive(on || falling);
                    if (i < trails.Count && trails[i] != null) trails[i].Clear();   // no streak from where it last was
                }
                if (i < trails.Count && trails[i] != null) trails[i].emitting = !rewinding;
                if (falling)
                {
                    if (rings[i].gameObject.activeSelf) rings[i].gameObject.SetActive(false);
                    var piece = defended[i % defended.Count].Piece;
                    rocks[i].position = Vector3.Lerp(End(i), piece.TopCenter, -until / FallSeconds);
                    continue;
                }
                // The ring shows where to hold your palm, from 3 s before; it tightens as the meteor nears.
                bool ringOn = until > 0f && until < 3f && (st.Phase < Sim.Aftermath && !rewinding) && !(Teaching && i == 0);
                if (rings[i].gameObject.activeSelf != ringOn) rings[i].gameObject.SetActive(ringOn);
                if (ringOn)
                {
                    bool covered = !rewinding && !(Teaching && i == 0) && BlocksNow(i);
                    if (covered)
                    {
                        // A palm that leaves for longer than the grace starts its steady hold again.
                        if (!coveredAt.TryGetValue(i, out float lastCover) || Time.time - lastCover > Grace) coverSince[i] = Time.time;
                        coveredAt[i] = Time.time;
                        if (HandsIn(i) >= 2) bothAt[i] = Time.time;
                    }
                    float size = Mathf.Lerp(0.2f, 0.42f, Mathf.Clamp01(until / 3f)) * (Sim.Strikes[i].Kind == Sim.Big ? 1.35f : 1f);
                    rings[i].localScale = Vector3.one * size;
                    if (fills.TryGetValue(i, out var fillT) && fillT != null)
                        fillT.localScale = Vector3.one * Mathf.Max(0.001f, (NeedsTwo(i) ? BigProgress(i) : 0f) * 0.82f);
                    ringR[i].GetPropertyBlock(block);
                    // Green while the palm has been in it within 0.4 s: at the edge of the catch zone, tracking jitter used
                    // to switch it orange and green every frame. The orange pulse is slow (1.4 a second); steady under Calm.
                    bool shownCovered = NeedsTwo(i) ? BigHeld(i) : coveredAt.TryGetValue(i, out float coveredWhen) && Time.time - coveredWhen < 0.4f;
                    if (shownCovered) rings[i].localScale = Vector3.one * (size * 0.8f);
                    block.SetColor("_Tint", shownCovered ? new Color(0.6f, 1.1f, 1.6f) : new Color(1.5f, 0.75f, 0.3f) * (Effects.Calm ? 0.85f : 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 9f)));
                    ringR[i].SetPropertyBlock(block);
                }
                if (!on) continue;
                rocks[i].position = defended.Count > 0
                    ? End(i) + Sky(i) * (until * 2.5f)
                    : Target(i) + From(i) * (until * 5f);
                rocks[i].Rotate(40f * Time.unscaledDeltaTime, 65f * Time.unscaledDeltaTime, 0f);
            }
            if (mark != null)
            {
                int nx = st.Next;
                float until2 = nx < Sim.Strikes.Count ? Sim.Strikes[nx].Time - viewT : -1f;
                bool show = defended.Count > 0 && until2 > 0f && until2 < 3f && (st.Phase < Sim.Aftermath && !rewinding);
                if (mark.gameObject.activeSelf != show) mark.gameObject.SetActive(show);
                if (show)
                {
                    mark.position = defended[nx % defended.Count].Piece.TopCenter + Vector3.up * 0.12f;
                    mark.localScale = Vector3.one * (Effects.Calm ? 0.5f : 0.45f + 0.12f * Mathf.Sin(Time.unscaledTime * 9f));
                }
            }
            if (flash != null && flash.gameObject.activeSelf)
            {
                if (Time.time > flashUntil || rewinding) flash.gameObject.SetActive(false);
                else
                {
                    // It fades out over its 0.35 s instead of vanishing in one frame.
                    var flashR = flash.GetComponent<Renderer>();
                    flashR.GetPropertyBlock(block);
                    block.SetColor("_Tint", flashTint * Mathf.Clamp01((flashUntil - Time.time) / 0.35f));
                    flashR.SetPropertyBlock(block);
                }
            }
            // A shield glows at each hand while the storm is on.
            bool storm = st.Phase == Sim.Warning || st.Phase == Sim.Storm;
            for (int h = 0; h < shields.Length; h++)
            {
                if (shields[h] == null) continue;
                if (Hands != null && Hands[h].Tracked) shieldSeen[h] = Time.unscaledTime;
                bool show = storm && Hands != null && Time.unscaledTime - shieldSeen[h] < 0.3f;
                if (shields[h].gameObject.activeSelf != show) shields[h].gameObject.SetActive(show);
                if (show && Hands[h].Tracked) shields[h].position = Hands[h].Palm;
            }
            if (bRewind != null)
            {
                bool rw = st.Taken > 0 && st.Phase >= Sim.Aftermath && !rewinding && CanRewind;
                if (bRewind.gameObject.activeSelf != rw)
                {
                    bRewind.gameObject.SetActive(rw);
                    if (rw) bRewind.ArmedAt = Time.unscaledTime + 1.5f;   // a hand already there doesn't press it
                }
                bRewind.Highlight = st.Phase >= Sim.Aftermath;
            }
            if (status != null)
            {
                int left = Sim.Strikes.Count - st.Next;
                string line = "";
                Score = st.Phase == Sim.Storm ? "Blocked " + st.Blocked + " · Missed " + st.Taken + " · " + left + " to go" : "";
                if (status.Current != line) status.Say(line, 0f, rewinding ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.95f, 0.85f));
            }
            if (Look != null && defended.Count == 0)
            {
                int hitsShown = 0;
                for (int i = 0; i < Sim.Strikes.Count; i++) if ((st.HitMask & (1 << i)) != 0 && Sim.Strikes[i].Time <= viewT) hitsShown++;
                Look.Power = Mathf.Lerp(Look.Power, Mathf.Max(0.35f, 1f - 0.13f * hitsShown), Time.unscaledDeltaTime * 1.5f);
            }
            if (Look != null)
            {
                float alarm = (st.Phase == Sim.Storm ? 0.45f : st.Phase == Sim.Warning ? 0.2f : 0f) * (Effects.Calm ? 0.4f : 1f);
                Look.Alarm = Mathf.Lerp(Look.Alarm, alarm, Time.unscaledDeltaTime * 1.5f);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The world beyond your table: five wonders per world, out in the land, each under a tall soft beacon you
    /// can see from afar. Walk, glide or jump to one and it is yours ("You found the Standing Stones! 2 of 5");
    /// all five is a world explored. Wonders are made from the same art library as everything else, bigger.
    /// Found wonders are remembered per world.
    /// </summary>
    public sealed class Wonders : MonoBehaviour
    {
        public Transform Head;
        public Material World, Glow;
        public float FindRadius = 3.2f;

        public sealed class Wonder
        {
            public string Name;
            public Vector3 At;
            public bool Found;
            public Transform Root, Beacon;
        }

        public readonly List<Wonder> All = new List<Wonder>();
        public string Biome { get; private set; } = "";
        public int FoundCount { get; private set; }
        public event Action<Wonder> Discovered;

        Transform root;
        readonly HashSet<string> remembered = new HashSet<string>();
        float shownAt;

        public struct Part
        {
            public string Kind; public bool Build; public Vector3 Offset; public float Scale; public float Yaw;
            public Part(string k, float x, float z, float s, float yaw = 0f, float y = 0f, bool build = false)
            { Kind = k; Offset = new Vector3(x, y, z); Scale = s; Yaw = yaw; Build = build; }
        }

        static Part P(string k, float x, float z, float s, float yaw = 0f, float y = 0f) => new Part(k, x, z, s, yaw, y);
        static Part B(string k, float x, float z, float s, float yaw = 0f) => new Part(k, x, z, s, yaw, 0f, true);

        static Part[] Ring(string kind, int n, float r, float s, float jitter = 0f)
        {
            var list = new Part[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                list[i] = P(kind, Mathf.Cos(a) * r, Mathf.Sin(a) * r, s * (1f + jitter * Mathf.Sin(i * 2.3f)), i * 47f);
            }
            return list;
        }

        static Part[] Cat(params Part[][] parts) { var l = new List<Part>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }

        /// <summary>The five wonders of each world: name, parts, and the beacon's colour.</summary>
        public static (string name, Part[] parts, Color beacon)[] For(string biome)
        {
            var warm = new Color(1f, 0.72f, 0.38f);
            var cool = new Color(0.55f, 0.8f, 1f);
            var green = new Color(0.6f, 1f, 0.6f);
            var pink = new Color(1f, 0.65f, 0.85f);
            switch (biome)
            {
                case "campfire":
                    return new[]
                    {
                        ("the Elder Pine", new[] { P("pine", 0, 0, 4.2f), P("rock", 1.6f, 0.8f, 1.4f), P("fern", -1.2f, 0.9f, 1.6f), P("fern", 0.6f, -1.3f, 1.4f) }, warm),
                        ("the Standing Stones", Cat(Ring("rock", 7, 3.2f, 1.9f, 0.25f), new[] { P("lantern", 0, 0, 1.4f) }), cool),
                        ("the Firefly Glade", Cat(Ring("flower_patch", 6, 2.2f, 1.4f), new[] { P("log_small", 0, 0, 1.6f, 30f), P("lantern", 1f, 0.4f, 1.1f) }), green),
                        ("the Fallen Giant", new[] { P("log_small", 0, 0, 5.5f, 70f), P("fern", 2f, 1f, 1.5f), P("bush", -2f, -0.8f, 1.6f), P("rock", 2.6f, -1.2f, 1.2f) }, warm),
                        ("the Lantern Oak", new[] { P("oak", 0, 0, 2.6f), P("lantern", 1.2f, 0.3f, 1.2f, 0f, 1.6f), P("lantern", -0.9f, 0.8f, 1.2f, 0f, 2.1f), P("lantern", 0.2f, -1.1f, 1.2f, 0f, 1.2f) }, pink),
                    };
                case "beach":
                    return new[]
                    {
                        ("the Palm Grove", Cat(Ring("palm", 5, 2.6f, 1.6f, 0.2f), new[] { P("shell_rock", 0, 0, 1.4f) }), warm),
                        ("the Tide Pools", Cat(Ring("shell_rock", 6, 2f, 1.3f, 0.3f), new[] { P("shell", 0.5f, 0.3f, 3f), P("shell", -0.6f, -0.4f, 2.4f, 60f) }), cool),
                        ("the Driftwood Arch", new[] { P("log_small", -1f, 0, 3.5f, 90f), P("log_small", 1f, 0, 3.5f, 90f), P("log_small", 0, 0, 4f, 0f, 1.5f) }, warm),
                        ("the Sand Fortress", new[] { B("sandcastle", 0, 0, 3.2f), P("shell", 1.6f, 1f, 2f), P("dune_grass", -1.8f, 0.5f, 1.6f) }, pink),
                        ("the Great Shell", new[] { P("shell", 0, 0, 7f, 20f), P("dune_grass", 1.8f, -1f, 1.5f), P("dune_grass", -1.6f, 1.2f, 1.3f) }, pink),
                    };
                case "desert":
                    return new[]
                    {
                        ("the Crystal Cave", Cat(Ring("crystal_cluster", 5, 1.8f, 2.6f, 0.3f), new[] { P("rock", 0, 0, 2.2f) }), cool),
                        ("the Cactus Garden", Ring("cactus", 7, 2.6f, 1.8f, 0.3f), green),
                        ("the Sun Obelisk", new[] { B("sun_crystal", 0, 0, 3.5f), P("stone", 1.5f, 0.5f, 2f), P("stone", -1.3f, -0.7f, 1.8f) }, warm),
                        ("the Oasis", Cat(Ring("palm", 4, 2.4f, 1.5f, 0.2f), Ring("dune_grass", 6, 1.4f, 1.4f)), cool),
                        ("the Wind Rocks", new[] { P("rock", 0, 0, 3.6f), P("rock", 2.4f, 1f, 2.4f, 40f), P("rock", -2f, 1.4f, 2f, 80f) }, warm),
                    };
                case "snow":
                    return new[]
                    {
                        ("the Ice Spires", Cat(Ring("ice", 5, 1.8f, 2.8f, 0.3f), new[] { P("ice", 0, 0, 3.6f) }), cool),
                        ("the Snow Family", new[] { B("snowman", 0, 0, 2.2f), B("snowman", 1.4f, 0.5f, 1.6f, 30f), B("snowman", -1.2f, 0.7f, 1.2f, -20f) }, warm),
                        ("the Silent Pines", Ring("snow_pine", 6, 2.8f, 2.2f, 0.2f), cool),
                        ("the Frozen Boulders", new[] { P("snow_rock", 0, 0, 3.2f), P("snow_rock", 2f, 1f, 2f, 40f), P("snow_rock", -1.8f, -1.2f, 2.4f, 80f) }, cool),
                        ("the Lantern Path", Cat(Ring("lantern", 6, 2.4f, 1.3f), new[] { P("snow_pine", 0, 0, 2f) }), warm),
                    };
                case "space":
                    return new[]
                    {
                        ("the Star Map", new[] { B("star_map", 0, 0, 1.6f) }, cool),
                        ("the Antenna Array", new[] { P("antenna", 0, 0, 1.6f), P("antenna", 0.8f, 0.4f, 1.2f, 40f), P("panel", -0.8f, 0.2f, 1.2f) }, cool),
                        ("the Energy Core", new[] { P("tank", 0, 0, 1.6f), P("light_pylon", 0.7f, 0, 1.1f), P("light_pylon", -0.7f, 0, 1.1f) }, warm),
                        ("the Crystal Garden", Ring("crystal_cluster", 4, 0.6f, 1.2f), pink),
                        ("the Solar Wing", new[] { P("panel", 0, 0, 1.8f), P("panel", 0.9f, 0, 1.8f), P("panel", -0.9f, 0, 1.8f) }, cool),
                    };
                case "reef":
                    return new[]
                    {
                        ("the Coral Arch", Cat(Ring("coral", 6, 2f, 2.2f, 0.3f), new[] { P("reef_rock", 0, 0, 2.4f) }), pink),
                        ("the Kelp Forest", Ring("kelp", 8, 2.4f, 2.6f, 0.3f), green),
                        ("the Pearl Bed", new[] { P("shell_rock", 0, 0, 2.6f), P("pearl", 0.4f, 0.3f, 3f, 0f, 0.6f), B("anemone_glow", 1.6f, 0.8f, 1.4f) }, cool),
                        ("the Anemone Garden", new[] { B("anemone_glow", 0, 0, 2.2f), B("anemone_glow", 1.6f, 0.8f, 1.4f), B("anemone_glow", -1.4f, 1f, 1.6f) }, pink),
                        ("the Old Reef Rock", new[] { P("reef_rock", 0, 0, 4f), P("coral", 1.8f, 1f, 1.6f), P("kelp", -1.8f, 0.6f, 2f) }, cool),
                    };
                case "city":
                    return new[]
                    {
                        ("the Lantern Alley", Cat(Ring("street_lamp", 6, 2.6f, 1.2f), new[] { P("lantern", 0, 0, 1.6f) }), warm),
                        ("the Music Corner", new[] { B("jukebox", 0, 0, 1.6f), P("street_lamp", 1.4f, 0.4f, 1.2f), P("lantern", -1f, 0.6f, 1.2f) }, pink),
                        ("the Rain Garden", Cat(Ring("bush", 6, 2.2f, 1.4f), new[] { P("flower_patch", 0, 0, 1.8f) }), green),
                        ("the Old Hydrant", new[] { P("hydrant", 0, 0, 2.4f), P("street_lamp", 1.2f, -0.6f, 1.2f) }, warm),
                        ("the Tall Tower", new[] { P("building", 0, 0, 1.8f) }, cool),
                    };
                default:   // meadow
                    return new[]
                    {
                        ("the Blossom Giant", new[] { B("blossom_tree", 0, 0, 3.6f), P("flower_patch", 1.6f, 1f, 1.6f), P("flower_patch", -1.5f, -1f, 1.6f) }, pink),
                        ("the Flower Ring", Cat(Ring("flower_patch", 8, 2.6f, 1.5f), new[] { P("stone", 0, 0, 1.6f) }), pink),
                        ("the Old Oak", new[] { P("oak", 0, 0, 3.2f), P("bush", 2f, 1f, 1.5f), P("log_small", -2f, 0.6f, 2f, 40f) }, green),
                        ("the Wishing Stones", Ring("stone", 5, 1.8f, 2.4f, 0.3f), cool),
                        ("the Cloud Hill", new[] { P("cloud", 0, 0, 2.2f, 0f, 3.2f), P("bush", 1.4f, 0.6f, 1.4f), P("flower_patch", -1.2f, 0.8f, 1.4f) }, warm),
                    };
            }
        }

        /// <summary>
        /// Places this world's wonders around the camp: the first about 9 m ahead, the others 40–112 m out, spread
        /// around you, on land, at the terrain's height, at least 6 m apart. Where that is all sea (the beach is an
        /// island), they come in to 10–13 m.
        /// </summary>
        public void Build(Recipe r, WorldBuilder builder, Vector3 camp, Vector3 forward, float limit)
        {
            Clear();
            if (r == null || builder == null) return;
            Biome = r.Biome;
            root = new GameObject("Wonders").transform;
            root.SetParent(transform, false);
            var colors = WorldColors.For(r);
            var rng = new Rng(r.Seed * 53 + 17);
            var defs = For(r.Biome);
            bool deck = false;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float baseYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float floor = builder.GroundAt(camp.x, camp.z);
            for (int i = 0; i < defs.Length; i++)
            {
                // Spread around you, the first one ahead so the first beacon is in view.
                Vector3 at = camp;
                bool ok = false;
                // Out at its distance first; where that is all sea (the beach is an island: on Nov 15 only one of its
                // five fitted), closer in, down to the edge of the sand.
                for (int tries = 0; tries < 48 && !ok; tries++)
                {
                    float yaw = baseYaw + (i == 0 ? rng.Range(-25f, 25f) : i * 72f + rng.Range(-20f, 20f) + (tries % 24) * 13f);
                    float dist = Mathf.Min(limit - 8f, (i == 0 ? 9f : 16f + i * 24f) + rng.Range(-1f, 4f));
                    if (tries >= 24) dist = Mathf.Min(dist, 9f + i * 1f);
                    Vector3 d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    at = camp + d * dist;
                    at.y = builder.GroundAt(at.x, at.z);
                    ok = at.y > floor - 0.15f || r.Biome == "reef";   // on land (not out at sea)
                    // Apart from the others: two under one beam would read as one (review 31).
                    foreach (var other in All) { Vector3 g = other.At - at; g.y = 0f; if (g.sqrMagnitude < 36f) ok = false; }
                }
                if (!ok) continue;
                var w = new Wonder { Name = defs[i].name, At = at, Found = remembered.Contains(r.Biome + "/" + defs[i].name) };
                w.Root = new GameObject("Wonder " + defs[i].name).transform;
                w.Root.SetParent(root, false);
                w.Root.position = at;
                var mb = new MeshBuilder();
                foreach (var part in defs[i].parts)
                {
                    Vector3 o = part.Offset * (deck ? 0.35f : 1f);
                    float y = builder.GroundAt(at.x + o.x, at.z + o.z) - at.y - 0.03f + o.y;
                    mb.Push(new Vector3(o.x, y, o.z), Quaternion.Euler(0f, part.Yaw + rng.Range(-15f, 15f), 0f), Vector3.one * part.Scale * (deck ? 0.6f : 1f));
                    var c = colors;
                    c.Prop = WorldColors.Jitter(colors.Prop, rng); c.PropAccent = WorldColors.Jitter(colors.PropAccent, rng);
                    if (part.Build) Builds.Result(mb, part.Kind, c, rng, 0.6f);
                    else Props.Build(mb, part.Kind, c, rng, 1f);
                    mb.Pop();
                }
                Util.MeshObject("Shape", w.Root, mb.ToMesh("wonder-" + i), World);
                // The beacon: a tall soft column of light (fades once found).
                var beacon = Util.MeshObject("Beacon", w.Root, BeaconMesh(defs[i].beacon), Glow);
                beacon.transform.localScale = deck ? new Vector3(0.5f, 3f, 0.5f) : new Vector3(2.4f, 60f, 2.4f);
                w.Beacon = beacon.transform;
                if (w.Found) { beacon.SetActive(false); FoundCount++; }
                All.Add(w);
            }
            shownAt = Time.time;
            Trace.Info("Wonders", "placed", "biome", r.Biome, "count", All.Count, "found_before", FoundCount,
                       "nearest_m", All.Count > 0 ? Vector3.Distance(camp, All[0].At) : 0f);
        }

        public void Remember(IEnumerable<string> found) { remembered.Clear(); if (found != null) foreach (var f in found) remembered.Add(f); }

        public void Clear()
        {
            All.Clear();
            FoundCount = 0;
            Biome = "";
            if (root != null) Util.Kill(root.gameObject);
            root = null;
        }

        /// <summary>The nearest wonder not yet found (null if all are found).</summary>
        public Wonder NextFrom(Vector3 p)
        {
            Wonder best = null;
            float d = float.MaxValue;
            foreach (var w in All) if (!w.Found) { float k = Vector3.Distance(p, w.At); if (k < d) { d = k; best = w; } }
            return best;
        }

        void Update()
        {
            if (All.Count == 0 || Head == null || Time.time - shownAt < 1f) return;
            Vector3 h = Head.position;
            foreach (var w in All)
            {
                if (w.Beacon != null && w.Beacon.gameObject.activeSelf)
                {
                    // Beacons breathe; close up they thin out so they don't fill the view.
                    float d = new Vector2(h.x - w.At.x, h.z - w.At.z).magnitude;
                    float k = Mathf.Clamp01(d / 12f);
                    var s = w.Beacon.localScale;
                    float width = 2.4f * Mathf.Lerp(0.35f, 1f, k) * (1f + 0.06f * Mathf.Sin(Time.time * 1.7f + w.At.x));
                    w.Beacon.localScale = new Vector3(width, s.y, width);
                }
                if (w.Found) continue;
                Vector2 flat = new Vector2(h.x - w.At.x, h.z - w.At.z);
                if (flat.magnitude > FindRadius * (false ? 0.45f : 1f)) continue;
                w.Found = true;
                FoundCount++;
                remembered.Add(Biome + "/" + w.Name);
                if (w.Beacon != null) w.Beacon.gameObject.SetActive(false);
                var burst = Effects.Burst(w.Root, Glow, Color.white);
                burst.transform.localPosition = Vector3.up * 1.5f;
                burst.Play();
                Sfx.Instance?.At("souvenir_chime", w.At + Vector3.up, 0.9f);
                Trace.Info("Wonders", "found", "name", w.Name, "found", FoundCount, "of", All.Count);
                Discovered?.Invoke(w);
            }
        }

        static readonly Dictionary<Color, Mesh> beacons = new Dictionary<Color, Mesh>();

        /// <summary>A soft column 1 unit tall: two crossed quads, bright at the bottom, fading up (Glow, no radial).</summary>
        static Mesh BeaconMesh(Color c)
        {
            if (beacons.TryGetValue(c, out var m) && m != null) return m;
            var v = new List<Vector3>(); var col = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            var lo = new Color(c.r, c.g, c.b, 0.55f);
            var hi = new Color(c.r, c.g, c.b, 0f);
            for (int q = 0; q < 2; q++)
            {
                Vector3 side = q == 0 ? Vector3.right : Vector3.forward;
                int b = v.Count;
                v.Add(-side * 0.5f); v.Add(side * 0.5f); v.Add(side * 0.5f + Vector3.up); v.Add(-side * 0.5f + Vector3.up);
                col.Add(lo); col.Add(lo); col.Add(hi); col.Add(hi);
                // The Glow shader's radial falloff runs across uv x: soft edges left and right.
                uv.Add(new Vector2(0f, 0.5f)); uv.Add(new Vector2(1f, 0.5f)); uv.Add(new Vector2(1f, 0.5f)); uv.Add(new Vector2(0f, 0.5f));
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            m = new Mesh { name = "beacon" };
            m.SetVertices(v); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            beacons[c] = m;
            return m;
        }
    }
}

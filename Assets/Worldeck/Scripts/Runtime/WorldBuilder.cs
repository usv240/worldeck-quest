using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Worldeck.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Worldeck
{
    /// <summary>
    /// Builds a world around the real room from a <see cref="Recipe"/>: sky, ground (flat where the room is,
    /// rising away from it), three horizon layers, props scattered outside the walls, extras (northern
    /// lights, clouds, lanterns, flowers, crystals) and weather that follows the head. Everything is merged
    /// into a few meshes per group (sectors, so they cull), within the research budget (≤ 300k triangles,
    /// ≤ 150 draw calls). Builds all at once (editor, tests) or spread over frames (on the headset).
    /// </summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        public Material World, Sky, Silhouette, Glow, GlowFlat;
        [Tooltip("The soft round shadow under animals that wander (Shadow shader).")]
        public Material Blob;
        static Mesh blobQuad;
        public WorldLook Look;
        public Transform Head;
        [Tooltip("Milliseconds of building per frame on the headset.")]
        public float BudgetMs = 4f;
        [Tooltip("Photographic ground (Ground shader) and the sets to use per world.")]
        public Material GroundMat;
        public Grounds Grounds;
        public int MaxTriangles = 240000;
        /// <summary>The kind of the animal being brought home: the world's herd is of its kind ("" for none).</summary>
        [System.NonSerialized] public string Herd = "";

        public Recipe Current { get; private set; }
        public RoomModel Room { get; private set; }
        public Transform Root { get; private set; }
        public bool Building { get; private set; }
        public int Triangles { get; private set; }
        public int Renderers { get; private set; }
        public float LastBuildMs { get; private set; }

        Transform weather, extras;
        ParticleSystem rain, snow, bubbles, fireflies, shooting;
        readonly List<AuroraRibbon> auroras = new List<AuroraRibbon>();

        const int Sectors = 8;
        float floorY, innerR, cx, cz;
        int seed = 1;
        Rng terrainRng;
        BiomeDef def;

        // ---------- public ----------

        /// <summary>Builds the whole world now (editor captures, tests).</summary>
        public void BuildNow(Recipe r, RoomModel room)
        {
            var it = BuildRoutine(r, room, float.PositiveInfinity);
            while (it.MoveNext()) { }
        }

        /// <summary>Builds the world over frames into a new root; the old one stays until the new one is done.</summary>
        public IEnumerator BuildRoutine(Recipe r, RoomModel room, float budgetMs = -1f)
        {
            if (budgetMs < 0f) budgetMs = BudgetMs;
            Building = true;
            var total = Stopwatch.StartNew();
            var slice = Stopwatch.StartNew();
            r = r.Copy().Clamp();
            def = r.Def;
            Room = room;
            floorY = room.FloorY;
            Vector3 h = room.Head;
            cx = h.x; cz = h.z;
            innerR = room.OuterRadius + 0.4f;
            seed = r.Seed;
            terrainRng = new Rng(r.Seed * 977 + 11);
            noiseOffset = new Vector2(terrainRng.Range(0f, 900f), terrainRng.Range(0f, 900f));
            var colors = WorldColors.For(r);

            if (building != null) Util.Kill(building.gameObject);   // a build cut short
            var newRoot = new GameObject("World: " + def.Id).transform;
            building = newRoot;
            newRoot.SetParent(transform, false);
            newRoot.gameObject.SetActive(Root == null || Root.gameObject.activeSelf);
            int tris = 0, rends = 0;

            IEnumerator Step()
            {
                if (slice.Elapsed.TotalMilliseconds > budgetMs) { yield return null; slice.Restart(); }
            }

            // Sky.
            BuildSky(newRoot);
            rends++;
            yield return Step();

            // Ground.
            var ground = BuildGround(colors);
            tris += ground.TriangleCount;
            var groundGo = Util.MeshObject("Ground", newRoot, ground.ToMesh("ground"), World);
            if (GroundMat != null && Grounds != null && Grounds.TryFor(def.Id, out var set))
            {
                var gr = groundGo.GetComponent<Renderer>();
                gr.sharedMaterial = GroundMat;
                Grounds.Apply(gr, set);
            }
            rends++;
            if (def.Horizon == "sea")
            {
                var sea = BuildSea(colors);
                tris += sea.TriangleCount;
                Util.MeshObject("Sea", newRoot, sea.ToMesh("sea"), World);
                rends++;
            }
            yield return Step();

            // Horizon layers.
            var hor = BuildHorizon(colors);
            if (hor.TriangleCount > 0)
            {
                tris += hor.TriangleCount;
                Util.MeshObject("Horizon", newRoot, hor.ToMesh("horizon"), Silhouette);
                rends++;
            }
            if (def.Horizon == "planet") { BuildPlanet(newRoot, colors); rends += 2; }
            if (Wildlife.PlaceOf(r) == "volcano") tris += BuildVolcano(newRoot, colors, ref rends);
            yield return Step();

            // Scatter, sector by sector.
            var sectors = new MeshBuilder[Sectors];
            for (int i = 0; i < Sectors; i++) sectors[i] = new MeshBuilder();
            var rng = new Rng(r.Seed * 131 + 7);
            var placed = new List<Vector3>();
            // The one thing on the horizon that makes this a real place; nothing is scattered where it stands.
            landmarkDir = Vector3.zero;
            landmarkFront = 0f;
            if (r.Look != null && Landmarks.Has(r.Look.Landmark)) { tris += BuildLandmark(newRoot, r.Look.Landmark, colors, placed); rends++; }
            int count = Mathf.RoundToInt(BaseCount(def.Id) * r.Density * 2.2f);
            int nearCount = Mathf.RoundToInt(count * 0.14f);
            for (int n = 0, tries = 0; n < count && tries < count * 6; tries++)
            {
                bool near = n < nearCount;
                string kind = def.Scatter[rng.Range(0, def.Scatter.Length)];
                float scale = rng.Range(0.8f, 1.25f);
                float rad = Props.Radius(kind) * scale;
                float dist = near ? rng.Range(innerR + 0.4f, innerR + 6f) : innerR + 6f + Mathf.Pow(rng.Next01(), 1.35f) * 190f;
                float ang = rng.Range(0f, Mathf.PI * 2f);
                var p = new Vector3(cx + Mathf.Cos(ang) * dist, 0f, cz + Mathf.Sin(ang) * dist);
                if (TooClose(placed, p, rad)) continue;
                // Nothing tall between your seat and the landmark (a blossom tree stood in front of the pagoda).
                if (!LowThings.Contains(kind) && InLandmarkView(p)) continue;
                if (InForecourt(p)) continue;
                // Tall things don't stand right outside the wall in front: keep the first view open.
                if (near && rad > 0.6f && FrontArc(p)) continue;
                p.y = HeightAt(p.x, p.z) - 0.02f;
                int sector = SectorOf(p);
                var mb = sectors[sector];
                if (tris + mb.TriangleCount > MaxTriangles) break;
                mb.AoBaseY = p.y;
                mb.AoHeight = Mathf.Max(0.15f, rad * 0.6f);
                mb.Push(p, Quaternion.Euler(rng.Range(-4f, 4f), rng.Range(0f, 360f), rng.Range(-4f, 4f)), Vector3.one * scale);
                var c = colors;
                c.Prop = WorldColors.Jitter(colors.Prop, rng); c.PropAccent = WorldColors.Jitter(colors.PropAccent, rng);
                Props.Build(mb, kind, c, rng, 1f);
                mb.Pop();
                mb.AoBaseY = float.NaN;
                placed.Add(new Vector3(p.x, rad, p.z));
                n++;
                if (slice.Elapsed.TotalMilliseconds > budgetMs) { yield return null; slice.Restart(); }
            }
            for (int i = 0; i < Sectors; i++)
            {
                if (sectors[i].TriangleCount == 0) continue;
                tris += sectors[i].TriangleCount;
                Util.MeshObject("Scatter " + i, newRoot, sectors[i].ToMesh("scatter" + i), World);
                rends++;
                yield return Step();
            }

            // Life: real animals at their real size, out in the world.
            int wildTris = BuildWildlife(r, newRoot, placed, out int wildCount);
            tris += wildTris;
            rends += wildCount;
            // Someone's in it: the world's local, just past where your walls were.
            int localTris = BuildLocal(r, newRoot, placed);
            if (localTris > 0) { tris += localTris; rends++; }
            yield return Step();

            // Extras and weather.
            extras = new GameObject("Extras").transform;
            extras.SetParent(newRoot, false);
            weather = new GameObject("Weather").transform;
            weather.SetParent(newRoot, false);
            tris += BuildExtras(r, colors, extras, ref rends);
            BuildWeather(r, colors);

            // Swap.
            if (Root != null) Util.Kill(Root.gameObject);
            Root = newRoot;
            building = null;
            Current = r;
            Triangles = tris;
            Renderers = rends;
            LastBuildMs = (float)total.Elapsed.TotalMilliseconds;
            Building = false;
            Debug.Log($"[Worldeck] world built: {r} in {LastBuildMs:0} ms, {tris} triangles, {rends} renderers");
        }

        /// <summary>Changes that don't need a rebuild: light, weather, extras. Returns false if a rebuild is needed.</summary>
        public bool Edit(Recipe r)
        {
            if (Current == null || Root == null || r.Biome != Current.Biome || r.Seed != Current.Seed ||
                (r.Look?.Name ?? "") != (Current.Look?.Name ?? "") || Mathf.Abs(r.Density - Current.Density) > 0.01f)
                return false;
            var colors = WorldColors.For(r);
            if (string.Join(",", r.Extras) != string.Join(",", Current.Extras))
            {
                Util.Kill(extras.gameObject);
                extras = new GameObject("Extras").transform;
                extras.SetParent(Root, false);
                int rends = 0;
                Triangles += BuildExtras(r, colors, extras, ref rends);
            }
            if (r.Precip != Current.Precip || Mathf.Abs(r.Wind - Current.Wind) > 0.01f || !r.Extras.Contains(Extras.Fireflies) != !Current.Extras.Contains(Extras.Fireflies) ||
                r.Extras.Contains(Extras.ShootingStars) != Current.Extras.Contains(Extras.ShootingStars))
            {
                Util.KillChildren(weather);
                BuildWeather(r, colors);
            }
            Current = r.Copy();
            return true;
        }

        public void SetVisible(bool on) { if (Root != null) Root.gameObject.SetActive(on); }

        Transform building;

        public void Clear()
        {
            if (building != null) Util.Kill(building.gameObject);
            building = null;
            if (Root != null) Util.Kill(Root.gameObject);
            Root = null;
            Current = null;
        }

        Transform sky;

        void LateUpdate()
        {
            if (sky != null && Head != null) sky.position = new Vector3(Head.position.x, floorY, Head.position.z);
            if (weather != null && Head != null)
                weather.position = new Vector3(Head.position.x, floorY, Head.position.z);
            foreach (var a in auroras) if (a != null) a.Tick(Time.time);
        }

        // ---------- terrain ----------

        Vector2 noiseOffset;

        /// <summary>Ground height at a point: the real floor inside and near the room, rising with distance.</summary>
        public float HeightAt(float x, float z)
        {
            float r = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
            float h = def != null ? def.TerrainHeight : 0f;
            if (def != null && def.Horizon == "sea")
            {
                // A beach: flat sand, then sloping under the water from 14 m.
                float slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerR + 10f, innerR + 22f, r));
                float dunes = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerR, innerR + 6f, r)) * (Fbm(x, z) - 0.5f) * 0.5f;
                return floorY - 0.01f + dunes * (1f - slope) - slope * 1.2f;
            }
            if (h <= 0f) return floorY - 0.01f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerR + 1.5f, innerR + 14f, r));
            float far = Mathf.InverseLerp(30f, 130f, r);
            float beyond = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(170f, 340f, r));
            return floorY - 0.01f + k * Fbm(x, z) * h * (0.6f + far * 1.4f) * Mathf.Lerp(0.2f, 1f, beyond);
        }

        float Fbm(float x, float z)
        {
            float a = 0f, amp = 0.55f, f = 0.018f, sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                a += amp * Mathf.PerlinNoise(x * f + noiseOffset.x + i * 17.3f, z * f + noiseOffset.y + i * 9.1f);
                sum += amp;
                amp *= 0.5f; f *= 2.1f;
            }
            return a / sum;
        }

        float gR0 = 1f, gQ = 1.2f;
        int gSeg = 96, gRings = 40;

        /// <summary>
        /// The height of the ground mesh itself at a point (the same triangles BuildGround made), so feet, jump
        /// targets and landmarks sit exactly on what is drawn, however coarse the far rings are.
        /// </summary>
        public float GroundAt(float x, float z)
        {
            float dx = x - cx, dz = z - cz;
            float r = Mathf.Sqrt(dx * dx + dz * dz);
            if (def == null || r <= gR0) return floorY - 0.01f;
            int ring = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(r / gR0) / Mathf.Log(gQ)), 0, gRings - 1);
            float ra = gR0 * Mathf.Pow(gQ, ring), rb = gR0 * Mathf.Pow(gQ, ring + 1);
            float ang = Mathf.Atan2(dz, dx);
            if (ang < 0f) ang += Mathf.PI * 2f;
            float fa = ang / (Mathf.PI * 2f) * gSeg;
            int i = Mathf.Clamp(Mathf.FloorToInt(fa), 0, gSeg - 1);
            float v = fa - i, u = Mathf.Clamp01((r - ra) / Mathf.Max(1e-4f, rb - ra));
            float a0 = i * Mathf.PI * 2f / gSeg, a1 = (i + 1) * Mathf.PI * 2f / gSeg;
            float H(float rr, float aa) => HeightAt(cx + Mathf.Cos(aa) * rr, cz + Mathf.Sin(aa) * rr);
            float ha = H(ra, a0), hb = H(ra, a1), hd = H(rb, a0), he = H(rb, a1);
            // Triangles (a, b, d) and (b, e, d), as BuildGround winds them.
            return u + v <= 1f ? ha + (hd - ha) * u + (hb - ha) * v : he + (hb - he) * (1f - u) + (hd - he) * (1f - v);
        }

        MeshBuilder BuildGround(WorldColors c)
        {
            var mb = new MeshBuilder();
            int seg = 64;
            // Inner disc at floor height (the room's floor once the room has gone).
            float y = floorY - 0.01f;
            var center = new Vector3(cx, y, cz);
            var rng = new Rng(7);
            Color inner = def.Id == "space" ? c.GroundAccent : c.Ground;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                var p0 = new Vector3(cx + Mathf.Cos(a0) * innerR, HeightAt(cx + Mathf.Cos(a0) * innerR, cz + Mathf.Sin(a0) * innerR), cz + Mathf.Sin(a0) * innerR);
                var p1 = new Vector3(cx + Mathf.Cos(a1) * innerR, HeightAt(cx + Mathf.Cos(a1) * innerR, cz + Mathf.Sin(a1) * innerR), cz + Mathf.Sin(a1) * innerR);
                mb.Tri(center, p1, p0, WorldColors.Jitter(inner, rng, 0.03f));
            }
            if (def.Id == "space-deck-retired")
            {
                // (The old deck: plates out to a railing, then the void.)
                float deck = innerR + 5f;
                RingStrip(mb, innerR, deck, y, y, 48, c.Ground, c.GroundAccent, rng);
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI * 2f / 48f;
                    var p = new Vector3(cx + Mathf.Cos(a) * deck, y, cz + Mathf.Sin(a) * deck);
                    mb.Cylinder(p, 0.03f, 0.03f, 1.05f, 5, c.Prop);
                }
                RingStrip(mb, deck - 0.03f, deck + 0.03f, y + 1.05f, y + 1.05f, 48, c.PropAccent, c.PropAccent, rng, 0.6f);
                return mb;
            }
            // Rings out to 760 m, geometric spacing (remembered, so GroundAt can read the same triangles).
            seg = 96;
            int rings = 40;
            float r0 = innerR, rMax = 760f;
            float q = Mathf.Pow(rMax / r0, 1f / rings);
            gR0 = r0; gQ = q; gSeg = seg; gRings = rings;
            for (int ring = 0; ring < rings; ring++)
            {
                float ra = r0 * Mathf.Pow(q, ring), rb = r0 * Mathf.Pow(q, ring + 1);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    Vector3 P(float rr, float aa) { float x = cx + Mathf.Cos(aa) * rr, z = cz + Mathf.Sin(aa) * rr; return new Vector3(x, HeightAt(x, z), z); }
                    var a = P(ra, a0); var b = P(ra, a1); var d = P(rb, a0); var e = P(rb, a1);
                    float slope = Mathf.Abs(e.y - a.y) / Mathf.Max(0.1f, rb - ra);
                    float patch = Mathf.PerlinNoise(a.x * 0.08f + 3f, a.z * 0.08f + 5f);
                    Color col = slope > 0.45f || patch > 0.68f ? c.GroundAccent : c.Ground;
                    if (def.Horizon == "sea" && a.y < floorY - 0.35f) col = Color.Lerp(c.Ground, c.GroundAccent, 0.6f);
                    col = WorldColors.Jitter(col, rng, 0.04f);
                    mb.Tri(a, b, d, col);
                    mb.Tri(b, e, d, WorldColors.Jitter(col, rng, 0.03f));
                }
            }
            return mb;
        }

        static void RingStrip(MeshBuilder mb, float r0, float r1, float y0, float y1, int seg, Color a, Color b, Rng rng, float emissive = 0f)
        {
            var host = mb.Current;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Color col = (i & 1) == 0 ? a : b;
                var p0 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0);
                var p1 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                var p2 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                var p3 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1);
                mb.Quad(p0, p1, p2, p3, WorldColors.Jitter(col, rng, 0.05f), emissive);   // faces up (+Y) for r1 > r0
            }
        }

        MeshBuilder BuildSea(WorldColors c)
        {
            var mb = new MeshBuilder();
            var deep = WorldColors.Hex("#1F6F8B", Color.blue);
            var shallow = WorldColors.Hex("#4FC3C9", Color.cyan);
            float y = floorY - 0.32f;
            int seg = 64, rings = 14;
            float r0 = innerR + 11f, rMax = 380f;
            float q = Mathf.Pow(rMax / r0, 1f / rings);
            var rng = new Rng(3);
            for (int ring = 0; ring < rings; ring++)
            {
                float ra = r0 * Mathf.Pow(q, ring), rb = r0 * Mathf.Pow(q, ring + 1);
                Color col = Color.Lerp(shallow, deep, Mathf.Clamp01(ring / 5f));
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    Vector3 P(float rr, float aa) => new Vector3(cx + Mathf.Cos(aa) * rr, y, cz + Mathf.Sin(aa) * rr);
                    // Wind weight on the water lets the shader roll it gently.
                    mb.Tri(P(ra, a0), P(ra, a1), P(rb, a0), WorldColors.Jitter(col, rng, 0.05f), 0.05f, 0.4f, 0.4f, 0.4f);
                    mb.Tri(P(ra, a1), P(rb, a1), P(rb, a0), WorldColors.Jitter(col, rng, 0.05f), 0.05f, 0.4f, 0.4f, 0.4f);
                }
            }
            // A ring of surf where the sand meets the water.
            RingStripAt(mb, r0 - 0.6f, r0 + 0.4f, y + 0.02f, 64, new Color(0.95f, 0.97f, 1f), rng);
            return mb;
        }

        void RingStripAt(MeshBuilder mb, float r0, float r1, float y, int seg, Color col, Rng rng)
        {
            mb.Push(new Vector3(cx, 0, cz));
            RingStrip(mb, r0, r1, y, y, seg, col, col * 0.95f, rng, 0.15f);
            mb.Pop();
        }

        // ---------- horizon ----------

        MeshBuilder BuildHorizon(WorldColors c)
        {
            var mb = new MeshBuilder();
            string kind = def.Horizon;
            if (kind == "sea" || kind == "planet") return mb;
            float[] dist = { 330f, 470f, 640f };
            float[] fog = { 0.38f, 0.66f, 0.86f };
            float[] height = kind == "skyline" ? new[] { 18f, 34f, 60f } : kind == "dunes" ? new[] { 6f, 12f, 22f } :
                             kind == "hills" ? new[] { 8f, 16f, 30f } : kind == "reef" ? new[] { 7f, 12f, 18f } : new[] { 12f, 32f, 70f };
            if (def.Id == "snow") { height[0] = 18f; height[1] = 48f; height[2] = 95f; }
            var rng = new Rng(seed * 3 + 1);
            for (int layer = 0; layer < 3; layer++)
            {
                float r = dist[layer];
                int seg = kind == "skyline" ? 120 : 96;
                Color col = layer == 0 ? Color.Lerp(c.Prop, c.GroundAccent, 0.35f) : layer == 1 ? Color.Lerp(c.Prop, c.Ground, 0.3f) : c.Prop;
                if (def.Id == "snow") col = Color.Lerp(c.GroundAccent, Color.white, 0.3f + 0.2f * layer);
                if (kind == "dunes") col = Color.Lerp(c.GroundAccent, c.Ground, 0.4f + layer * 0.2f);
                float baseY = floorY - 40f;
                float phase = rng.Range(0f, 100f);
                float prev = 0f;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float top = Profile(kind, a, phase, layer) * height[layer] * 3.6f;
                    if (i > 0)
                    {
                        float a0 = (i - 1) * Mathf.PI * 2f / seg;
                        var b0 = new Vector3(cx + Mathf.Cos(a0) * r, baseY, cz + Mathf.Sin(a0) * r);
                        var b1 = new Vector3(cx + Mathf.Cos(a) * r, baseY, cz + Mathf.Sin(a) * r);
                        var t0 = new Vector3(b0.x, floorY + prev, b0.z);
                        var t1 = new Vector3(b1.x, floorY + top, b1.z);
                        if (kind == "skyline")
                        {
                            // Flat-topped buildings: hold the height across the segment, then step.
                            t1.y = t0.y;
                            mb.Quad(b0, t0, t1, b1, col, fog[layer], 0f, 0f);
                            Windows(mb, b0, b1, t0.y, rng, c, fog[layer]);
                        }
                        else mb.Quad(b0, t0, t1, b1, col, fog[layer], 0f, 0f);
                    }
                    prev = top;
                }
            }
            return mb;
        }

        static float Profile(string kind, float a, float phase, int layer)
        {
            float x = a * (6f + layer * 3f) + phase;
            switch (kind)
            {
                case "dunes": return 0.55f + 0.3f * Mathf.Sin(x * 0.7f) + 0.15f * Mathf.Sin(x * 1.9f + 1.3f);
                case "hills": return 0.5f + 0.35f * Mathf.Sin(x * 0.8f) + 0.15f * Mathf.Sin(x * 2.3f);
                case "skyline":
                    {
                        float h = Mathf.PerlinNoise(Mathf.Floor(a * 40f / (1 + layer)) * 0.37f, phase);
                        return 0.35f + 0.65f * h * h;
                    }
                case "reef": return 0.4f + 0.5f * Mathf.Abs(Mathf.Sin(x * 0.9f)) * Mathf.PerlinNoise(x * 0.2f, phase);
                default:
                    {
                        // Ridged mountains.
                        float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.35f, phase) * 2f - 1f);
                        float m = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.9f, phase + 3f) * 2f - 1f);
                        return 0.25f + 0.55f * n * n + 0.2f * m;
                    }
            }
        }

        void Windows(MeshBuilder mb, Vector3 b0, Vector3 b1, float topY, Rng rng, WorldColors c, float fog)
        {
            Vector3 along = b1 - b0;
            float len = along.magnitude;
            if (len < 1f) return;
            Vector3 inward = new Vector3(cx - b0.x, 0, cz - b0.z).normalized * 0.05f;
            int cols = Mathf.Clamp((int)(len / 1.6f), 1, 6);
            for (float y = floorY + 2f; y < topY - 1.5f; y += 2.6f)
                for (int i = 0; i < cols; i++)
                {
                    if (rng.Next01() > 0.18f) continue;
                    float t = (i + 0.5f) / cols;
                    Vector3 p = b0 + along * t + inward;
                    Vector3 w = along.normalized * 0.45f, up = Vector3.up * 0.7f;
                    Color col = rng.Next01() < 0.25f ? c.Neon : c.Emissive;
                    mb.Quad(p - w, p - w + up, p + w + up, p + w, col, fog, 1f, 1f);
                }
        }

        void BuildPlanet(Transform root, WorldColors c)
        {
            var mb = new MeshBuilder();
            var rng = new Rng(17);
            Vector3 center = new Vector3(cx + 140f, floorY - 30f, cz + 210f);
            mb.Ico(center, Vector3.one * 95f, Color.Lerp(c.PropAccent, c.Horizon, 0.35f), rng, 0.02f);
            mb.Ico(center + new Vector3(-6f, 8f, -4f), Vector3.one * 93f, Color.Lerp(c.Emissive, c.Horizon, 0.5f), rng, 0.04f, 0.2f);
            Util.MeshObject("Planet", root, mb.ToMesh("planet"), World);
            var ring = new MeshBuilder();
            ring.Push(center, Quaternion.Euler(14f, 20f, 8f), Vector3.one);
            RingStrip(ring, 120f, 150f, 0f, 0f, 96, c.PropAccent, c.Prop, rng, 0.25f);
            RingStrip(ring, 150f, 120f, 0f, 0f, 96, c.PropAccent, c.Prop, rng, 0.25f);
            ring.Pop();
            Util.MeshObject("PlanetRing", root, ring.ToMesh("ring"), World);
        }

        // ---------- sky ----------

        void BuildSky(Transform root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Sky";
            Util.Kill(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(cx, floorY, cz);
            go.transform.localScale = Vector3.one * 1720f;
            sky = go.transform;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Sky;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ---------- extras ----------

        int BuildExtras(Recipe r, WorldColors c, Transform root, ref int rends)
        {
            int tris = 0;
            auroras.Clear();
            var rng = new Rng(r.Seed * 53 + 3);
            var near = new MeshBuilder();
            var far = new MeshBuilder();
            foreach (var e in r.Extras)
            {
                switch (e)
                {
                    case Extras.Aurora:
                        for (int i = 0; i < 3; i++)
                        {
                            var go = new GameObject("Aurora " + i);
                            go.transform.SetParent(root, false);
                            var a = go.AddComponent<AuroraRibbon>();
                            float ang = rng.Range(-0.6f, 0.6f) + Mathf.Atan2(Room.Forward.z, Room.Forward.x);
                            a.Build(new Vector3(cx, floorY, cz), ang, 75f + i * 18f, 45f + i * 9f, GlowFlat, rng.Range(0f, 10f));
                            auroras.Add(a);
                            rends++;
                        }
                        break;
                    case Extras.Clouds:
                        for (int i = 0; i < 9; i++)
                        {
                            float ang = rng.Range(0f, Mathf.PI * 2f), d = rng.Range(70f, 170f);
                            var p = new Vector3(cx + Mathf.Cos(ang) * d, floorY + rng.Range(38f, 70f), cz + Mathf.Sin(ang) * d);
                            far.Push(p, Quaternion.Euler(0, rng.Range(0f, 360f), 0), Vector3.one * rng.Range(1.6f, 3.2f));
                            Props.Build(far, "cloud", c, rng);
                            far.Pop();
                        }
                        break;
                    case Extras.Lanterns:
                        foreach (var w in Room.Walls)
                        {
                            Vector3 ab = w.B - w.A;
                            int n = Mathf.Clamp(Mathf.RoundToInt(ab.magnitude / 1.4f), 1, 4);
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 p = w.A + ab * ((i + 0.5f) / n) + w.Normal * 0.45f;
                                p.y = Mathf.Min(Room.CeilingY - 0.35f, floorY + 2.25f) + rng.Range(-0.08f, 0.08f);
                                near.Push(p, Quaternion.Euler(0, rng.Range(0f, 360f), 0), Vector3.one * rng.Range(0.9f, 1.15f));
                                Props.Build(near, "lantern", c, rng);
                                near.Pop();
                            }
                        }
                        break;
                    case Extras.Flowers:
                        for (int i = 0; i < 46; i++)
                        {
                            float ang = rng.Range(0f, Mathf.PI * 2f), d = innerR + rng.Range(0.3f, 7f);
                            var p = new Vector3(cx + Mathf.Cos(ang) * d, 0, cz + Mathf.Sin(ang) * d);
                            p.y = HeightAt(p.x, p.z);
                            near.Push(p, Quaternion.Euler(0, rng.Range(0f, 360f), 0), Vector3.one * rng.Range(0.8f, 1.3f));
                            Props.Build(near, "flower", c, rng);
                            near.Pop();
                        }
                        break;
                    case Extras.Crystals:
                        for (int i = 0; i < 10; i++)
                        {
                            float ang = rng.Range(0f, Mathf.PI * 2f), d = innerR + rng.Range(0.6f, 9f);
                            var p = new Vector3(cx + Mathf.Cos(ang) * d, 0, cz + Mathf.Sin(ang) * d);
                            p.y = HeightAt(p.x, p.z) - 0.05f;
                            near.Push(p, Quaternion.Euler(0, rng.Range(0f, 360f), rng.Range(-8f, 8f)), Vector3.one * rng.Range(1.2f, 2.6f));
                            Props.Build(near, "crystal_cluster", c, rng);
                            near.Pop();
                        }
                        break;
                }
            }
            if (near.TriangleCount > 0) { tris += near.TriangleCount; Util.MeshObject("Near extras", root, near.ToMesh("near"), World); rends++; }
            if (far.TriangleCount > 0) { tris += far.TriangleCount; Util.MeshObject("Clouds", root, far.ToMesh("clouds"), World); rends++; }
            return tris;
        }

        void BuildWeather(Recipe r, WorldColors c)
        {
            rain = snow = bubbles = fireflies = shooting = null;
            if (r.Precip == Precip.Rain) { rain = Effects.Rain(weather, GlowFlat); rain.Play(); }
            if (r.Precip == Precip.Snow) { snow = Effects.Snow(weather, Glow); snow.Play(); }
            if (r.Precip == Precip.Bubbles) { bubbles = Effects.Bubbles(weather, Glow); bubbles.Play(); }
            if (r.Extras.Contains(Extras.Fireflies)) { fireflies = Effects.Fireflies(weather, Glow, WorldColors.Hex("#D9FF6B", Color.yellow)); fireflies.Play(); }
            if (r.Extras.Contains(Extras.ShootingStars)) { shooting = Effects.ShootingStars(weather, GlowFlat); shooting.Play(); }
            if (r.Extras.Contains(Extras.Embers))
            {
                var em = Effects.Embers(weather, Glow, 6f);
                var sh = em.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(8f, 8f, 0.5f);
                em.transform.localPosition = new Vector3(0, 0.2f, 0);
                em.Play();
            }
            if (rain != null) { var e = rain.emission; e.rateOverTime = Mathf.Lerp(300f, 520f, r.Wind); }
        }

        // ---------- placement helpers ----------

        static int BaseCount(string biome) => biome switch
        {
            "campfire" => 150, "meadow" => 130, "beach" => 70, "desert" => 60, "snow" => 120,
            "space" => 28, "reef" => 140, "city" => 70, _ => 100
        };

        /// <summary>
        /// The volcano itself, for the volcano place: a broad dark cone ahead and to one side, lava running down its
        /// flanks and its crater glowing against the night. Returns its triangles.
        /// </summary>
        static Material farMat;

        /// <summary>Looking this way from your seat, a little above level, none of your furniture is in the way.</summary>
        public bool ClearView(Vector3 flatDir)
        {
            if (Room == null) return true;
            Vector3 ray = (flatDir.normalized + Vector3.up * 0.14f).normalized;
            for (float t = 0.3f; t <= Room.OuterRadius + 0.5f; t += 0.2f)
            {
                Vector3 q = Room.Head + ray * t;
                foreach (var piece in Room.Pieces)
                    if (!piece.Virtual && piece.Distance(q) < 0.12f) return false;
            }
            return true;
        }

        Vector3 landmarkDir, landmarkAt;
        float landmarkFront;

        /// <summary>The spot is in the landmark's forecourt (its garden and water, running towards you): nothing is scattered there.</summary>
        public bool InForecourt(Vector3 p)
        {
            if (landmarkFront <= 0f || landmarkDir == Vector3.zero) return false;
            float dx = p.x - landmarkAt.x, dz = p.z - landmarkAt.z;
            float towardsYou = -(dx * landmarkDir.x + dz * landmarkDir.z);
            float across = Mathf.Abs(dx * landmarkDir.z - dz * landmarkDir.x);
            return towardsYou > 0f && towardsYou < landmarkFront + 4f && across < 20f;
        }
        float landmarkSpread = 0.13f;
        /// <summary>Scatter that stays below your eye line: everything else (a tree, a lamp post, a building) is kept out of the view to a landmark.</summary>
        static readonly HashSet<string> LowThings = new HashSet<string>
        {
            "bush", "fern", "dune_grass", "flower", "flower_patch", "rock", "snow_rock", "reef_rock", "shell_rock", "stone", "ice", "crystal_cluster",
            "log_small", "hydrant", "lantern", "mushroom", "stump", "log_stack", "crate_space", "bar_stool", "lounge_chair", "potted_plant", "crater",
            "crystal_rock", "meteor_rock", "pumpkin", "fence", "canoe", "lily", "cone", "present", "teddy", "radio", "barrels", "coral"
        };

        /// <summary>The spot is in the open view kept from your seat to the landmark: a corridor that widens with distance.</summary>
        public bool InLandmarkView(Vector3 p)
        {
            if (landmarkDir == Vector3.zero) return false;
            float dx = p.x - cx, dz = p.z - cz;
            float along = dx * landmarkDir.x + dz * landmarkDir.z;
            if (along <= 0f || along > innerR + 70f) return false;
            float across = Mathf.Abs(dx * landmarkDir.z - dz * landmarkDir.x);
            return across < Mathf.Max(3.5f, along * landmarkSpread);
        }

        /// <summary>Where the landmark stands (for tests), or null.</summary>
        public Transform Landmark { get; private set; }

        /// <summary>
        /// A real place's landmark (Landmarks), a little to the right of straight ahead and far enough off to be
        /// seen whole, facing you. Returns its triangles.
        /// </summary>
        int BuildLandmark(Transform root, string kind, WorldColors colors, List<Vector3> placed)
        {
            if (Room == null) return 0;
            Vector3 fwd = Room.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            // A little right of straight ahead, or the nearest direction to that your own furniture does not stand in.
            Vector3 dir = Quaternion.AngleAxis(16f, Vector3.up) * fwd;
            foreach (float deg in new[] { 16f, -16f, 0f, 32f, -32f, 48f, -48f })
            {
                Vector3 d = Quaternion.AngleAxis(deg, Vector3.up) * fwd;
                if (ClearView(d)) { dir = d; break; }
            }
            landmarkDir = dir;
            float dist = innerR + Landmarks.Distance(kind);
            Vector3 c = new Vector3(cx, 0f, cz) + dir * dist;
            // On the lowest ground under it, so no corner hangs in the air on a slope.
            float rad = Landmarks.Radius(kind), low = HeightAt(c.x, c.z);
            // The open view is as wide as the landmark itself (a colosseum is wider than a tower).
            landmarkSpread = Mathf.Clamp(rad * 0.8f / dist, 0.13f, 0.3f);
            landmarkAt = c;
            landmarkFront = Landmarks.Forecourt(kind);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                low = Mathf.Min(low, HeightAt(c.x + Mathf.Cos(a) * rad * 0.7f, c.z + Mathf.Sin(a) * rad * 0.7f));
            }
            c.y = low - 0.5f;
            var mb = new MeshBuilder();
            Landmarks.Build(mb, kind, colors.Ground, new Rng(seed * 61 + 3));
            if (mb.TriangleCount == 0) return 0;
            // It stands out of the haze: half the fog of the things round it (a city's fog swallowed a tower 150 m off).
            if (farMat == null || farMat.shader != World.shader)
            {
                farMat = new Material(World) { name = "World (far landmark)" };
                farMat.SetFloat("_FogScale", 0.5f);
            }
            var go = Util.MeshObject("Landmark " + kind, root, mb.ToMesh("landmark"), farMat);
            go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(-dir, Vector3.up));
            Landmark = go.transform;
            placed.Add(new Vector3(c.x, rad, c.z));
            return mb.TriangleCount;
        }

        int BuildVolcano(Transform root, WorldColors colors, ref int rends)
        {
            if (Room == null) return 0;
            Vector3 fwd = Room.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(-24f, Vector3.up) * fwd;
            const float dist = 95f, radius = 52f, height = 40f, crater = 8f;
            Vector3 c = new Vector3(cx, 0f, cz) + dir * dist;
            c.y = HeightAt(c.x, c.z) - 3f;
            var rng = new Rng(seed * 53 + 9);
            const int n = 40;
            var jit = new float[n];
            var lava = new bool[n];
            for (int i = 0; i < n; i++) { jit[i] = rng.Range(0.86f, 1.12f); lava[i] = rng.Next01() < 0.16f; }
            Color rock = colors.Ground * 0.6f, upper = colors.Ground * 0.4f, glow = new Color(1f, 0.42f, 0.12f, 1f);
            rock.a = upper.a = 1f;
            var mb = new MeshBuilder();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float a0 = i * Mathf.PI * 2f / n, a1 = j * Mathf.PI * 2f / n;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = c + d0 * radius * jit[i], b1 = c + d1 * radius * jit[j];
                Vector3 m0 = c + d0 * radius * 0.42f * jit[i] + Vector3.up * height * 0.6f, m1 = c + d1 * radius * 0.42f * jit[j] + Vector3.up * height * 0.6f;
                Vector3 t0 = c + d0 * crater + Vector3.up * height, t1 = c + d1 * crater + Vector3.up * height;
                mb.Quad(b0, m0, m1, b1, lava[i] ? Color.Lerp(rock, glow, 0.5f) : rock, lava[i] ? 0.55f : 0f);
                mb.Quad(m0, t0, t1, m1, lava[i] ? glow : upper, lava[i] ? 1f : 0f);
                mb.Tri(t0, c + Vector3.up * (height - 5f), t1, glow, 1f);
            }
            int tris = mb.TriangleCount;
            Util.MeshObject("Volcano", root, mb.ToMesh("volcano"), World);
            rends++;
            if (Glow != null)
            {
                var halo = Effects.Halo("Volcano glow", root, Glow, 30f, new Color(1f, 0.42f, 0.12f, 1f) * 1.1f);
                halo.transform.position = c + Vector3.up * (height + 1.5f);
                rends++;
            }
            return tris;
        }

        /// <summary>Animals at their real size, 10 to 30 m out, mostly ahead and to the sides (Wildlife). Returns their triangles.</summary>
        int BuildWildlife(Recipe r, Transform root, List<Vector3> placed, out int count)
        {
            count = 0;
            var kinds = Wildlife.For(r, Herd);
            if (kinds.Length == 0 || Room == null) return 0;
            var rng = new Rng(r.Seed * 389 + 5);
            Vector3 fwd = Room.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float baseAng = Mathf.Atan2(fwd.z, fwd.x);
            int tris = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                string kind = kinds[i];
                float life = LifeSize.Metres(kind);
                var mesh = Maker.BuildMesh(new ActItem { Kind = kind }, WorldColors.For(r), r.Seed + i * 17, out float largest);
                if (mesh.vertexCount == 0) continue;
                Vector3 p = default;
                bool ok = false;
                for (int tries = 0; tries < 12 && !ok; tries++)
                {
                    float dist = innerR + 7f + rng.Range(0f, 16f) + life * 0.5f;
                    float ang = baseAng + rng.Range(-1.2f, 1.2f);
                    p = new Vector3(cx + Mathf.Cos(ang) * dist, 0f, cz + Mathf.Sin(ang) * dist);
                    ok = !TooClose(placed, p, life * 0.5f);
                }
                if (!ok) continue;
                p.y = HeightAt(p.x, p.z) - 0.02f;
                var go = Util.MeshObject("Animal " + kind, root, mesh, World);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f));
                go.transform.localScale = Vector3.one * (life / Mathf.Max(0.001f, largest));
                if (Blob != null)
                {
                    // They wander, so the sun's shadow texture leaves them out: a soft shadow goes with them instead.
                    if (blobQuad == null) blobQuad = Util.Quad(Color.white, "Shadow");
                    var shade = Util.MeshObject("Shadow", go.transform, blobQuad, Blob);
                    float k = life / Mathf.Max(0.001f, largest);
                    shade.transform.localPosition = new Vector3(0f, 0.06f / k, 0f);
                    shade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var mb = mesh.bounds;
                    shade.transform.localScale = new Vector3(mb.size.x * 1.25f, mb.size.z * 1.25f, 1f);
                }
                if (Gait.Rig(mesh, kind)) { float lifeSize = life; go.AddComponent<Gait>().Size = () => lifeSize; }
                var wild = go.AddComponent<Wild>();
                wild.Head = Head;
                wild.Size = life;
                wild.Kind = kind;
                wild.Ground = HeightAt;
                placed.Add(new Vector3(p.x, life * 0.5f, p.z));
                tris += mesh.triangles.Length / 3;
                count++;
            }
            if (count > 0) Debug.Log("[Worldeck] wildlife: " + count + " (" + string.Join(", ", kinds) + ")");
            return tris;
        }

        /// <summary>The world's local (Locals), life size, a little to one side of straight ahead, clear of the scenery.</summary>
        int BuildLocal(Recipe r, Transform root, List<Vector3> placed)
        {
            var def = Locals.For(r);
            if (def == null || Room == null) return 0;
            Mesh mesh; float largest;
            ModelKit.Variant = def.Kind == "person" ? def.Seed : -1;
            try { mesh = Maker.BuildMesh(new ActItem { Kind = def.Kind }, WorldColors.For(r), def.Seed, out largest); }
            finally { ModelKit.Variant = -1; }
            if (mesh.vertexCount == 0) return 0;
            Vector3 fwd = Room.Forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 p = default;
            bool ok = false;
            // First where face and chest both show past your furniture (only a head showed over a bench); then the face.
            for (int pass = 0; pass < 2 && !ok; pass++)
                foreach (float deg in new[] { 30f, -30f, 42f, -42f, 55f, -55f, 20f, -20f, 8f, -8f })
                {
                    Vector3 dir = Quaternion.AngleAxis(deg, Vector3.up) * fwd;
                    for (float extra = 0.7f; extra <= 2.3f && !ok; extra += 0.4f)
                    {
                        p = new Vector3(cx, 0f, cz) + dir * (innerR + extra);
                        ok = !TooClose(placed, p, 0.45f) && SeenFromSeat(p) && (pass == 1 || SeenFromSeat(p, 0.45f, 0.3f));
                    }
                    if (ok) break;
                }
            if (!ok) p = new Vector3(cx, 0f, cz) + Quaternion.AngleAxis(30f, Vector3.up) * fwd * (innerR + 0.7f);
            p.y = HeightAt(p.x, p.z) - 0.01f;
            float height = Locals.Height(def);
            var go = Util.MeshObject("Local " + def.Name, root, mesh, World);
            Vector3 toYou = new Vector3(cx - p.x, 0f, cz - p.z);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(toYou.sqrMagnitude > 1e-4f ? toYou.normalized : -fwd, Vector3.up));
            go.transform.localScale = Vector3.one * (height / Mathf.Max(0.001f, largest));
            var local = go.AddComponent<Local>();
            local.Def = def;
            local.Head = Head;
            local.Height = height;
            placed.Add(new Vector3(p.x, 0.5f, p.z));
            return mesh.triangles.Length / 3;
        }

        /// <summary>
        /// Nothing in the room stands between your seat and this spot at the height of someone's face (a bookshelf
        /// that became a tree trunk would hide them, and they say hello only when looked at).
        /// </summary>
        public bool SeenFromSeat(Vector3 p, float height = 1.05f, float clear = 0.08f)
        {
            if (Room == null) return true;
            Vector3 eye = Room.Head, face = new Vector3(p.x, floorY + height, p.z);
            for (int i = 1; i <= 10; i++)
            {
                Vector3 q = Vector3.Lerp(eye, face, i / 11f);
                foreach (var piece in Room.Pieces)
                    if (!piece.Virtual && piece.Distance(q) < clear) return false;
            }
            return true;
        }

        bool TooClose(List<Vector3> placed, Vector3 p, float rad)
        {
            foreach (var q in placed)
            {
                float dx = q.x - p.x, dz = q.z - p.z, min = (q.y + rad) * 0.9f;
                if (dx * dx + dz * dz < min * min) return true;
            }
            // Never inside or right against the real room.
            float d = Mathf.Sqrt((p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz));
            return d < innerR + rad * 0.5f;
        }

        bool FrontArc(Vector3 p)
        {
            Vector3 to = new Vector3(p.x - cx, 0, p.z - cz).normalized;
            return Vector3.Dot(to, Room.Forward) > 0.82f;
        }

        int SectorOf(Vector3 p)
        {
            float a = Mathf.Atan2(p.z - cz, p.x - cx);
            int s = Mathf.FloorToInt((a + Mathf.PI) / (Mathf.PI * 2f) * Sectors);
            return Mathf.Clamp(s, 0, Sectors - 1);
        }
    }

    /// <summary>A northern-lights ribbon: a tall curtain of additive quads that ripples slowly.</summary>
    public sealed class AuroraRibbon : MonoBehaviour
    {
        Mesh mesh;
        Vector3[] basePos, pos;
        float phase;
        const int Seg = 48;

        public void Build(Vector3 center, float angle, float dist, float height, Material mat, float ph)
        {
            phase = ph;
            mesh = new Mesh { name = "aurora" };
            basePos = new Vector3[(Seg + 1) * 2];
            var cols = new Color[basePos.Length];
            var uvs = new Vector2[basePos.Length];
            var tris = new List<int>();
            Color a = WorldColors.Hex("#5CFFB0", Color.green), b = WorldColors.Hex("#B36BFF", Color.magenta);
            for (int i = 0; i <= Seg; i++)
            {
                float t = i / (float)Seg;
                float ang = angle - 0.7f + t * 1.4f;
                float d = dist + Mathf.Sin(t * 7f + ph) * 8f;
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * d, height, Mathf.Sin(ang) * d);
                basePos[i * 2] = p;
                basePos[i * 2 + 1] = p + Vector3.up * (18f + 8f * Mathf.Sin(t * 5f + ph));
                float fade = Mathf.Sin(t * Mathf.PI);
                cols[i * 2] = a * (0.55f * fade);
                cols[i * 2 + 1] = b * (0.0f);
                cols[i * 2].a = 1f; cols[i * 2 + 1].a = 1f;
                uvs[i * 2] = new Vector2(t, 0); uvs[i * 2 + 1] = new Vector2(t, 1);
                if (i < Seg)
                {
                    int k = i * 2;
                    tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                }
            }
            pos = (Vector3[])basePos.Clone();
            mesh.vertices = pos;
            mesh.colors = cols;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 20f);
            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Tick(float time)
        {
            if (mesh == null) return;
            for (int i = 0; i < basePos.Length; i++)
            {
                float t = (i / 2) / (float)Seg;
                float w = Mathf.Sin(t * 13f + time * 0.17f + phase) * 3f + Mathf.Sin(t * 6f + time * 0.1f) * 5f;
                pos[i] = basePos[i] + new Vector3(w, (i & 1) == 1 ? Mathf.Sin(time * 0.3f + t * 9f) * 2f : 0f, w * 0.5f);
            }
            mesh.vertices = pos;
        }
    }
}

using System;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The player's real furniture as each world's themed object, filling the furniture's box exactly.
    /// The origin is the box's bottom centre, +Y up, +Z the front. Every vertex stays inside
    /// [-x/2, x/2] × [0, y] × [-z/2, z/2], except that <see cref="MayRiseAbove"/> skins may go above y (inside the
    /// footprint). Surface skins have a flat top at exactly y over the whole footprint (the table top is real),
    /// with nothing on it. Door and window skins build only the frame: the outer 15 % of x and the top 15 % of y.
    /// </summary>
    public static class Skins
    {
        const float Tau = Mathf.PI * 2f;

        public static bool Build(MeshBuilder mb, string skin, Vector3 size, WorldColors c, Rng rng)
        {
            if (mb == null) return false;
            string k = Art.Norm(skin);
            if (k == "none") return true;
            if (!Known(k)) return false;
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f) || float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)) return false;
            size = new Vector3(Mathf.Clamp(size.x, 0.02f, 20f), Mathf.Clamp(size.y, 0.02f, 20f), Mathf.Clamp(size.z, 0.02f, 20f));
            rng ??= new Rng(1);
            using (new Art.Ao(mb, Mathf.Clamp(size.y * 0.15f, 0.02f, 0.3f))) Dispatch(mb, k, size, c, rng);
            return true;
        }

        /// <summary>True for the table skins: a flat top at exactly y covering the footprint.</summary>
        public static bool FlatTop(string skin)
        {
            switch (Art.Norm(skin))
            {
                case "fire_ring": case "driftwood_table": case "stone_altar": case "cabin_bench":
                case "console": case "giant_clam": case "bar_table": case "picnic_table":
                    return true;
                default: return false;
            }
        }

        /// <summary>True when the skin may grow above the box top (trunks with canopies, lamps, plants, arches), inside the footprint.</summary>
        public static bool MayRiseAbove(string skin)
        {
            switch (Art.Norm(skin))
            {
                case "pine_trunk": case "palm_trunk": case "snow_pine_trunk": case "oak_trunk": case "kelp_column":
                case "lantern_post": case "tiki_torch": case "brazier": case "oil_lamp": case "light_pylon": case "jelly_lamp": case "street_lamp":
                case "fern": case "palm_bush": case "cactus_small": case "snow_bush": case "hydroponic": case "anemone": case "potted_palm": case "flower_bush":
                case "tree_arch": case "palm_arch": case "stone_arch": case "coral_arch": case "hedge_arch":
                    return true;
                default: return false;
            }
        }

        /// <summary>True for door and window frames, which leave the middle (the opening) empty.</summary>
        public static bool IsFrame(string skin)
        {
            switch (Art.Norm(skin))
            {
                case "tree_arch": case "palm_arch": case "stone_arch": case "cabin_door": case "airlock": case "coral_arch":
                case "club_door": case "hedge_arch": case "frost_window": case "porthole": case "neon_window":
                    return true;
                default: return false;
            }
        }

        static bool Known(string k)
        {
            switch (k)
            {
                case "fire_ring": case "driftwood_table": case "stone_altar": case "cabin_bench": case "console": case "giant_clam": case "bar_table": case "picnic_table":
                case "log_bench": case "lounger": case "sandstone_block": case "snowy_log": case "pilot_seat": case "coral_rock": case "booth": case "mossy_log":
                case "pine_trunk": case "palm_trunk": case "rock_pillar": case "snow_pine_trunk": case "machine_tower": case "kelp_column": case "brick_pillar": case "oak_trunk":
                case "lantern_post": case "tiki_torch": case "brazier": case "oil_lamp": case "light_pylon": case "jelly_lamp": case "street_lamp":
                case "fern": case "palm_bush": case "cactus_small": case "snow_bush": case "hydroponic": case "anemone": case "potted_palm": case "flower_bush":
                case "rock": case "snow_rock": case "crate": case "reef_rock": case "speaker": case "mossy_rock":
                case "tree_arch": case "palm_arch": case "stone_arch": case "cabin_door": case "airlock": case "coral_arch": case "club_door": case "hedge_arch":
                case "frost_window": case "porthole": case "neon_window":
                    return true;
                default: return false;
            }
        }

        static void Dispatch(MeshBuilder mb, string k, Vector3 s, WorldColors c, Rng rng)
        {
            switch (k)
            {
                // surfaces
                case "fire_ring": FireRing(mb, s, c, rng); break;
                case "driftwood_table": DriftwoodTable(mb, s, c, rng); break;
                case "stone_altar": StoneAltar(mb, s, c, rng); break;
                case "cabin_bench": CabinBench(mb, s, c, rng); break;
                case "console": ConsoleDesk(mb, s, c, rng); break;
                case "giant_clam": GiantClam(mb, s, c, rng); break;
                case "bar_table": BarTable(mb, s, c, rng); break;
                case "picnic_table": PicnicTable(mb, s, c, rng); break;
                // seats
                case "log_bench": LogSeat(mb, s, c, rng, 0); break;
                case "snowy_log": LogSeat(mb, s, c, rng, 1); break;
                case "mossy_log": LogSeat(mb, s, c, rng, 2); break;
                case "lounger": Lounger(mb, s, c, rng); break;
                case "sandstone_block": SandstoneBlock(mb, s, c, rng); break;
                case "pilot_seat": PilotSeat(mb, s, c, rng); break;
                case "coral_rock": CoralRock(mb, s, c, rng); break;
                case "booth": Booth(mb, s, c, rng); break;
                // tall
                case "pine_trunk": PineTrunk(mb, s, c, rng, false); break;
                case "snow_pine_trunk": PineTrunk(mb, s, c, rng, true); break;
                case "palm_trunk": PalmTrunk(mb, s, c, rng); break;
                case "rock_pillar": RockPillar(mb, s, c, rng); break;
                case "machine_tower": MachineTower(mb, s, c, rng); break;
                case "kelp_column": KelpColumn(mb, s, c, rng); break;
                case "brick_pillar": BrickPillar(mb, s, c, rng); break;
                case "oak_trunk": OakTrunk(mb, s, c, rng); break;
                // lights
                case "lantern_post": LanternPost(mb, s, c, rng); break;
                case "tiki_torch": TikiTorch(mb, s, c, rng); break;
                case "brazier": Brazier(mb, s, c, rng); break;
                case "oil_lamp": OilLamp(mb, s, c, rng); break;
                case "light_pylon": LightPylon(mb, s, c, rng); break;
                case "jelly_lamp": JellyLamp(mb, s, c, rng); break;
                case "street_lamp": StreetLamp(mb, s, c, rng); break;
                // plants
                case "fern": FernSkin(mb, s, c, rng); break;
                case "palm_bush": PalmBush(mb, s, c, rng); break;
                case "cactus_small": CactusSmall(mb, s, c, rng); break;
                case "snow_bush": SnowBush(mb, s, c, rng); break;
                case "hydroponic": Hydroponic(mb, s, c, rng); break;
                case "anemone": Anemone(mb, s, c, rng); break;
                case "potted_palm": PottedPalm(mb, s, c, rng); break;
                case "flower_bush": FlowerBush(mb, s, c, rng); break;
                // clutter
                case "rock": Boulders(mb, s, c, rng, Art.Stone(c), null); break;
                case "snow_rock": Boulders(mb, s, c, rng, Art.Stone(c), Art.Snow(c)); break;
                case "mossy_rock": Boulders(mb, s, c, rng, Art.Stone(c), Art.Moss(c)); break;
                case "reef_rock": ReefRock(mb, s, c, rng); break;
                case "crate": Crates(mb, s, c, rng); break;
                case "speaker": Speaker(mb, s, c, rng); break;
                // doors and windows
                case "tree_arch": TreeArch(mb, s, c, rng); break;
                case "palm_arch": PalmArch(mb, s, c, rng); break;
                case "stone_arch": StoneArch(mb, s, c, rng); break;
                case "cabin_door": CabinDoor(mb, s, c, rng, false); break;
                case "frost_window": CabinDoor(mb, s, c, rng, true); break;
                case "airlock": Airlock(mb, s, c, rng); break;
                case "porthole": Porthole(mb, s, c, rng); break;
                case "coral_arch": CoralArch(mb, s, c, rng); break;
                case "club_door": ClubDoor(mb, s, c, rng, false); break;
                case "neon_window": ClubDoor(mb, s, c, rng, true); break;
                case "hedge_arch": HedgeArch(mb, s, c, rng); break;
            }
        }

        // ---------- helpers ----------

        static void Dims(Vector3 s, out float X, out float Y, out float Z, out float hx, out float hz, out float m)
        {
            X = s.x; Y = s.y; Z = s.z; hx = X * 0.5f; hz = Z * 0.5f; m = Mathf.Min(X, Z);
        }

        /// <summary>How many repeated parts of about <paramref name="unit"/> fit along <paramref name="len"/>.</summary>
        static int Count(float len, float unit, int max) => Mathf.Clamp(Mathf.RoundToInt(len / unit), 1, max);

        struct Cell { public float cx, cz, ex, ez; }

        /// <summary>Splits the footprint along its long side into cells no longer than <paramref name="ratio"/> × their width.</summary>
        static Cell[] Cells(Vector3 s, float ratio, int max)
        {
            int nx = 1, nz = 1;
            if (s.x >= s.z) nx = Mathf.Clamp(Mathf.CeilToInt(s.x / (s.z * ratio) - 1e-4f), 1, max);
            else nz = Mathf.Clamp(Mathf.CeilToInt(s.z / (s.x * ratio) - 1e-4f), 1, max);
            var cells = new Cell[nx * nz];
            float ex = s.x / (2f * nx), ez = s.z / (2f * nz);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    cells[i * nz + j] = new Cell { cx = -s.x * 0.5f + ex * (2 * i + 1), cz = -s.z * 0.5f + ez * (2 * j + 1), ex = ex, ez = ez };
            return cells;
        }

        /// <summary>
        /// Flagstones (or panels) across a table top at exactly <paramref name="y"/>: a grid of tiles about
        /// <paramref name="tile"/> m, each a little different in tone, staggered by row, with darker joints 3 mm
        /// below. The tops are the surfaces people look at most, so they get the detail (Nov 11 captures).
        /// </summary>
        static void TileTop(MeshBuilder mb, float hx, float hz, float y, float tile, Color stone, Color joint, Rng rng, float vary = 0.12f)
        {
            Art.RectY(mb, y - 0.003f, -hx, hx, -hz, hz, 1f, joint);
            int nz = Mathf.Clamp(Mathf.RoundToInt(2f * hz / tile), 1, 12);
            float gap = Mathf.Min(0.006f, tile * 0.05f);
            for (int j = 0; j < nz; j++)
            {
                float z0 = -hz + 2f * hz * j / nz, z1 = -hz + 2f * hz * (j + 1) / nz;
                float w = tile * rng.Range(0.8f, 1.3f);
                float x = -hx - ((j & 1) == 1 ? w * 0.5f : 0f);
                while (x < hx)
                {
                    float x0 = Mathf.Max(-hx, x), x1 = Mathf.Min(hx, x + w);
                    if (x1 - x0 > gap * 2f)
                        Art.RectY(mb, y, x0 + (x0 > -hx ? gap : 0f), x1 - (x1 < hx ? gap : 0f), z0 + (j > 0 ? gap : 0f), z1 - (j < nz - 1 ? gap : 0f), 1f,
                                  WorldColors.Jitter(stone, rng, vary));
                    x += w;
                    w = tile * rng.Range(0.8f, 1.3f);
                }
            }
        }

        static void Planks(MeshBuilder mb, float hx, float hz, float y0, float y1, int n, Color wood, Rng rng)
        {
            for (int i = 0; i < n; i++)
            {
                float z0 = -hz + 2f * hz * i / n, z1 = i == n - 1 ? hz : -hz + 2f * hz * (i + 1) / n;
                Art.BoxMM(mb, new Vector3(-hx, y0, z0), new Vector3(hx, y1, z1), Art.J(wood, rng));
            }
        }

        /// <summary>Legs in two rows (front and back), 2–4 per row by length, their outer faces just inside the footprint.</summary>
        static void Legs(MeshBuilder mb, float hx, float hz, float h, float r, Color col, bool round, Rng rng, int sides = 6)
        {
            int nx = Mathf.Clamp(Mathf.CeilToInt(hx * 2f / 1.4f) + 1, 2, 4);
            float lx = hx - r * 1.2f, lz = hz - r * 1.2f;
            for (int i = 0; i < nx; i++)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float x = Mathf.Lerp(-lx, lx, i / (nx - 1f)), z = sz * lz;
                    Color lc = Art.J(col, rng);
                    if (round) Art.Column(mb, x, z, r, r, 0f, h, sides, 1f, 0.9f, lc, false);
                    else Art.BoxMM(mb, new Vector3(x - r, 0f, z - r), new Vector3(x + r, h, z + r), lc);
                }
        }

        // ---------- surfaces: a flat top at exactly y over the footprint, supports below ----------

        static void FireRing(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.05f + 0.05f * m, 0.3f * Y);
            Color stone = Art.Stone(c), slab = Art.J(Art.Lighten(Art.Mix(stone, c.GroundAccent, 0.3f), 0.12f), rng);
            Color dark = Art.Darken(stone, 0.3f), ember = Art.Mix(c.Emissive, new Color(0.55f, 0.12f, 0.04f), 0.55f);
            float rb = Mathf.Min(0.2f, 0.24f * m), depth = Mathf.Min(0.06f, 0.8f * t);
            // The slab top is the footprint minus the fire bowl, all at y: 16 rays (12 even + the 4 corners).
            var ang = new float[16];
            for (int i = 0; i < 12; i++) ang[i] = i * Tau / 12f;
            float ca = Mathf.Atan2(hz, hx);
            ang[12] = ca; ang[13] = Mathf.PI - ca; ang[14] = Mathf.PI + ca; ang[15] = Tau - ca;
            Array.Sort(ang);
            var outer = new Vector3[16];
            var inner = new Vector3[16];
            for (int i = 0; i < 16; i++)
            {
                float cs = Mathf.Cos(ang[i]), sn = Mathf.Sin(ang[i]);
                float tx = Mathf.Abs(cs) > 1e-6f ? hx / Mathf.Abs(cs) : float.MaxValue, tz = Mathf.Abs(sn) > 1e-6f ? hz / Mathf.Abs(sn) : float.MaxValue;
                float tr = Mathf.Min(tx, tz);
                outer[i] = new Vector3(Mathf.Clamp(cs * tr, -hx, hx), Y, Mathf.Clamp(sn * tr, -hz, hz));
                inner[i] = new Vector3(cs * rb, Y, sn * rb);
            }
            Vector3 bed = new Vector3(0f, Y - depth, 0f);
            for (int i = 0; i < 16; i++)
            {
                int j = (i + 1) % 16;
                // Each wedge its own tone: the slab reads as fitted stones around the fire, not one flat plate.
                Art.QuadF(mb, inner[i], outer[i], outer[j], inner[j], Vector3.up, WorldColors.Jitter(slab, rng, 0.14f));
                Vector3 a = inner[i], b = inner[j], a2 = a + Vector3.down * depth, b2 = b + Vector3.down * depth;
                Art.QuadF(mb, a, b, b2, a2, new Vector3(-(a.x + b.x), 0f, -(a.z + b.z)), dark);
                Art.TriF(mb, bed, a2, b2, Vector3.up, ember, 0.35f);
            }
            Art.Walls(mb, -hx, hx, -hz, hz, Y - t, Y, Art.Darken(slab, 0.12f));
            Color coal = Art.Mix(ember, Color.black, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float a = rng.Range(0f, Tau), d = rb * 0.35f, q = rb * 0.28f;
                Vector3 p = new Vector3(Mathf.Cos(a) * d, Y - depth, Mathf.Sin(a) * d);
                Art.IcoIn(mb, p - new Vector3(q, 0f, q), p + new Vector3(q, depth * 0.7f, q), coal, rng, 0.2f, 0.3f, 0.6f);
            }
            float under = Y - t;
            if (under < 0.02f) return;
            float ins = Mathf.Min(0.05f, 0.08f * m);
            Art.BoxMM(mb, new Vector3(-hx + ins, 0f, -hz + ins), new Vector3(hx - ins, under, hz - ins), dark);
            // a ring of boulders around the base
            float bh = Mathf.Min(0.75f * under, 0.45f), bd = Mathf.Min(0.35f * m, 0.3f);
            if (bh < 0.04f) return;
            int nx = Count(X, 0.35f, 5), nz = Count(Z, 0.35f, 3);
            for (int i = 0; i < nx; i++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float x0 = -hx + X * i / nx, x1 = -hx + X * (i + 1) / nx;
                    float z0 = sd > 0 ? hz - bd : -hz, z1 = sd > 0 ? hz : -hz + bd;
                    Art.IcoIn(mb, new Vector3(x0, 0f, z0), new Vector3(x1, bh * rng.Range(0.7f, 1f), z1), Art.J(stone, rng), rng, 0.18f, 0.4f);
                }
            for (int i = 0; i < nz; i++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float z0 = -hz + Z * i / nz, z1 = -hz + Z * (i + 1) / nz;
                    float x0 = sd > 0 ? hx - bd : -hx, x1 = sd > 0 ? hx : -hx + bd;
                    Art.IcoIn(mb, new Vector3(x0, 0f, z0), new Vector3(x1, bh * rng.Range(0.7f, 1f), z1), Art.J(stone, rng), rng, 0.18f, 0.4f);
                }
        }

        static void DriftwoodTable(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out _, out float Y, out float Z, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.035f + 0.03f * m, 0.3f * Y);
            Color wood = Art.Lighten(Art.Dull(c.PropAccent, 0.35f), 0.3f), legC = Art.Darken(wood, 0.15f);
            Planks(mb, hx, hz, Y - t, Y, Count(Z, 0.14f, 10), wood, rng);
            float legH = Y - t, r = Mathf.Min(0.03f + 0.025f * m, 0.2f * m);
            if (legH < 0.01f) return;
            Legs(mb, hx, hz, legH, r, legC, true, rng);
            float rh = Mathf.Min(0.05f, 0.3f * legH), lz = hz - r * 1.2f, lx = hx - r * 1.2f;
            for (int sd = -1; sd <= 1; sd += 2)
                Art.BoxMM(mb, new Vector3(-lx, legH - rh, sd * lz - r * 0.6f), new Vector3(lx, legH, sd * lz + r * 0.6f), Art.J(legC, rng));
            if (legH > 0.4f)
                for (int sd = -1; sd <= 1; sd += 2)
                    Art.BoxMM(mb, new Vector3(sd * lx - r * 0.5f, legH * 0.3f, -lz), new Vector3(sd * lx + r * 0.5f, legH * 0.3f + rh, lz), Art.J(legC, rng));
        }

        static void StoneAltar(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.05f + 0.05f * m, 0.3f * Y), pl = Mathf.Min(0.1f, 0.15f * Y);
            Color sand = Art.Mix(c.Ground, c.PropAccent, 0.35f), top = Art.Lighten(c.GroundAccent, 0.1f), glyph = Art.Vivid(c.Emissive, 0.5f, 1f);
            Art.BoxMM(mb, new Vector3(-hx, Y - t, -hz), new Vector3(hx, Y - 0.004f, hz), Art.Darken(sand, 0.05f), Art.Darken(top, 0.35f));
            TileTop(mb, hx, hz, Y, 0.18f, top, Art.Darken(top, 0.35f), rng);
            float by0 = pl, by1 = Y - t;
            if (by1 - by0 < 0.03f)
            {
                if (Y - t > 0.005f) Art.BoxMM(mb, new Vector3(-hx * 0.9f, 0f, -hz * 0.9f), new Vector3(hx * 0.9f, Y - t, hz * 0.9f), Art.Darken(sand, 0.15f));
                return;
            }
            Art.BoxMM(mb, new Vector3(-hx, 0f, -hz), new Vector3(hx, pl, hz), Art.Darken(sand, 0.15f));
            float ins = Mathf.Min(0.06f, 0.1f * m), bw = X - 2f * ins;
            int nx = Count(bw, 0.4f, 5), ny = Count(by1 - by0, 0.3f, 5);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float e = rng.Range(0f, 0.6f) * ins;
                    Art.BoxMM(mb, new Vector3(-hx + ins + bw * i / nx, by0 + (by1 - by0) * j / ny, -hz + ins + e),
                              new Vector3(-hx + ins + bw * (i + 1) / nx, by0 + (by1 - by0) * (j + 1) / ny, hz - ins - e), Art.J(sand, rng));
                }
            float gr = Mathf.Min(0.3f * (by1 - by0), Mathf.Min(0.18f * X, 0.25f));
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 gc = new Vector3(0f, (by0 + by1) * 0.5f, sd * (hz - ins + 0.004f)), n = new Vector3(0f, 0f, sd);
                Art.Ring(mb, gc, n, gr * 0.6f, gr, 12, glyph, 0.8f);
                Art.Disc(mb, gc, n, gr * 0.35f, 8, glyph, 0.9f);
            }
        }

        static void CabinBench(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.04f + 0.035f * m, 0.3f * Y);
            Color wood = c.PropAccent, top = Art.Lighten(c.PropAccent, 0.12f);
            Planks(mb, hx, hz, Y - t, Y, Count(Z, 0.15f, 10), top, rng);
            float legH = Y - t;
            if (legH < 0.01f) return;
            float w = Mathf.Min(0.035f + 0.03f * m, 0.18f * m);
            Legs(mb, hx, hz, legH, w, Art.Darken(wood, 0.1f), false, rng);
            float ah = Mathf.Min(0.08f, 0.3f * legH), lx = hx - w * 0.2f;
            Art.BoxMM(mb, new Vector3(-lx, legH - ah, hz - w * 2.2f), new Vector3(lx, legH, hz - w * 0.2f), Art.J(wood, rng));
            Art.BoxMM(mb, new Vector3(-lx, legH - ah, -hz + w * 0.2f), new Vector3(lx, legH, -hz + w * 2.2f), Art.J(wood, rng));
            if (legH <= 0.3f) return;
            float sy = Mathf.Max(0.06f, 0.18f * legH), st = Mathf.Min(0.03f, 0.1f * legH);
            Art.BoxMM(mb, new Vector3(-lx, sy, -hz + w * 0.2f), new Vector3(lx, sy + st, hz - w * 0.2f), Art.J(wood, rng), top);
            float sw = Mathf.Min(0.3f, 0.3f * X), sh = Mathf.Min(0.12f, 0.25f * legH);
            Art.IcoIn(mb, new Vector3(lx - sw, sy + st, -hz + w), new Vector3(lx, sy + st + sh, hz - w), Art.J(Art.Snow(c), rng), rng, 0.15f, 0.4f);
        }

        static void ConsoleDesk(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out _);
            Color body = Art.J(c.Prop, rng), topC = c.GroundAccent, pad = Art.Mix(c.GroundAccent, c.Emissive, 0.35f), dark = Art.Darken(c.Prop, 0.45f);
            int nx = Mathf.Clamp(Mathf.RoundToInt(X / 0.2f), 3, 8), nz = Mathf.Clamp(Mathf.RoundToInt(Z / 0.2f), 3, 6);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x0 = -hx + X * i / nx, x1 = i == nx - 1 ? hx : -hx + X * (i + 1) / nx;
                    float z0 = -hz + Z * j / nz, z1 = j == nz - 1 ? hz : -hz + Z * (j + 1) / nz;
                    bool centre = Mathf.Abs(i - (nx - 1) * 0.5f) <= Mathf.Max(0.5f, (nx - 1) * 0.2f) && Mathf.Abs(j - (nz - 1) * 0.5f) <= Mathf.Max(0.5f, (nz - 1) * 0.2f);
                    Color col; float em = 0f;
                    if (centre) { col = pad; em = 0.4f; }
                    else if (j == nz - 1 && rng.Next01() < 0.55f)
                    {
                        int pick = rng.Range(0, 3);
                        col = pick == 0 ? c.Emissive : pick == 1 ? c.PropAccent : c.Neon;
                        em = 0.9f;
                    }
                    else col = Art.J(topC, rng);
                    Art.RectY(mb, Y, x0, x1, z0, z1, 1f, col, em);
                }
            float lip = Mathf.Min(0.03f, 0.2f * Y), fz = hz - Mathf.Min(0.012f, 0.1f * Z);
            Art.RectZ(mb, hz, -hx, hx, Y - lip, Y, 1f, dark);
            Art.RectY(mb, Y - lip, -hx, hx, fz, hz, -1f, dark);
            Art.RectZ(mb, fz, -hx, hx, 0f, Y - lip, 1f, body);
            Art.RectZ(mb, -hz, -hx, hx, 0f, Y, -1f, body);
            Art.RectX(mb, hx, -hz, hz, 0f, Y, 1f, body);
            Art.RectX(mb, -hx, -hz, hz, 0f, Y, -1f, body);
            float zd = fz + Mathf.Min(0.004f, (hz - fz) * 0.5f), fh = Y - lip;
            for (int k = 0; k < 2; k++)
            {
                float y = fh * (0.62f + 0.14f * k);
                Art.RectZ(mb, zd, -hx * 0.8f, hx * 0.8f, y, y + Mathf.Min(0.02f, 0.04f * fh), 1f, c.Emissive, 0.9f);
            }
            Art.RectZ(mb, zd, -hx * 0.6f, hx * 0.6f, fh * 0.12f, fh * 0.42f, 1f, dark);
        }

        static void GiantClam(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out _, out float Y, out _, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.04f + 0.03f * m, 0.3f * Y);
            Color shell = Art.J(c.PropAccent, rng), rib = Art.Lighten(shell, 0.15f), nacre = Art.Mix(c.PropAccent, c.Emissive, 0.35f);
            Art.BoxMM(mb, new Vector3(-hx, Y - t, -hz), new Vector3(hx, Y - 0.004f, hz), Art.Mix(nacre, shell, 0.4f), Art.Darken(nacre, 0.3f), 0.1f);
            TileTop(mb, hx, hz, Y, 0.16f, nacre, Art.Darken(nacre, 0.3f), rng, 0.16f);
            float bh = Y - t;
            if (bh < 0.01f) return;
            const int S = 20;
            float[] ty = { 0f, 0.35f, 0.75f, 1f }, sr = { 0.45f, 0.75f, 0.93f, 1f };
            var ring = new Vector3[4 * S];
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < S; k++)
                {
                    float a = k * Tau / S, f = sr[i] * ((k & 1) == 1 ? 1f : 0.88f);
                    ring[i * S + k] = new Vector3(Mathf.Cos(a) * hx * f, ty[i] * bh, Mathf.Sin(a) * hz * f);
                }
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < S; k++)
                {
                    int k1 = (k + 1) % S;
                    Vector3 a = ring[i * S + k], b = ring[(i + 1) * S + k], cc = ring[(i + 1) * S + k1], d = ring[i * S + k1];
                    Vector3 f = (a + b + cc + d) * 0.25f;
                    f.y = 0f;
                    Art.QuadF(mb, a, b, cc, d, f, (k & 1) == 1 ? rib : shell);
                }
            float pr = Mathf.Min(0.06f, Mathf.Min(0.12f * m, 0.4f * bh));
            Color pearl = Art.Mix(new Color(1f, 0.97f, 0.98f), c.Emissive, 0.2f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 p = new Vector3(sd * (hx - pr * 1.1f), 0f, hz - pr * 1.1f);
                Art.IcoIn(mb, p - new Vector3(pr, 0f, pr), p + new Vector3(pr, 2f * pr, pr), pearl, rng, 0.05f, 0f, 0.4f);
            }
        }

        static void BarTable(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.035f + 0.03f * m, 0.3f * Y);
            Color wood = Art.Darken(c.PropAccent, 0.35f), brass = Art.Lighten(c.PropAccent, 0.15f), topC = Art.Mix(wood, c.GroundAccent, 0.3f);
            Art.BoxMM(mb, new Vector3(-hx, Y - t, -hz), new Vector3(hx, Y - 0.012f, hz), brass, Art.Darken(topC, 0.3f));
            Planks(mb, hx, hz, Y - 0.012f, Y, Count(Z, 0.11f, 12), topC, rng);
            float legH = Y - t;
            if (legH < 0.01f) return;
            float ns = Mathf.Min(0.012f, 0.2f * legH), ni = Mathf.Min(0.02f, 0.1f * m);
            Art.Walls(mb, -hx + ni, hx - ni, -hz + ni, hz - ni, legH - ns, legH, c.Neon, 0.9f);
            if (X * Z < 0.6f)
            {
                float fh = Mathf.Min(0.04f, 0.2f * legH), pr = Mathf.Min(0.06f + 0.04f * m, 0.3f * m);
                Art.Column(mb, 0f, 0f, hx * 0.8f, hz * 0.8f, 0f, fh, 8, 1f, 0.85f, brass);
                Art.Column(mb, 0f, 0f, pr, pr, 0f, legH, 8, 1f, 0.75f, wood, false);
                Art.Column(mb, 0f, 0f, pr * 1.4f, pr * 1.4f, legH * 0.45f, Mathf.Min(0.03f, 0.1f * legH), 8, 1f, 1f, brass);
                return;
            }
            float r = Mathf.Min(0.025f + 0.015f * m, 0.15f * m);
            Legs(mb, hx, hz, legH, r, wood, true, rng, 8);
            float lx = hx - r * 1.2f, lz = hz - r * 1.2f, ry = legH * 0.25f, rt = Mathf.Min(0.02f, r);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Art.BoxMM(mb, new Vector3(-lx, ry, sd * lz - rt * 0.5f), new Vector3(lx, ry + rt, sd * lz + rt * 0.5f), brass);
                Art.BoxMM(mb, new Vector3(sd * lx - rt * 0.5f, ry, -lz), new Vector3(sd * lx + rt * 0.5f, ry + rt, lz), brass);
            }
        }

        static void PicnicTable(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out float m);
            float t = Mathf.Min(0.04f + 0.03f * m, 0.3f * Y);
            Color wood = c.PropAccent, red = Art.Vivid(Art.Mix(new Color(0.85f, 0.2f, 0.22f), c.Emissive, 0.1f), 0.6f, 0.8f), white = new Color(0.97f, 0.95f, 0.9f);
            int rows = Count(Z, 0.15f, 8), cols = Mathf.Clamp(Mathf.RoundToInt(X / 0.1f), 4, 14);
            var rowC = new Color[rows];
            for (int j = 0; j < rows; j++) rowC[j] = Art.J(wood, rng);
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    float x0 = -hx + X * i / cols, x1 = i == cols - 1 ? hx : -hx + X * (i + 1) / cols;
                    float z0 = -hz + Z * j / rows, z1 = j == rows - 1 ? hz : -hz + Z * (j + 1) / rows;
                    bool cloth = Mathf.Abs((x0 + x1) * 0.5f) < 0.32f * X;
                    Art.RectY(mb, Y, x0, x1, z0, z1, 1f, cloth ? (((i + j) & 1) == 0 ? red : white) : rowC[j]);
                }
            Art.Walls(mb, -hx, hx, -hz, hz, Y - t, Y, Art.Darken(wood, 0.1f), 0f, true);
            float legTop = Y - t;
            if (legTop < 0.02f) return;
            // A-frame legs at each end
            float lw = Mathf.Min(0.05f, 0.1f * X), bd = Mathf.Min(0.07f, 0.25f * Z), zt = 0.15f * hz;
            float xe = hx - lw * 0.5f - Mathf.Min(0.1f * X, 0.15f);
            for (int ex = -1; ex <= 1; ex += 2)
                for (int ez = -1; ez <= 1; ez += 2)
                {
                    float x0 = ex * xe - lw * 0.5f, x1 = ex * xe + lw * 0.5f;
                    float zb0 = ez * (hz - bd), zb1 = ez * hz, zt0 = ez * zt, zt1 = ez * (zt + bd);
                    var p = new[]
                    {
                        new Vector3(x0, 0f, zb0), new Vector3(x1, 0f, zb0), new Vector3(x1, 0f, zb1), new Vector3(x0, 0f, zb1),
                        new Vector3(x0, legTop, zt0), new Vector3(x1, legTop, zt0), new Vector3(x1, legTop, zt1), new Vector3(x0, legTop, zt1)
                    };
                    Art.Hexa(mb, p, Art.J(wood, rng));
                }
            float by = legTop * 0.45f, bt = Mathf.Min(0.04f, 0.1f * legTop), zb = Mathf.Lerp(hz - bd * 0.5f, zt + bd * 0.5f, 0.45f);
            Color brace = Art.Darken(wood, 0.1f);
            for (int ex = -1; ex <= 1; ex += 2)
                Art.BoxMM(mb, new Vector3(ex * xe - lw * 0.5f, by, -zb), new Vector3(ex * xe + lw * 0.5f, by + bt, zb), brace);
            float sz = Mathf.Min(0.02f, 0.2f * hz);
            Art.BoxMM(mb, new Vector3(-xe, by + bt * 0.2f, -sz), new Vector3(xe, by + bt * 0.8f, sz), brace);
        }

        // ---------- seats: something to sit on, its surface at the box top ----------

        /// <summary>A log split along its length: round underneath, a flat seat at <paramref name="y1"/>.</summary>
        static void HalfLog(MeshBuilder mb, float x0, float x1, float cz, float y1, float r, int sides, Color bark, Color cut, Color top)
        {
            var p = new Vector3[sides + 1];
            for (int k = 0; k <= sides; k++)
            {
                float th = Mathf.PI * k / sides;
                p[k] = new Vector3(0f, Mathf.Min(y1, y1 - Mathf.Sin(th) * r), cz + Mathf.Cos(th) * r);
            }
            Vector3 X0 = Vector3.right * x0, X1 = Vector3.right * x1;
            for (int k = 0; k < sides; k++)
            {
                float thm = Mathf.PI * (k + 0.5f) / sides;
                var f = new Vector3(0f, -Mathf.Sin(thm), Mathf.Cos(thm));
                Art.QuadF(mb, p[k] + X0, p[k] + X1, p[k + 1] + X1, p[k + 1] + X0, f, (k & 1) == 0 ? bark : Art.Darken(bark, 0.06f));
                Art.TriF(mb, new Vector3(x0, y1, cz), p[k] + X0, p[k + 1] + X0, Vector3.left, cut);
                Art.TriF(mb, new Vector3(x1, y1, cz), p[k] + X1, p[k + 1] + X1, Vector3.right, cut);
            }
            Art.RectY(mb, y1, x0, x1, cz - r, cz + r, 1f, top);
        }

        static void Mushroom(MeshBuilder mb, float x, float z, float h, Color cap, Color stem)
        {
            Art.Column(mb, x, z, h * 0.18f, h * 0.18f, 0f, h * 0.7f, 5, 1f, 0.8f, stem, false);
            Art.Column(mb, x, z, h * 0.45f, h * 0.45f, h * 0.6f, h * 0.4f, 6, 1f, 0f, cap, false, 0.15f, 0f, 0f, 0f, null, true);
        }

        /// <summary>log_bench (0), snowy_log (1), mossy_log (2): a split log on stumps, or a log rack for tall, thin furniture.</summary>
        static void LogSeat(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng, int deco)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out _);
            Color bark = Art.J(Art.Darken(c.PropAccent, 0.05f), rng), cut = Art.Lighten(Art.Mix(c.PropAccent, new Color(0.9f, 0.75f, 0.5f), 0.5f), 0.15f);
            Color snow = Art.Snow(c), moss = Art.Moss(c);
            Color top = deco == 1 ? snow : deco == 2 ? moss : cut;
            if (Y <= 1.3f * Z)
            {
                float r = Mathf.Min(hz, 0.45f * Y);
                HalfLog(mb, -hx, hx, 0f, Y, r, Mathf.Clamp(Mathf.RoundToInt(r * 16f), 4, 8), bark, cut, top);
                float legH = Y - r;
                if (legH < 0.03f) return;
                int ns = Mathf.Clamp(Mathf.CeilToInt(X) + 1, 2, 4);
                float rs = Mathf.Min(0.8f * r, Mathf.Min(0.18f * X, hz));
                for (int i = 0; i < ns; i++)
                {
                    float x = Mathf.Lerp(-hx + rs, hx - rs, i / (ns - 1f));
                    Art.Column(mb, x, 0f, rs, rs, 0f, legH + 0.3f * r, 7, 1f, 0.85f, Art.J(bark, rng), true, 0f, 0f, 0f, rng.Range(0f, 1f), cut);
                    float dh = Mathf.Min(0.12f, 0.35f * legH);
                    if (deco == 1)
                        Art.IcoIn(mb, new Vector3(Mathf.Max(-hx, x - rs * 1.8f), 0f, -hz), new Vector3(Mathf.Min(hx, x + rs * 1.8f), dh, hz), Art.J(snow, rng), rng, 0.15f, 0.45f);
                    else if (deco == 2 && i < 2)
                    {
                        float h = Mathf.Min(0.12f, Mathf.Min(0.5f * legH, 0.8f * hz));
                        float mx = Mathf.Clamp(x + (i == 0 ? 1f : -1f) * (rs + 0.5f * h), -hx + 0.45f * h, hx - 0.45f * h);
                        Mushroom(mb, mx, hz - 0.5f * h, h, Art.Mix(new Color(0.85f, 0.3f, 0.25f), c.PropAccent, 0.2f), new Color(0.95f, 0.9f, 0.8f));
                    }
                }
                return;
            }
            // a log rack: two posts and logs stacked between them
            float pw = Mathf.Min(0.08f, 0.15f * X);
            Art.BoxMM(mb, new Vector3(-hx, 0f, -hz), new Vector3(-hx + pw, Y, hz), bark, cut);
            Art.BoxMM(mb, new Vector3(hx - pw, 0f, -hz), new Vector3(hx, Y, hz), bark, cut);
            float d0 = Mathf.Min(Z, 0.4f);
            int n = Mathf.Clamp(Mathf.FloorToInt(Y / d0), 1, 8);
            float pitch = Y / n, lr = Mathf.Min(hz, pitch * 0.5f);
            for (int i = 0; i < n; i++)
                mb.Log(new Vector3(0f, Y - lr - i * pitch, 0f), X - pw, lr, 7, Art.J(bark, rng), cut);
            if (deco != 0)
                Art.IcoIn(mb, new Vector3(-hx + pw, Y - 0.5f * lr, -lr * 0.9f), new Vector3(hx - pw, Y, lr * 0.9f), Art.J(top, rng), rng, 0.12f, 0.4f);
        }

        static void Lounger(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out float m);
            float ct = Mathf.Min(0.08f + 0.04f * m, 0.35f * Y), legH = Y - ct, e = Mathf.Min(0.01f, 0.05f * m);
            Color frame = Art.J(c.PropAccent, rng), s1 = Art.Vivid(c.Prop, 0.45f, 0.6f), s2 = new Color(0.98f, 0.93f, 0.85f);
            int ns = Mathf.Clamp(Mathf.RoundToInt(X / 0.12f), 3, 16);
            for (int i = 0; i < ns; i++)
            {
                float x0 = -hx + e + (X - 2f * e) * i / ns, x1 = i == ns - 1 ? hx - e : -hx + e + (X - 2f * e) * (i + 1) / ns;
                Art.RectY(mb, Y, x0, x1, -hz + e, hz - e, 1f, (i & 1) == 0 ? s1 : s2);
            }
            Art.Walls(mb, -hx + e, hx - e, -hz + e, hz - e, Mathf.Max(0f, legH), Y, Art.Darken(s2, 0.08f), 0f, legH > 0.01f);
            if (legH < 0.02f) return;
            float fw = Mathf.Min(0.04f, 0.12f * m), rh = Mathf.Min(0.05f, 0.5f * legH);
            Art.BoxMM(mb, new Vector3(-hx, legH - rh, hz - fw), new Vector3(hx, legH, hz), frame);
            Art.BoxMM(mb, new Vector3(-hx, legH - rh, -hz), new Vector3(hx, legH, -hz + fw), frame);
            Art.BoxMM(mb, new Vector3(-hx, legH - rh, -hz + fw), new Vector3(-hx + fw, legH, hz - fw), frame);
            Art.BoxMM(mb, new Vector3(hx - fw, legH - rh, -hz + fw), new Vector3(hx, legH, hz - fw), frame);
            if (legH - rh < 0.005f) return;
            int nl = Mathf.Clamp(Mathf.CeilToInt(X / 0.9f) + 1, 2, 4);
            for (int i = 0; i < nl; i++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float x = Mathf.Lerp(-hx + fw * 0.5f, hx - fw * 0.5f, i / (nl - 1f));
                    Art.BoxMM(mb, new Vector3(x - fw * 0.5f, 0f, sd > 0 ? hz - fw : -hz), new Vector3(x + fw * 0.5f, legH - rh, sd > 0 ? hz : -hz + fw), Art.J(frame, rng));
                }
        }

        static void SandstoneBlock(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            Color sand = Art.Mix(c.Ground, c.PropAccent, 0.35f), topC = Art.Lighten(sand, 0.08f), glyph = Art.Vivid(c.Emissive, 0.5f, 1f);
            int ny = Count(Y, 0.3f, 6), nx = Count(X, 0.5f, 5);
            float bw = X / nx, e0 = Mathf.Min(0.012f, 0.1f * hz);
            int gj = ny > 1 ? ny - 2 : 0, gi = rng.Range(0, nx);
            var cuts = new float[nx + 2];
            for (int j = 0; j < ny; j++)
            {
                float y0 = Y * j / ny, y1 = j == ny - 1 ? Y : Y * (j + 1) / ny;
                int nc = 0;
                cuts[nc++] = -hx;
                float first = ((j & 1) == 1 && nx > 1) ? bw * 0.5f : bw;
                for (int q = 0; q < nx && nc < nx + 1; q++)
                {
                    float x = -hx + first + q * bw;
                    if (x >= hx - 1e-4f) break;
                    cuts[nc++] = x;
                }
                cuts[nc++] = hx;
                for (int i = 0; i + 1 < nc; i++)
                {
                    bool g = j == gj && i == Mathf.Min(gi, nc - 2);
                    float e = g ? e0 : rng.Range(0f, 1f) * e0;
                    Art.BoxMM(mb, new Vector3(cuts[i], y0, -hz + e), new Vector3(cuts[i + 1], y1, hz - e), Art.J(sand, rng), topC);
                    if (!g) continue;
                    float gr = 0.3f * Mathf.Min(cuts[i + 1] - cuts[i], y1 - y0);
                    Vector3 gc = new Vector3((cuts[i] + cuts[i + 1]) * 0.5f, (y0 + y1) * 0.5f, hz - e0 * 0.5f);
                    Art.Ring(mb, gc, Vector3.forward, gr * 0.6f, gr, 12, glyph, 0.7f);
                    Art.Disc(mb, gc, Vector3.forward, gr * 0.35f, 8, glyph, 0.8f);
                }
            }
        }

        static void PilotSeat(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out float m);
            int n = Count(X, 0.7f, 4);
            float cw = X / n, ct = Mathf.Min(0.1f + 0.05f * m, 0.35f * Y), seat0 = Y - ct, bh = Mathf.Min(0.06f, 0.15f * Y), aw = Mathf.Min(0.08f, 0.18f * cw);
            Color shell = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.45f), pad = Art.Vivid(Art.Darken(c.PropAccent, 0.1f), 0.5f, 0.6f), glow = c.Emissive;
            Art.BoxMM(mb, new Vector3(-hx, 0f, -hz), new Vector3(hx, bh, hz), dark);
            for (int i = 0; i < n; i++)
            {
                float x0 = -hx + cw * i, x1 = i == n - 1 ? hx : x0 + cw, xm = (x0 + x1) * 0.5f;
                if (seat0 > bh + 0.01f)
                {
                    float pr = Mathf.Min(0.08f, 0.2f * Mathf.Min(cw, Z));
                    Art.Column(mb, xm, 0f, pr, pr, bh, seat0 - bh, 6, 1f, 0.8f, shell, false);
                    float sk = Mathf.Min(0.08f, 0.5f * (seat0 - bh)), zf = hz - Mathf.Min(0.004f, 0.1f * hz);
                    Art.BoxMM(mb, new Vector3(x0 + aw, seat0 - sk, -hz * 0.6f), new Vector3(x1 - aw, seat0, zf), dark);
                    Art.RectZ(mb, hz - Mathf.Min(0.001f, 0.02f * hz), x0 + aw, x1 - aw, seat0 - sk * 0.6f, seat0 - sk * 0.3f, 1f, glow, 1f);
                }
                Art.BoxMM(mb, new Vector3(x0 + aw, Mathf.Max(bh, seat0), -hz), new Vector3(x1 - aw, Y, hz), pad, Art.Lighten(pad, 0.1f));
                float zs = hz - Mathf.Min(0.35f * Z, 0.15f);
                for (int sd = 0; sd < 2; sd++)
                {
                    float ax0 = sd == 0 ? x0 : x1 - aw, ax1 = sd == 0 ? x0 + aw : x1;
                    Art.Walls(mb, ax0, ax1, -hz, hz, bh, Y, shell);
                    Art.RectY(mb, Y, ax0, ax1, -hz, zs, 1f, shell);
                    Art.RectY(mb, Y, ax0, ax1, zs, hz, 1f, glow, 0.9f);
                }
            }
        }

        static void CoralRock(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Color rock = Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.3f), 0.2f), top = Art.Mix(rock, c.GroundAccent, 0.45f), coral = Art.Vivid(c.Prop, 0.55f, 0.8f);
            float[] t = { 0f, 0.15f, 0.55f, 0.85f, 1f }, sr = { 0.8f, 0.97f, 1f, 0.92f, 0.75f };
            foreach (var q in Cells(s, 1.6f, 4))
            {
                Art.Lump(mb, q.cx, q.cz, q.ex, q.ez, 0f, s.y, t, sr, 9, Art.J(rock, rng), top, rng, 0.08f);
                // coral sprigs in the corners the round rock leaves free
                float R = Mathf.Min(q.ex, q.ez), rc = 0.12f * R, hc = Mathf.Min(0.45f * s.y, 4f * rc);
                int skip = rng.Range(0, 4);
                for (int k = 0; k < 4; k++)
                {
                    if (k == skip) continue;
                    float sx = (k & 1) == 0 ? -1f : 1f, sz = (k & 2) == 0 ? -1f : 1f;
                    mb.Cylinder(new Vector3(q.cx + sx * q.ex * 0.8f, 0f, q.cz + sz * q.ez * 0.8f), rc, 0f, hc * rng.Range(0.7f, 1f), 6, Art.J(coral, rng), emissive: 0.1f);
                }
            }
        }

        static void Booth(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out float m);
            int n = Count(X, 0.6f, 4);
            float cw = X / n, ct = Mathf.Min(0.12f + 0.05f * m, 0.4f * Y), baseTop = Y - ct, e = Mathf.Min(0.015f, 0.1f * m), pipe = Mathf.Min(0.02f, 0.2f * ct);
            Color velvet = Art.Vivid(Art.Mix(c.Neon, c.PropAccent, 0.45f), 0.55f, 0.5f), wood = Art.Darken(c.PropAccent, 0.4f), brass = Art.Lighten(c.PropAccent, 0.15f);
            if (baseTop > 0.01f)
            {
                Art.BoxMM(mb, new Vector3(-hx + e, 0f, -hz + e), new Vector3(hx - e, baseTop, hz - e), wood);
                Art.RectZ(mb, hz - e * 0.5f, -hx + e, hx - e, Mathf.Min(0.02f, 0.2f * baseTop), Mathf.Min(0.05f, 0.5f * baseTop), 1f, brass, 0.3f);
            }
            float y0 = Mathf.Max(0f, baseTop);
            for (int i = 0; i < n; i++)
            {
                float x0 = -hx + cw * i, x1 = i == n - 1 ? hx : x0 + cw, xm = (x0 + x1) * 0.5f;
                Color v = Art.J(velvet, rng);
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                        Art.RectY(mb, Y, a == 0 ? x0 : xm, a == 0 ? xm : x1, b == 0 ? -hz : 0f, b == 0 ? 0f : hz, 1f, ((a + b) & 1) == 0 ? v : Art.Darken(v, 0.07f));
                Art.RectZ(mb, hz, x0, x1, y0, Y - pipe, 1f, v);
                Art.RectZ(mb, hz, x0, x1, Y - pipe, Y, 1f, c.Neon, 0.7f);
                Art.RectZ(mb, -hz, x0, x1, y0, Y, -1f, Art.Darken(v, 0.1f));
                Art.RectX(mb, x0, -hz, hz, y0, Y, -1f, Art.Darken(v, 0.05f));
                Art.RectX(mb, x1, -hz, hz, y0, Y, 1f, Art.Darken(v, 0.05f));
            }
        }

        // ---------- tall: fill the box (several trunks for a wide box), canopies may rise above ----------

        static void PineTrunk(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng, bool snow)
        {
            float Y = s.y, hc = Mathf.Clamp(0.45f * Y, 0.3f, 1.3f), b1 = Mathf.Max(0.4f * Y, Y - hc);
            Color snowC = Art.Snow(c);
            foreach (var q in Cells(s, 1.6f, 5))
            {
                Color bark = Art.J(Art.Darken(c.PropAccent, 0.1f), rng);
                float ang = rng.Range(0f, 1f);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, 0f, Mathf.Min(0.12f * Y, 0.25f), 8, 1f, 0.82f, Art.Darken(bark, 0.1f), false, 0f, 0f, 0f, ang);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, 0f, b1 + 0.04f * Y, 8, 0.82f, 0.7f, bark, false, 0f, 0f, 0f, ang);
                float h1 = (Y - b1) + 0.5f * hc, b2 = b1 + 0.5f * (Y - b1), h2 = (Y - b2) + 0.9f * hc;
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b1, h1, 6, 1f, 0f, Art.J(c.Prop, rng), false, 0f, 0.05f, 0.4f, ang, null, true);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b2, h2, 6, 0.75f, 0f, Art.J(c.Prop, rng), false, 0f, 0.2f, 0.5f, ang, null, true);
                if (!snow) continue;
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b1 + 0.5f * h1, 0.5f * h1 + 0.03f, 6, 0.55f, 0f, Art.J(snowC, rng), false, 0f, 0.25f, 0.45f, ang, null, true);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b2 + 0.5f * h2, 0.5f * h2 + 0.03f, 6, 0.43f, 0f, Art.J(snowC, rng), false, 0f, 0.35f, 0.5f, ang, null, true);
            }
        }

        static void PalmTrunk(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out _, out float Y, out _, out float hx, out float hz, out _);
            int k = Mathf.Clamp(Mathf.RoundToInt(Y / 0.22f), 2, 6);
            float sh = Y / k;
            Color bark = Art.J(Art.Mix(c.PropAccent, c.GroundAccent, 0.3f), rng), barkD = Art.Darken(bark, 0.12f);
            Color nut = Art.Darken(Art.Mix(c.PropAccent, c.Prop, 0.3f), 0.2f);
            foreach (var q in Cells(s, 1.6f, 5))
            {
                float ang = rng.Range(0f, 1f);
                for (int i = 0; i < k; i++)
                    Art.Column(mb, q.cx, q.cz, q.ex, q.ez, i * sh, sh, 6, 1f, 0.84f, (i & 1) == 0 ? bark : barkD, true, 0f, 0f, 0f, ang);
                // the crown: fronds arching out and up, inside the footprint
                float Rx = hx - Mathf.Abs(q.cx), Rz = hz - Mathf.Abs(q.cz), R = Mathf.Min(Rx, Rz), w = Mathf.Clamp(0.3f * R, 0.03f, 0.3f);
                float off = rng.Range(0f, Tau), up0 = Mathf.Clamp(0.5f * Y, 0.15f, 0.8f);
                var top = new Vector3(q.cx, Y, q.cz);
                for (int f = 0; f < 6; f++)
                {
                    float a = off + f * Tau / 6f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    float rr = 1f / Mathf.Sqrt(ca * ca / (Rx * Rx) + sa * sa / (Rz * Rz));
                    float reach = rr * (0.92f - (w * 0.5f + 0.002f) / R), up = up0 * rng.Range(0.7f, 1.1f);
                    var dir = new Vector3(ca * reach, up, sa * reach);
                    mb.Frond(top, dir, dir.magnitude, w, 3, 0.4f * up, Art.J(c.Prop, rng), 0.3f, 1f);
                }
                float nr = Mathf.Min(0.3f * Mathf.Min(q.ex, q.ez), Mathf.Min(0.1f, 0.3f * Y));
                for (int i = 0; i < 2; i++)
                {
                    float a = off + i * Mathf.PI;
                    var p = new Vector3(q.cx + Mathf.Cos(a) * q.ex * 0.4f, Y - nr, q.cz + Mathf.Sin(a) * q.ez * 0.4f);
                    Art.IcoIn(mb, p - Vector3.one * nr, p + Vector3.one * nr, nut, rng, 0.1f, 0f, 0f, 0.3f, 0.35f);
                }
            }
        }

        static void RockPillar(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color a = Art.Mix(Art.Stone(c), c.PropAccent, 0.35f), b = Art.Lighten(Art.Mix(Art.Stone(c), c.Ground, 0.4f), 0.05f), top = Art.Lighten(b, 0.1f);
            float[] t = { 0f, 0.5f, 1f }, sr = { 0.9f, 1f, 0.88f };
            foreach (var q in Cells(s, 1.6f, 5))
            {
                int L = Mathf.Clamp(Mathf.RoundToInt(Y / Mathf.Max(0.3f, 2.4f * Mathf.Min(q.ex, q.ez))), 1, 5);
                for (int j = 0; j < L; j++)
                {
                    float f = j == L - 1 ? 1f : rng.Range(0.82f, 1f);
                    Art.Lump(mb, q.cx, q.cz, q.ex * f, q.ez * f, Y * j / L, j == L - 1 ? Y : Y * (j + 1) / L, t, sr, 8, Art.J((j & 1) == 0 ? a : b, rng), top, rng, 0.08f);
                }
            }
        }

        static void MachineTower(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out _, out float Y, out _, out float hx, out float hz, out float m);
            Color body = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.45f), panel = Art.Darken(c.Prop, 0.2f);
            float e = Mathf.Min(0.012f, 0.1f * m), bh = Mathf.Min(0.08f, 0.1f * Y), capH = bh * 0.6f;
            Art.BoxMM(mb, new Vector3(-hx, 0f, -hz), new Vector3(hx, bh, hz), dark);
            Art.BoxMM(mb, new Vector3(-hx + e, bh, -hz + e), new Vector3(hx - e, Y - capH, hz - e), body);
            Art.BoxMM(mb, new Vector3(-hx, Y - capH, -hz), new Vector3(hx, Y, hz), dark, Art.Mix(dark, c.Emissive, 0.2f));
            float y0 = bh, y1 = Y - capH, x0 = -hx + e, x1 = hx - e;
            int rows = Count(y1 - y0, 0.3f, 8), cols = Count(x1 - x0, 0.35f, 5);
            float cw = (x1 - x0) / cols, rh = (y1 - y0) / rows, g = Mathf.Min(0.015f, 0.1f * Mathf.Min(cw, rh)), led = Mathf.Min(0.025f, 0.2f * Mathf.Min(cw, rh));
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float zp = sd * (hz - e + Mathf.Min(0.003f, 0.25f * e)), zl = sd * (hz - e + Mathf.Min(0.006f, 0.5f * e));
                for (int r = 0; r < rows; r++)
                    for (int k = 0; k < cols; k++)
                    {
                        float ax = x0 + cw * k + g, bx = x0 + cw * (k + 1) - g, ay = y0 + rh * r + g, by = y0 + rh * (r + 1) - g;
                        Art.RectZ(mb, zp, ax, bx, ay, by, sd, panel);
                        bool on = rng.Next01() < 0.4f;
                        int pick = rng.Range(0, 3);
                        Color lc = on ? (pick == 0 ? c.Emissive : pick == 1 ? c.PropAccent : c.Neon) : dark;
                        Art.RectZ(mb, zl, bx - g - led, bx - g, by - g - led, by - g, sd, lc, on ? 1f : 0f);
                    }
            }
            for (int sd = -1; sd <= 1; sd += 2)
                Art.RectX(mb, sd * (hx - e + Mathf.Min(0.003f, 0.25f * e)), -hz * 0.5f, hz * 0.5f, y0 + 0.1f * (y1 - y0), y1 - 0.1f * (y1 - y0), sd, c.Emissive, 0.7f);
        }

        static void KelpColumn(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color rock = Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.25f), 0.3f), stalkC = Art.Mix(c.GroundAccent, new Color(0.5f, 0.52f, 0.16f), 0.55f);
            float[] t = { 0f, 0.5f, 1f }, sr = { 0.9f, 1f, 0.85f };
            var cells = Cells(s, 1.6f, 5);
            int stalks = cells.Length >= 3 ? 3 : 4;
            const int N = 6;
            foreach (var q in cells)
            {
                float R = Mathf.Min(q.ex, q.ez);
                int L = Mathf.Clamp(Mathf.RoundToInt(Y / Mathf.Max(0.4f, 3f * R)), 1, 3);
                for (int j = 0; j < L; j++)
                    Art.Lump(mb, q.cx, q.cz, q.ex * 0.6f, q.ez * 0.6f, Y * j / L, j == L - 1 ? Y * 0.92f : Y * (j + 1) / L, t, sr, 7, Art.J(rock, rng), Art.Mix(rock, stalkC, 0.4f), rng, 0.1f);
                float r0 = Mathf.Min(0.025f, 0.12f * R), bl = Mathf.Min(0.6f, 2.5f * R), bw = Mathf.Min(0.1f, 0.3f * R);
                for (int k = 0; k < stalks; k++)
                {
                    float a = k * Tau / stalks + rng.Range(-0.3f, 0.3f), ph = rng.Range(0f, Tau), H = Y * rng.Range(1.05f, 1.3f);
                    var pts = new Vector3[N];
                    var rad = new float[N];
                    var wind = new float[N];
                    for (int i = 0; i < N; i++)
                    {
                        float u = i / (N - 1f);
                        float nx = Mathf.Cos(a) * 0.75f + Mathf.Sin(u * 5f + ph) * 0.1f, nz = Mathf.Sin(a) * 0.75f + Mathf.Cos(u * 4f + ph) * 0.1f;
                        pts[i] = new Vector3(q.cx + nx * q.ex, 0.02f + u * (H - 0.02f), q.cz + nz * q.ez);
                        rad[i] = Mathf.Lerp(r0, r0 * 0.5f, u);
                        wind[i] = u;
                    }
                    Color sc = Art.J(stalkC, rng);
                    Art.Tube(mb, pts, rad, 3, sc, wind);
                    for (int i = 2; i < N; i += 2)
                    {
                        float u = i / (N - 1f);
                        mb.Frond(pts[i], new Vector3(-Mathf.Cos(a) * 0.35f, 1f, -Mathf.Sin(a) * 0.35f), bl * rng.Range(0.7f, 1f), bw, 2, 0.05f * bl,
                                 Art.Lighten(sc, 0.08f), u, Mathf.Min(1f, u + 0.25f));
                    }
                }
            }
        }

        static void BrickPillar(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out float m);
            Color brick = Art.Mix(new Color(0.55f, 0.24f, 0.18f), c.PropAccent, 0.35f), stone = Art.Lighten(Art.Dull(c.GroundAccent, 0.4f), 0.25f), mortar = Art.Darken(stone, 0.35f);
            float capH = Mathf.Min(0.1f, 0.08f * Y), baseH = Mathf.Min(0.12f, 0.1f * Y), e = Mathf.Min(0.02f, 0.12f * m);
            Art.BoxMM(mb, new Vector3(-hx, 0f, -hz), new Vector3(hx, baseH, hz), stone);
            Art.BoxMM(mb, new Vector3(-hx, Y - capH, -hz), new Vector3(hx, Y, hz), stone);
            float y0 = baseH, y1 = Y - capH, bx = hx - e, bz = hz - e, mi = Mathf.Min(0.003f, 0.3f * e);
            if (y1 - y0 < 0.01f) return;
            Art.Walls(mb, -bx + mi, bx - mi, -bz + mi, bz - mi, y0, y1, mortar);
            int courses = Count(y1 - y0, 0.14f, 16);
            float ch = (y1 - y0) / courses, g = Mathf.Min(0.006f, 0.05f * ch);
            for (int f = 0; f < 4; f++)
            {
                float w = f < 2 ? 2f * bx : 2f * bz;
                int nb = Count(w, 0.28f, 6);
                float bw = w / nb;
                for (int j = 0; j < courses; j++)
                {
                    float ya = y0 + ch * j + g, yb = y0 + ch * (j + 1) - g;
                    float u = 0f, first = (j & 1) == 1 && nb > 1 ? bw * 0.5f : bw;
                    for (int piece = 0; piece <= nb && u < w - 1e-4f; piece++)
                    {
                        float v = Mathf.Min(w, piece == 0 ? first : u + bw);
                        float a0 = -w * 0.5f + u + (piece == 0 ? 0f : g), a1 = -w * 0.5f + v - (v >= w - 1e-4f ? 0f : g);
                        Color col = Art.J(brick, rng);
                        if (f == 0) Art.RectZ(mb, bz, a0, a1, ya, yb, 1f, col);
                        else if (f == 1) Art.RectZ(mb, -bz, a0, a1, ya, yb, -1f, col);
                        else if (f == 2) Art.RectX(mb, bx, a0, a1, ya, yb, 1f, col);
                        else Art.RectX(mb, -bx, a0, a1, ya, yb, -1f, col);
                        u = v;
                    }
                }
            }
            if (e >= 0.008f && y1 - y0 > 0.3f)
            {
                float nw = Mathf.Min(0.02f, 0.1f * X);
                Art.BoxMM(mb, new Vector3(-nw, y0 + 0.2f * (y1 - y0), bz + 0.002f), new Vector3(nw, y1 - 0.2f * (y1 - y0), hz - 0.002f), c.Neon, null, 1f);
            }
        }

        static void OakTrunk(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y, hc = Mathf.Clamp(0.3f * Y, 0.15f, 0.9f), yb = 0.55f * Y;
            foreach (var q in Cells(s, 1.6f, 5))
            {
                Color bark = Art.J(c.PropAccent, rng);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, 0f, Mathf.Min(0.12f * Y, 0.25f), 7, 1f, 0.75f, Art.Darken(bark, 0.1f), false);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, 0f, yb + 0.1f * Y, 7, 0.75f, 0.6f, bark, false);
                Art.IcoIn(mb, new Vector3(q.cx - q.ex, yb, q.cz - q.ez), new Vector3(q.cx + q.ex, Y + hc, q.cz + q.ez), Art.J(c.Prop, rng), rng, 0.15f, 0f, 0f, 0.3f, 0.8f);
                Art.IcoIn(mb, new Vector3(q.cx - q.ex, yb - 0.05f * Y, q.cz - q.ez * 0.8f), new Vector3(q.cx, 0.9f * Y, q.cz + q.ez * 0.8f), Art.J(c.Prop, rng), rng, 0.15f, 0f, 0f, 0.25f, 0.7f);
                Art.IcoIn(mb, new Vector3(q.cx, yb - 0.05f * Y, q.cz - q.ez * 0.8f), new Vector3(q.cx + q.ex, 0.9f * Y, q.cz + q.ez * 0.8f), Art.J(c.Prop, rng), rng, 0.15f, 0f, 0f, 0.25f, 0.7f);
            }
        }

        // ---------- lights: fit the box, an emissive light at the top ----------

        static void LanternPost(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color wood = Art.J(c.PropAccent, rng), dark = Art.Darken(c.PropAccent, 0.4f), glow = Art.Vivid(c.Emissive, 0.5f, 1f), stone = Art.Stone(c);
            foreach (var q in Cells(s, 2f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), pw = Mathf.Min(Mathf.Clamp(0.12f * R + 0.02f, 0.02f, 0.06f), 0.45f * R);
                bool arm = q.ex >= 0.12f && q.ex >= 3f * pw && Y > 0.35f;
                float px = arm ? q.cx - 0.5f * q.ex : q.cx, lx = arm ? q.cx + 0.4f * q.ex : q.cx;
                float lr = arm ? Mathf.Min(0.09f, 0.9f * Mathf.Min(0.6f * q.ex, q.ez)) : Mathf.Min(0.09f, 0.9f * R);
                lr = Mathf.Max(0.005f, Mathf.Min(lr, (Y - 0.06f) / 2.8f));
                float lh = 2.4f * lr, ly1 = arm ? Y - 0.07f : Y, ly0 = ly1 - lh;
                float fh = Mathf.Min(0.06f, 0.1f * Y), fw = Mathf.Min(2f * pw, arm ? 0.5f * q.ex : q.ex), fz = Mathf.Min(2f * pw, q.ez);
                Art.BoxMM(mb, new Vector3(px - fw, 0f, q.cz - fz), new Vector3(px + fw, fh, q.cz + fz), Art.J(stone, rng));
                Art.BoxMM(mb, new Vector3(px - pw, 0f, q.cz - pw), new Vector3(px + pw, arm ? Y : Mathf.Max(fh, ly0), q.cz + pw), wood);
                if (arm)
                {
                    Art.BoxMM(mb, new Vector3(px, Y - 0.05f, q.cz - pw * 0.6f), new Vector3(lx + 0.02f, Y - 0.015f, q.cz + pw * 0.6f), wood);
                    Art.BoxMM(mb, new Vector3(lx - 0.004f, ly1, q.cz - 0.004f), new Vector3(lx + 0.004f, Y - 0.05f, q.cz + 0.004f), dark);
                }
                Art.Column(mb, lx, q.cz, lr, lr, ly0, 0.12f * lh, 6, 0.8f, 0.9f, dark);
                Art.Column(mb, lx, q.cz, lr, lr, ly0 + 0.12f * lh, 0.6f * lh, 6, 0.75f, 0.75f, glow, false, 0.9f);
                Art.Column(mb, lx, q.cz, lr, lr, ly0 + 0.72f * lh, 0.2f * lh, 6, 1f, 0.15f, dark);
                Art.Column(mb, lx, q.cz, lr, lr, ly0 + 0.92f * lh, 0.08f * lh, 4, 0.12f, 0f, dark);
            }
        }

        static void TikiTorch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color bamboo = Art.J(Art.Mix(c.PropAccent, new Color(0.8f, 0.68f, 0.4f), 0.4f), rng), weave = Art.Darken(c.PropAccent, 0.15f);
            Color flame = c.Emissive, core = Art.Lighten(c.Emissive, 0.5f);
            foreach (var q in Cells(s, 2f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), pr = Mathf.Clamp(0.25f * R, 0.008f, 0.035f), cupR = Mathf.Min(0.9f * R, 0.09f), cupH = Mathf.Min(0.18f, 0.3f * Y);
                Art.Column(mb, q.cx, q.cz, pr, pr, 0f, Y - cupH, 6, 1f, 0.85f, bamboo, false);
                int nodes = Mathf.Clamp(Mathf.FloorToInt((Y - cupH) / 0.35f), 0, 6);
                for (int i = 1; i <= nodes; i++)
                    Art.Column(mb, q.cx, q.cz, pr * 1.25f, pr * 1.25f, i * (Y - cupH) / (nodes + 1f), 0.02f, 6, 1f, 1f, Art.Darken(bamboo, 0.15f), false);
                Art.Column(mb, q.cx, q.cz, cupR, cupR, Y - cupH, cupH, 6, 0.5f, 1f, weave, true, 0f, 0f, 0f, 0f, Art.Darken(weave, 0.4f));
                float fh = Mathf.Min(0.25f, 1.6f * cupR + 0.05f);
                Art.Column(mb, q.cx, q.cz, cupR, cupR, Y, fh, 5, 0.6f, 0f, flame, false, 1f, 0.6f, 1f);
                Art.Column(mb, q.cx, q.cz, cupR, cupR, Y, fh * 0.65f, 5, 0.35f, 0f, core, false, 1f, 0.6f, 1f, 0.6f);
            }
        }

        static void Brazier(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color bronze = Art.J(Art.Mix(c.PropAccent, new Color(0.55f, 0.4f, 0.2f), 0.4f), rng), dark = Art.Darken(bronze, 0.4f);
            Color ember = Art.Mix(c.Emissive, new Color(0.9f, 0.3f, 0.05f), 0.4f), flame = c.Emissive, core = Art.Lighten(c.Emissive, 0.5f);
            foreach (var q in Cells(s, 1.6f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), bowlH = Mathf.Min(0.35f * Y, 0.6f * R + 0.05f), footH = Mathf.Min(0.05f, 0.1f * Y);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.7f, q.ez * 0.7f, 0f, footH, 8, 1f, 0.8f, dark);
                Art.Column(mb, q.cx, q.cz, 0.18f * R, 0.18f * R, footH, Mathf.Max(0.01f, Y - bowlH - footH), 6, 1f, 0.8f, bronze, false);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.95f, q.ez * 0.95f, Y - bowlH, bowlH * 0.92f, 8, 0.45f, 1f, bronze, false);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, Y - bowlH * 0.08f, bowlH * 0.08f, 8, 1f, 1f, dark, false);
                mb.Push(new Vector3(q.cx, Y - 0.3f * bowlH, q.cz), Quaternion.identity, new Vector3(q.ex, 1f, q.ez));
                mb.Disc(Vector3.zero, 0.8f, 8, ember, 0.8f);
                mb.Pop();
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Tau / 3f + rng.Range(0f, 1f);
                    float ox = Mathf.Cos(a) * 0.25f * q.ex, oz = Mathf.Sin(a) * 0.25f * q.ez;
                    Art.IcoIn(mb, new Vector3(q.cx + ox - 0.2f * q.ex, Y - 0.3f * bowlH, q.cz + oz - 0.2f * q.ez),
                              new Vector3(q.cx + ox + 0.2f * q.ex, Y - 0.08f * bowlH, q.cz + oz + 0.2f * q.ez), ember, rng, 0.15f, 0.3f, 0.9f);
                }
                float fh = Mathf.Min(0.4f, 1.2f * R);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.5f, q.ez * 0.5f, Y - 0.25f * bowlH, fh, 5, 1f, 0f, flame, false, 1f, 0.5f, 1f);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.5f, q.ez * 0.5f, Y - 0.25f * bowlH, fh * 0.65f, 5, 0.55f, 0f, core, false, 1f, 0.5f, 1f, 0.6f);
            }
        }

        static void OilLamp(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color brass = Art.J(Art.Mix(c.PropAccent, new Color(0.75f, 0.6f, 0.3f), 0.5f), rng), dark = Art.Darken(c.PropAccent, 0.45f);
            Color glass = Art.Vivid(c.Emissive, 0.5f, 1f), wood = Art.J(c.PropAccent, rng);
            foreach (var q in Cells(s, 1.6f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), lh = Mathf.Min(Y, Mathf.Max(0.3f, 2.6f * R)), yb = Y - lh, Rl = Mathf.Min(R, lh / 2.4f);
                if (yb > 0.02f)
                {
                    float st = Mathf.Min(0.03f, 0.1f * yb);
                    Art.Column(mb, q.cx, q.cz, q.ex * 0.7f, q.ez * 0.7f, 0f, Mathf.Min(0.04f, 0.1f * yb), 8, 1f, 0.85f, dark);
                    Art.Column(mb, q.cx, q.cz, 0.15f * R, 0.15f * R, 0f, yb, 6, 1f, 0.8f, wood, false);
                    Art.Column(mb, q.cx, q.cz, q.ex * 0.8f, q.ez * 0.8f, yb - st, st, 8, 1f, 1f, wood);
                }
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb, 0.12f * lh, 8, 0.9f, 0.75f, brass);
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb + 0.12f * lh, 0.2f * lh, 8, 0.75f, 0.6f, brass, false);
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb + 0.32f * lh, 0.05f * lh, 8, 0.35f, 0.35f, dark);
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb + 0.37f * lh, 0.25f * lh, 8, 0.4f, 0.62f, glass, false, 0.85f);
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb + 0.62f * lh, 0.25f * lh, 8, 0.62f, 0.3f, glass, false, 0.85f);
                Art.Column(mb, q.cx, q.cz, Rl, Rl, yb + 0.87f * lh, 0.09f * lh, 8, 0.32f, 0.25f, dark);
                // the bail handle, its top at y
                float hr = 0.7f * Rl, tr = Mathf.Max(0.003f, 0.04f * Rl), cy = Y - hr - tr;
                var pts = new Vector3[7];
                var rad = new float[7];
                for (int i = 0; i < 7; i++)
                {
                    float ph = Mathf.PI * i / 6f;
                    pts[i] = new Vector3(q.cx + Mathf.Cos(ph) * hr, cy + Mathf.Sin(ph) * hr, q.cz);
                    rad[i] = tr;
                }
                Art.Tube(mb, pts, rad, 4, dark);
            }
        }

        static void LightPylon(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color metal = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.45f), glow = c.Emissive;
            foreach (var q in Cells(s, 2f, 3))
            {
                float bh = Mathf.Min(0.08f, 0.12f * Y), capH = Mathf.Min(0.04f, 0.06f * Y), headH = Mathf.Min(0.25f, 0.2f * Y);
                Art.BoxMM(mb, new Vector3(q.cx - q.ex, 0f, q.cz - q.ez), new Vector3(q.cx + q.ex, bh, q.cz + q.ez), dark, c.PropAccent);
                float colTop = Y - capH - headH, qx = 0.35f * q.ex, qz = 0.35f * q.ez;
                if (colTop > bh + 0.01f)
                {
                    Art.BoxMM(mb, new Vector3(q.cx - qx, bh, q.cz - qz), new Vector3(q.cx + qx, colTop, q.cz + qz), metal);
                    float sw = 0.3f * qx, sy0 = bh + 0.1f * (colTop - bh), sy1 = colTop - 0.1f * (colTop - bh), off = Mathf.Min(0.003f, 0.1f * q.ez);
                    Art.RectZ(mb, q.cz + qz + off, q.cx - sw, q.cx + sw, sy0, sy1, 1f, glow, 0.9f);
                    Art.RectZ(mb, q.cz - qz - off, q.cx - sw, q.cx + sw, sy0, sy1, -1f, glow, 0.9f);
                }
                Art.BoxMM(mb, new Vector3(q.cx - 0.6f * q.ex, Mathf.Max(bh, colTop), q.cz - 0.6f * q.ez), new Vector3(q.cx + 0.6f * q.ex, Y - capH, q.cz + 0.6f * q.ez), glow, null, 1f);
                Art.BoxMM(mb, new Vector3(q.cx - 0.75f * q.ex, Y - capH, q.cz - 0.75f * q.ez), new Vector3(q.cx + 0.75f * q.ex, Y, q.cz + 0.75f * q.ez), dark);
            }
        }

        static void JellyLamp(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color jelly = Art.Lighten(Art.Mix(c.PropAccent, c.Emissive, 0.4f), 0.15f), inner = c.Emissive, rock = Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.3f), 0.2f);
            foreach (var q in Cells(s, 1.6f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), bellH = Mathf.Min(0.45f * Y, 1.1f * R + 0.04f), b0 = Y - bellH, rh = Mathf.Min(0.1f, 0.2f * Y);
                Art.IcoIn(mb, new Vector3(q.cx - q.ex * 0.6f, 0f, q.cz - q.ez * 0.6f), new Vector3(q.cx + q.ex * 0.6f, rh, q.cz + q.ez * 0.6f), Art.J(rock, rng), rng, 0.15f, 0.4f);
                Art.Column(mb, q.cx, q.cz, 0.06f * R, 0.06f * R, 0f, b0 + 0.3f * bellH, 5, 1f, 1f, Art.Darken(jelly, 0.2f), false, 0.3f);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b0, 0.35f * bellH, 10, 1f, 0.92f, jelly, false, 0.8f, 0.2f, 0.25f);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b0 + 0.35f * bellH, 0.4f * bellH, 10, 0.92f, 0.65f, jelly, false, 0.8f, 0.25f, 0.3f);
                Art.Column(mb, q.cx, q.cz, q.ex, q.ez, b0 + 0.75f * bellH, 0.25f * bellH, 10, 0.65f, 0f, Art.Lighten(jelly, 0.15f), false, 0.85f, 0.3f, 0.3f);
                mb.Push(new Vector3(q.cx, b0, q.cz), Quaternion.identity, new Vector3(q.ex, 1f, q.ez));
                mb.Disc(Vector3.zero, 0.98f, 10, inner, 0.9f, true);
                mb.Pop();
                float tl = Mathf.Min(0.9f * b0 - 0.05f, 1.2f * bellH), tw = Mathf.Min(0.04f, 0.3f * R);
                if (tl < 0.03f) continue;
                for (int i = 0; i < 7; i++)
                {
                    float a = i * Tau / 7f + rng.Range(-0.2f, 0.2f);
                    var root = new Vector3(q.cx + Mathf.Cos(a) * 0.7f * q.ex, b0, q.cz + Mathf.Sin(a) * 0.7f * q.ez);
                    mb.Frond(root, new Vector3(-Mathf.Cos(a) * 0.08f, -1f, -Mathf.Sin(a) * 0.08f), tl * rng.Range(0.7f, 1f), tw, 3, 0f, Art.J(jelly, rng), 0.3f, 1f);
                }
            }
        }

        static void StreetLamp(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color metal = Art.J(Art.Darken(Art.Mix(c.Prop, new Color(0.2f, 0.2f, 0.22f), 0.4f), 0.1f), rng), glow = c.Emissive;
            foreach (var q in Cells(s, 2f, 3))
            {
                float R = Mathf.Min(q.ex, q.ez), hh = Mathf.Min(0.3f * Y, 2f * R + 0.05f), bH = Mathf.Min(0.1f * Y, 0.35f), pr = Mathf.Max(0.008f, Mathf.Min(0.055f, 0.3f * R));
                float poleTop = Y - hh;
                Art.Column(mb, q.cx, q.cz, R * 0.8f, R * 0.8f, 0f, bH, 6, 1f, 0.75f, metal);
                Art.Column(mb, q.cx, q.cz, pr, pr, bH, Mathf.Max(0.01f, poleTop - bH), 6, 1f, 0.85f, metal, false);
                Art.Column(mb, q.cx, q.cz, R, R, poleTop, 0.08f * hh, 6, 0.5f, 0.5f, metal);
                Art.Column(mb, q.cx, q.cz, R, R, poleTop + 0.08f * hh, 0.5f * hh, 6, 0.55f, 0.85f, glow, false, 1f);
                Art.Column(mb, q.cx, q.cz, R, R, poleTop + 0.58f * hh, 0.3f * hh, 6, 1f, 0f, metal, false, 0f, 0f, 0f, 0f, null, true);
                Art.Column(mb, q.cx, q.cz, R, R, poleTop + 0.88f * hh, 0.12f * hh, 4, 0.1f, 0f, metal);
            }
        }

        // ---------- plants: may rise above the box, never outside its footprint ----------

        /// <summary>Fronds from (cx, y0, cz) arching out and up to <paramref name="rise"/>, reaching no further than the cell's ellipse.</summary>
        static void FrondClump(MeshBuilder mb, float cx, float cz, float ex, float ez, float y0, float rise, int n, int segs, float widthK, float sagK, Color col, Rng rng)
        {
            float R = Mathf.Min(ex, ez), w = Mathf.Min(Mathf.Clamp(widthK * R, 0.02f, 0.35f), 0.35f * R);
            float off = rng.Range(0f, Tau);
            for (int i = 0; i < n; i++)
            {
                float a = off + i * Tau / n + rng.Range(-0.2f, 0.2f), ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float rr = 1f / Mathf.Sqrt(ca * ca / (ex * ex) + sa * sa / (ez * ez));
                float reach = rr * (0.92f - (w * 0.5f + 0.002f) / R) * rng.Range(0.75f, 1f);
                float h = Mathf.Max(0.02f, rise * rng.Range(0.75f, 1.05f));
                var dir = new Vector3(ca * reach, h, sa * reach);
                mb.Frond(new Vector3(cx, y0, cz), dir, dir.magnitude, w, segs, sagK * h, Art.J(col, rng), 0f, 1f);
            }
        }

        static void FernSkin(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color leaf = Art.Mix(c.Prop, c.GroundAccent, 0.2f), trunk = Art.Darken(c.PropAccent, 0.2f);
            bool tree = Y > 1.1f;
            foreach (var q in Cells(s, 1.6f, 4))
            {
                float y0 = tree ? 0.6f * Y : 0f;
                if (tree) Art.Column(mb, q.cx, q.cz, 0.22f * q.ex, 0.22f * q.ez, 0f, y0 + 0.02f, 6, 1f, 0.8f, trunk, true);
                else Art.IcoIn(mb, new Vector3(q.cx - 0.3f * q.ex, 0f, q.cz - 0.3f * q.ez), new Vector3(q.cx + 0.3f * q.ex, Mathf.Min(0.12f, 0.2f * Y), q.cz + 0.3f * q.ez),
                               Art.Darken(leaf, 0.3f), rng, 0.15f, 0.4f);
                FrondClump(mb, q.cx, q.cz, q.ex, q.ez, y0, tree ? 0.45f * Y : Y, 9, 3, 0.3f, tree ? 0.45f : 0.25f, leaf, rng);
            }
        }

        static void PalmBush(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y, stub = Mathf.Min(0.35f * Y, 0.4f);
            Color trunk = Art.Mix(c.PropAccent, c.GroundAccent, 0.3f);
            foreach (var q in Cells(s, 1.6f, 4))
            {
                Art.Column(mb, q.cx, q.cz, 0.3f * q.ex, 0.3f * q.ez, 0f, stub, 6, 1f, 0.7f, Art.J(trunk, rng), true);
                FrondClump(mb, q.cx, q.cz, q.ex, q.ez, stub, Y - stub + 0.1f * Y, 7, 4, 0.45f, 0.4f, c.Prop, rng);
            }
        }

        static void CactusSmall(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color pink = Art.Mix(new Color(1f, 0.45f, 0.6f), c.PropAccent, 0.2f), sand = Art.Mix(c.Ground, c.GroundAccent, 0.4f);
            foreach (var q in Cells(s, 1.6f, 4))
            {
                float R = Mathf.Min(q.ex, q.ez), rc = 0.45f * R, Hc = Y * rng.Range(0.9f, 1f);
                Color gc = Art.J(c.Prop, rng);
                Art.IcoIn(mb, new Vector3(q.cx - q.ex * 0.8f, 0f, q.cz - q.ez * 0.8f), new Vector3(q.cx + q.ex * 0.8f, Mathf.Min(0.06f, 0.1f * Y), q.cz + q.ez * 0.8f),
                          Art.J(sand, rng), rng, 0.15f, 0.4f);
                if (Hc < 2.2f * rc)
                    Art.IcoIn(mb, new Vector3(q.cx - rc, 0f, q.cz - rc), new Vector3(q.cx + rc, Hc, q.cz + rc), gc, rng, 0.08f, 0.35f);
                else
                {
                    Art.Column(mb, q.cx, q.cz, rc, rc, 0f, Hc - rc, 8, 1f, 0.97f, gc, false);
                    Art.Column(mb, q.cx, q.cz, rc, rc, Hc - rc, rc, 8, 0.97f, 0.35f, gc);
                    bool alongX = q.ex >= q.ez;
                    float e = alongX ? q.ex : q.ez, ar = 0.6f * rc, outD = Mathf.Min(e - ar - 0.005f, rc + 0.5f * e);
                    if (outD > rc + ar * 0.5f && Hc > 4f * rc)
                    {
                        float sg = rng.Next01() < 0.5f ? -1f : 1f;
                        int arms = 1 + rng.Range(0, 2);
                        for (int i = 0; i < arms; i++)
                        {
                            float sd = i == 0 ? sg : -sg, ya = Hc * rng.Range(0.3f, 0.5f), up = Mathf.Max(0.01f, Mathf.Min(Hc * 0.3f, Hc - ya - ar * 2.4f));
                            Vector3 d = alongX ? Vector3.right * sd : Vector3.forward * sd, o = new Vector3(q.cx, 0f, q.cz);
                            var pts = new[]
                            {
                                o + d * (rc * 0.3f) + Vector3.up * ya, o + d * outD + Vector3.up * ya,
                                o + d * outD + Vector3.up * (ya + ar * 1.4f), o + d * outD + Vector3.up * (ya + ar * 1.4f + up)
                            };
                            Art.Tube(mb, pts, new[] { ar, ar, ar, ar * 0.9f }, 8, gc, null, 0f, false, true);
                        }
                    }
                }
                Art.Column(mb, q.cx, q.cz, rc * 0.5f, rc * 0.5f, Hc - 0.01f, Mathf.Min(0.05f, 0.3f * rc), 5, 0.4f, 1f, pink, true, 0.2f);
            }
        }

        static void SnowBush(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color leaf = Art.Darken(c.Prop, 0.05f), snow = Art.Snow(c);
            foreach (var q in Cells(s, 1.6f, 5))
            {
                Art.IcoIn(mb, new Vector3(q.cx - q.ex, 0f, q.cz - q.ez), new Vector3(q.cx + q.ex, 0.85f * Y, q.cz + q.ez), Art.J(leaf, rng), rng, 0.18f, 0.35f, 0f, 0f, 0.4f);
                Art.IcoIn(mb, new Vector3(q.cx - q.ex, 0f, q.cz - 0.6f * q.ez), new Vector3(q.cx - 0.1f * q.ex, 0.55f * Y, q.cz + 0.6f * q.ez), Art.J(leaf, rng), rng, 0.18f, 0.35f, 0f, 0f, 0.3f);
                Art.IcoIn(mb, new Vector3(q.cx - 0.75f * q.ex, 0.5f * Y, q.cz - 0.75f * q.ez), new Vector3(q.cx + 0.75f * q.ex, Y, q.cz + 0.75f * q.ez), Art.J(snow, rng), rng, 0.12f, 0.5f, 0f, 0.3f, 0.45f);
            }
        }

        static void Hydroponic(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out float m);
            Color metal = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.45f), soil = new Color(0.25f, 0.18f, 0.14f);
            Color leaf = Art.Mix(new Color(0.35f, 0.78f, 0.38f), c.Emissive, 0.15f), grow = Art.Mix(c.Emissive, new Color(1f, 0.35f, 0.9f), 0.5f);
            int tiers = Count(Y, 0.45f, 4), np = Count(X, 0.15f, 8);
            float th = Y / tiers, pw = Mathf.Min(0.03f, 0.1f * m);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Art.BoxMM(mb, new Vector3(sx > 0 ? hx - pw : -hx, 0f, sz > 0 ? hz - pw : -hz), new Vector3(sx > 0 ? hx : -hx + pw, Y, sz > 0 ? hz : -hz + pw), dark);
            for (int j = 0; j < tiers; j++)
            {
                float y0 = j * th, trayH = Mathf.Min(0.08f, 0.25f * th);
                Art.BoxMM(mb, new Vector3(-hx + pw, y0, -hz + pw), new Vector3(hx - pw, y0 + trayH, hz - pw), metal, soil);
                float ly1 = (j + 1) * th - Mathf.Min(0.005f, 0.02f * th), ly0 = ly1 - Mathf.Min(0.015f, 0.05f * th);
                Art.BoxMM(mb, new Vector3(-hx + pw, ly0, -hz * 0.4f), new Vector3(hx - pw, ly1, hz * 0.4f), grow, null, 1f);
                float ph = Mathf.Min(0.6f * (ly0 - y0 - trayH), 0.25f);
                if (ph < 0.02f) continue;
                for (int i = 0; i < np; i++)
                {
                    float x0 = -hx + pw + (X - 2f * pw) * i / np, x1 = -hx + pw + (X - 2f * pw) * (i + 1) / np;
                    Art.IcoIn(mb, new Vector3(x0, y0 + trayH, -hz * 0.7f), new Vector3(x1, y0 + trayH + ph * rng.Range(0.7f, 1f), hz * 0.7f), Art.J(leaf, rng), rng, 0.2f, 0.35f, 0.05f, 0f, 0.4f);
                }
            }
        }

        static void Anemone(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color body = Art.J(c.PropAccent, rng), arm = Art.Vivid(c.Prop, 0.5f, 0.8f), tip = Art.Vivid(c.Emissive, 0.5f, 1f);
            var cells = Cells(s, 1.6f, 4);
            int k = cells.Length > 2 ? 12 : 18;
            foreach (var q in cells)
            {
                float R = Mathf.Min(q.ex, q.ez), colH = 0.35f * Y, rt = 0.06f * R;
                Art.Column(mb, q.cx, q.cz, q.ex * 0.55f, q.ez * 0.55f, 0f, colH, 8, 1f, 0.85f, body, true, 0f, 0f, 0f, 0f, Art.Lighten(body, 0.2f));
                for (int i = 0; i < k; i++)
                {
                    float a = i * Tau / k + rng.Range(-0.15f, 0.15f), tilt = Mathf.Deg2Rad * rng.Range(12f, 40f);
                    var d = new Vector3(Mathf.Cos(a) * Mathf.Sin(tilt), Mathf.Cos(tilt), Mathf.Sin(a) * Mathf.Sin(tilt));
                    float L = Mathf.Min((Y - colH) * rng.Range(0.75f, 1.05f) / Mathf.Cos(tilt), 0.45f * R / Mathf.Sin(tilt));
                    var root = new Vector3(q.cx + Mathf.Cos(a) * 0.4f * q.ex, colH, q.cz + Mathf.Sin(a) * 0.4f * q.ez);
                    mb.Push(root, Quaternion.FromToRotation(Vector3.up, d), Vector3.one);
                    mb.Cylinder(Vector3.zero, rt, rt * 0.4f, L * 0.8f, 4, Art.J(arm, rng), false, false, 0.1f, 0.3f, 0.85f);
                    mb.Cylinder(new Vector3(0f, L * 0.8f, 0f), rt * 0.4f, 0f, L * 0.2f, 4, tip, false, false, 0.7f, 0.85f, 1f);
                    mb.Pop();
                }
            }
        }

        static void PottedPalm(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color pot = Art.Darken(c.PropAccent, 0.1f), rim = Art.Lighten(c.PropAccent, 0.2f), soil = new Color(0.2f, 0.14f, 0.1f);
            Color bark = Art.Mix(c.PropAccent, c.GroundAccent, 0.5f), leaf = Art.Mix(new Color(0.25f, 0.55f, 0.3f), c.Prop, 0.3f);
            foreach (var q in Cells(s, 1.6f, 3))
            {
                float potH = Mathf.Min(0.35f * Y, 0.45f), trunkH = (Y - potH) * 0.5f, rimH = Mathf.Min(0.04f, 0.15f * potH);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.75f, q.ez * 0.75f, 0f, potH, 8, 0.75f, 1f, pot, true, 0f, 0f, 0f, 0f, soil);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.8f, q.ez * 0.8f, potH - rimH, rimH, 8, 1f, 1f, rim, false);
                Art.Column(mb, q.cx, q.cz, q.ex * 0.12f, q.ez * 0.12f, potH, trunkH, 6, 1f, 0.75f, bark, true);
                FrondClump(mb, q.cx, q.cz, q.ex, q.ez, potH + trunkH, (Y - potH - trunkH) + 0.1f * Y, 7, 4, 0.35f, 0.45f, leaf, rng);
            }
        }

        static void FlowerBush(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            float Y = s.y;
            Color leaf = Art.Mix(c.Prop, c.GroundAccent, 0.15f), centre = Art.Vivid(c.Emissive, 0.6f, 0.95f);
            foreach (var q in Cells(s, 1.6f, 5))
            {
                Art.IcoIn(mb, new Vector3(q.cx - q.ex, 0f, q.cz - q.ez), new Vector3(q.cx + q.ex, Y, q.cz + q.ez), Art.J(leaf, rng), rng, 0.15f, 0.35f, 0f, 0f, 0.45f);
                float R = Mathf.Min(q.ex, q.ez), fr = Mathf.Min(0.05f, 0.15f * R);
                Color pc = Petal(rng);
                for (int i = 0; i < 7; i++)
                {
                    float phi = rng.Range(0f, Tau), th = rng.Range(0f, 0.9f);
                    float px = q.cx + Mathf.Cos(phi) * Mathf.Sin(th) * q.ex * 0.8f, pz = q.cz + Mathf.Sin(phi) * Mathf.Sin(th) * q.ez * 0.8f;
                    float py = 0.3f * Y + Mathf.Cos(th) * 0.7f * Y - 0.5f * fr;
                    Art.Column(mb, px, pz, fr, fr, py, fr * 0.6f, 5, 0.4f, 1f, Art.J(pc, rng), true, 0.15f, 0.6f, 0.8f, phi, centre);
                }
            }
        }

        static readonly Color[] Petals =
        {
            new Color(1f, 0.55f, 0.72f), new Color(0.98f, 0.96f, 0.92f), new Color(0.78f, 0.6f, 0.98f), new Color(1f, 0.8f, 0.35f)
        };

        static Color Petal(Rng rng) => Petals[rng.Range(0, Petals.Length)];

        // ---------- clutter: a boulder or object filling ~90 % of the box ----------

        static void Boulders(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng, Color col, Color? cap)
        {
            float Y = s.y;
            foreach (var q in Cells(s, 1.5f, 4))
            {
                float w = 2f * Mathf.Max(q.ex, q.ez);
                int L = Mathf.Clamp(Mathf.RoundToInt(Y / Mathf.Max(0.25f, 1.1f * w)), 1, 3);
                for (int j = 0; j < L; j++)
                {
                    float y0 = Y * j / L, y1 = j == L - 1 ? Y : Y * (j + 1) / L, k = 0.97f * (j == 0 ? 1f : rng.Range(0.85f, 0.95f));
                    var mn = new Vector3(q.cx - q.ex * k, y0, q.cz - q.ez * k);
                    var mx = new Vector3(q.cx + q.ex * k, y1, q.cz + q.ez * k);
                    Art.IcoIn(mb, mn, mx, Art.J(col, rng), rng, 0.15f, 0.35f);
                    if (cap.HasValue)
                        Art.IcoIn(mb, new Vector3(Mathf.Lerp(mn.x, mx.x, 0.12f), Mathf.Lerp(y0, y1, 0.55f), Mathf.Lerp(mn.z, mx.z, 0.12f)),
                                  new Vector3(Mathf.Lerp(mn.x, mx.x, 0.88f), y1, Mathf.Lerp(mn.z, mx.z, 0.88f)), Art.J(cap.Value, rng), rng, 0.12f, 0.45f);
                }
            }
        }

        static void ReefRock(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Boulders(mb, s, c, rng, Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.35f), 0.25f), null);
            Color inner = Art.Darken(c.PropAccent, 0.5f);
            foreach (var q in Cells(s, 1.5f, 4))
            {
                float R = Mathf.Min(q.ex, q.ez), r = 0.12f * R, h = Mathf.Min(0.4f * s.y, 3f * r + 0.05f);
                for (int k = 0; k < 2; k++)
                {
                    float sx = k == 0 ? -1f : 1f, sz = rng.Next01() < 0.5f ? -1f : 1f;
                    mb.Cylinder(new Vector3(q.cx + sx * q.ex * 0.8f, 0f, q.cz + sz * q.ez * 0.8f), r, r * 1.25f, h * rng.Range(0.7f, 1f), 6,
                                Art.J(k == 0 ? c.Prop : c.PropAccent, rng), capColor: inner);
                }
            }
        }

        static void Crates(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out _);
            float u = Mathf.Max(0.3f, Mathf.Min(X, Z));
            int nx = Count(X, u, 3), ny = Count(Y, u, 4), nz = Count(Z, u, 2);
            while (nx * ny * nz > 12) { if (ny > 1) ny--; else if (nx > 1) nx--; else nz--; }
            Color frame = c.PropAccent, panel = Art.Mix(c.Prop, c.Ground, 0.2f);
            const float gap = 0.004f;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        var mn = new Vector3(-hx + X * i / nx + (i > 0 ? gap : 0f), Y * j / ny, -hz + Z * k / nz + (k > 0 ? gap : 0f));
                        var mx = new Vector3(i == nx - 1 ? hx : -hx + X * (i + 1) / nx - gap, j == ny - 1 ? Y : Y * (j + 1) / ny, k == nz - 1 ? hz : -hz + Z * (k + 1) / nz - gap);
                        Crate(mb, mn, mx, Art.J(frame, rng), Art.J(panel, rng), c.Emissive);
                    }
        }

        static void Crate(MeshBuilder mb, Vector3 mn, Vector3 mx, Color frame, Color panel, Color label)
        {
            Vector3 sz = mx - mn;
            float t = Mathf.Min(0.04f, 0.12f * Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)));
            Art.BoxMM(mb, mn + new Vector3(t * 0.4f, 0f, t * 0.4f), mx - new Vector3(t * 0.4f, 0f, t * 0.4f), panel);
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                {
                    float x0 = a == 0 ? mn.x : mx.x - t, z0 = b == 0 ? mn.z : mx.z - t;
                    Art.BoxMM(mb, new Vector3(x0, mn.y, z0), new Vector3(x0 + t, mx.y, z0 + t), frame);
                }
            Art.BoxMM(mb, new Vector3(mn.x + t, mx.y - t, mn.z), new Vector3(mx.x - t, mx.y, mn.z + t), frame);
            Art.BoxMM(mb, new Vector3(mn.x + t, mx.y - t, mx.z - t), new Vector3(mx.x - t, mx.y, mx.z), frame);
            Art.BoxMM(mb, new Vector3(mn.x, mx.y - t, mn.z + t), new Vector3(mn.x + t, mx.y, mx.z - t), frame);
            Art.BoxMM(mb, new Vector3(mx.x - t, mx.y - t, mn.z + t), new Vector3(mx.x, mx.y, mx.z - t), frame);
            float lw = 0.3f * sz.x, lh = 0.12f * sz.y, cx = (mn.x + mx.x) * 0.5f, cy = (mn.y + mx.y) * 0.5f;
            Art.RectZ(mb, mx.z - t * 0.4f + Mathf.Min(0.002f, t * 0.3f), cx - lw * 0.5f, cx + lw * 0.5f, cy - lh * 0.5f, cy + lh * 0.5f, 1f, label, 0.6f);
        }

        static void Speaker(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out float Z, out float hx, out float hz, out _);
            Color body = Art.Darken(Art.Mix(c.Prop, new Color(0.1f, 0.1f, 0.12f), 0.5f), 0.1f), cone = new Color(0.08f, 0.08f, 0.1f), trim = Art.Lighten(c.PropAccent, 0.1f);
            int ny = Count(Y, Mathf.Max(0.35f, 0.9f * X), 3);
            float e = Mathf.Min(0.01f, 0.05f * X), zf = hz - Mathf.Min(0.01f, 0.1f * Z), zr = zf + Mathf.Min(0.003f, 0.3f * (hz - zf));
            for (int j = 0; j < ny; j++)
            {
                float y0 = Y * j / ny, y1 = j == ny - 1 ? Y : Y * (j + 1) / ny, h = y1 - y0, w = X - 2f * e;
                Art.BoxMM(mb, new Vector3(-hx + e, y0, -hz), new Vector3(hx - e, y1, zf), Art.J(body, rng), Art.Lighten(body, 0.05f));
                float wr = 0.36f * Mathf.Min(w, h), wy = y0 + 0.4f * h;
                Art.Ring(mb, new Vector3(0f, wy, zr), Vector3.forward, wr * 0.86f, wr, 16, c.Neon, 0.7f);
                Art.Disc(mb, new Vector3(0f, wy, zr), Vector3.forward, wr * 0.86f, 12, cone, 0f, -Mathf.Min(0.25f * wr, 0.5f * Z));
                float tr = Mathf.Min(0.08f * h, 0.15f * w), ty = y1 - Mathf.Max(1.4f * tr, 0.1f * h);
                Art.Ring(mb, new Vector3(0f, ty, zr), Vector3.forward, tr * 0.6f, tr, 10, trim, 0.2f);
                Art.Disc(mb, new Vector3(0f, ty, zr), Vector3.forward, tr * 0.6f, 8, cone, 0f, -Mathf.Min(0.3f * tr, 0.3f * Z));
            }
        }

        // ---------- doors and windows: only the frame (outer 15 % of x, top 15 % of y); the middle is the opening ----------

        static void TreeArch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, rise = Mathf.Min(0.25f, 0.12f * Y);
            Color bark = Art.J(Art.Darken(c.PropAccent, 0.05f), rng);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float cx = sd * (hx - pw * 0.5f);
                Art.Column(mb, cx, 0f, pw * 0.5f, hz, 0f, Mathf.Min(0.12f * Y, 0.2f), 7, 1f, 0.8f, Art.Darken(bark, 0.1f), false);
                Art.Column(mb, cx, 0f, pw * 0.5f, hz, 0f, Y - lh * 0.5f, 7, 0.8f, 0.65f, bark, false);
            }
            float rb = Mathf.Min(0.4f * lh, Mathf.Min(0.95f * hz, 0.45f * pw));
            var pts = new Vector3[5];
            var rad = new float[5];
            for (int i = 0; i < 5; i++)
            {
                pts[i] = new Vector3(Mathf.Lerp(-(hx - pw * 0.5f), hx - pw * 0.5f, i / 4f), Y - lh * 0.5f, 0f);
                rad[i] = rb;
            }
            Art.Tube(mb, pts, rad, 5, bark);
            int n = Count(X, 0.35f, 7);
            for (int i = 0; i < n; i++)
            {
                float x0 = -hx + X * i / n, x1 = -hx + X * (i + 1) / n, ov = 0.25f * (x1 - x0);
                Art.IcoIn(mb, new Vector3(Mathf.Max(-hx, x0 - ov), Y - lh, -hz), new Vector3(Mathf.Min(hx, x1 + ov), Y + rise * rng.Range(0.5f, 1f), hz),
                          Art.J(c.Prop, rng), rng, 0.18f, 0f, 0f, 0.3f, 0.7f);
            }
        }

        static void PalmArch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, w = Mathf.Min(0.25f, Mathf.Min(1.8f * hz, 0.5f * lh));
            int k = Mathf.Clamp(Mathf.RoundToInt(Y / 0.25f), 2, 8);
            float sh = Y / k;
            Color bark = Art.J(Art.Mix(c.PropAccent, c.GroundAccent, 0.3f), rng), barkD = Art.Darken(bark, 0.12f), nut = Art.Darken(Art.Mix(c.PropAccent, c.Prop, 0.3f), 0.2f);
            float[] ups = { 0.12f, 0.35f, 0.8f, 1.8f }, lens = { 0.75f, 0.65f, 0.45f, 0.4f };
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float cx = sd * (hx - pw * 0.5f);
                for (int i = 0; i < k; i++)
                    Art.Column(mb, cx, 0f, pw * 0.5f, hz, i * sh, sh, 6, 1f, 0.84f, (i & 1) == 0 ? bark : barkD, true);
                var top = new Vector3(cx, Y, 0f);
                for (int f = 0; f < ups.Length; f++)
                {
                    var dir = new Vector3(-sd, ups[f], 0f).normalized;
                    float L = Mathf.Min(0.9f * X, 0.8f * Y) * lens[f] * rng.Range(0.85f, 1f);
                    float sag = Mathf.Min(0.35f * L, dir.y * L + 0.8f * lh);
                    mb.Frond(top, dir, L, w, 4, sag, Art.J(c.Prop, rng), 0.3f, 1f);
                }
                float nr = Mathf.Min(0.4f * pw, Mathf.Min(0.9f * hz, 0.4f * lh));
                Art.IcoIn(mb, new Vector3(cx - nr, Y - 2f * nr, -nr), new Vector3(cx + nr, Y, nr), nut, rng, 0.1f, 0f);
            }
        }

        static void StoneArch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, rise = Mathf.Min(0.06f, 0.25f * lh), e0 = Mathf.Min(0.012f, 0.15f * hz);
            Color sand = Art.Mix(c.Ground, c.PropAccent, 0.35f), glyph = Art.Vivid(c.Emissive, 0.5f, 1f);
            int courses = Count(Y - lh, 0.3f, 8);
            for (int sd = -1; sd <= 1; sd += 2)
                for (int j = 0; j < courses; j++)
                {
                    float y0 = (Y - lh) * j / courses, y1 = (Y - lh) * (j + 1) / courses, e = rng.Range(0f, 1f) * e0;
                    float xo = sd * (hx - rng.Range(0f, 0.5f) * e0), xi = sd * (hx - pw);
                    Art.BoxMM(mb, new Vector3(Mathf.Min(xo, xi), y0, -hz + e), new Vector3(Mathf.Max(xo, xi), y1, hz - e), Art.J(sand, rng), Art.Lighten(sand, 0.06f));
                }
            int nv = Mathf.Min(9, Count(X, 0.25f, 9) | 1);
            for (int i = 0; i < nv; i++)
            {
                float x0 = -hx + X * i / nv, x1 = i == nv - 1 ? hx : -hx + X * (i + 1) / nv;
                bool key = i == nv / 2;
                float e = key ? e0 : rng.Range(0f, 1f) * e0;
                Art.BoxMM(mb, new Vector3(x0, Y - lh, -hz + e), new Vector3(x1, key ? Y + rise : Y, hz - e), Art.J(key ? Art.Lighten(sand, 0.1f) : sand, rng));
                if (!key) continue;
                float gr = 0.3f * Mathf.Min(x1 - x0, lh);
                var gc = new Vector3((x0 + x1) * 0.5f, Y - lh * 0.5f, hz - e0 * 0.5f);
                Art.Ring(mb, gc, Vector3.forward, gr * 0.6f, gr, 12, glyph, 0.8f);
                Art.Disc(mb, gc, Vector3.forward, gr * 0.35f, 8, glyph, 0.9f);
            }
        }

        /// <summary>cabin_door, or frost_window when <paramref name="window"/>: a log frame, snow on the lintel, icicles under it.</summary>
        static void CabinDoor(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng, bool window)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, e = Mathf.Min(0.006f, 0.2f * hz), zf = hz - e, zs = hz - 0.4f * e;
            Color dark = Art.Darken(c.PropAccent, 0.3f), snow = Art.Snow(c), ice = Art.Lighten(Art.Mix(c.Horizon, Color.white, 0.4f), 0.2f), glow = Art.Vivid(c.Emissive, 0.5f, 1f);
            for (int sd = -1; sd <= 1; sd += 2)
                for (int p = 0; p < 2; p++)
                {
                    float xa = sd * (hx - pw * p * 0.5f), xb = sd * (hx - pw * (p + 1) * 0.5f);
                    Art.BoxMM(mb, new Vector3(Mathf.Min(xa, xb), 0f, -zf), new Vector3(Mathf.Max(xa, xb), Y - 0.3f * lh, zf), Art.J(c.PropAccent, rng));
                }
            Art.BoxMM(mb, new Vector3(-hx, Y - 0.75f * lh, -zf), new Vector3(hx, Y - 0.3f * lh, zf), dark);
            int nl = Count(X, 0.3f, 6);
            for (int i = 0; i < nl; i++)
                Art.IcoIn(mb, new Vector3(-hx + X * i / nl, Y - 0.32f * lh, -hz), new Vector3(-hx + X * (i + 1) / nl, Y, hz), Art.J(snow, rng), rng, 0.12f, 0.45f);
            int ni = Count(X, 0.12f, 10);
            float ir = Mathf.Min(0.015f, Mathf.Min(0.4f * hz, 0.3f * X / ni)), maxLen = 0.25f * lh - 0.006f;
            if (maxLen > 0.01f)
                for (int i = 0; i < ni; i++)
                {
                    float x = -hx + X * (i + 0.5f) / ni, len = maxLen * rng.Range(0.4f, 1f);
                    Art.Column(mb, x, 0f, ir, ir, Y - 0.75f * lh - len, len, 4, 0f, 1f, ice, false, 0.15f);
                }
            if (window)
            {
                float fr = Mathf.Min(0.06f, 0.35f * pw);
                for (int sd = -1; sd <= 1; sd += 2)
                    for (int i = 0; i < 3; i++)
                        Art.Disc(mb, new Vector3(sd * (hx - pw * 0.5f), Y * rng.Range(0.35f, 0.8f), zs), Vector3.forward, fr * rng.Range(0.6f, 1f), 6, ice, 0.3f);
            }
            else
            {
                float lx0 = hx - 0.8f * pw, lx1 = hx - 0.2f * pw, lhh = Mathf.Min(0.15f, Mathf.Min(1.2f * (lx1 - lx0), 0.25f * Y));
                float ly = Mathf.Min(0.55f * Y, Y - lh - 1.2f * lhh);
                Art.RectZ(mb, zs, lx0, lx1, ly, ly + lhh, 1f, glow, 0.9f);
                Art.RectZ(mb, zs, lx0 - 0.05f * pw, lx1 + 0.05f * pw, ly + lhh, ly + lhh * 1.2f, 1f, dark);
            }
        }

        static void Airlock(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, e = Mathf.Min(0.004f, 0.2f * hz), zf = hz - e, zs = zf + 0.5f * e;
            Color metal = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.45f), hazard = Art.Vivid(c.PropAccent, 0.7f, 0.9f), glow = c.Emissive;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float xo = sd * hx, xi = sd * (hx - pw);
                Art.BoxMM(mb, new Vector3(Mathf.Min(xo, xi), 0f, -zf), new Vector3(Mathf.Max(xo, xi), Y - lh, zf), metal);
                float sa = sd * (hx - 0.85f * pw), sb = sd * (hx - 0.65f * pw);
                for (int f = -1; f <= 1; f += 2)
                    Art.RectZ(mb, f * zs, Mathf.Min(sa, sb), Mathf.Max(sa, sb), 0.08f * Y, Y - lh - 0.06f * Y, f, glow, 1f);
                Art.Disc(mb, new Vector3(sd * (hx - 0.3f * pw), Y - lh - 0.1f * Y, zs), Vector3.forward, Mathf.Min(0.15f * pw, 0.03f), 8, new Color(0.3f, 1f, 0.5f), 1f);
            }
            Art.BoxMM(mb, new Vector3(-hx, Y - lh, -zf), new Vector3(hx, Y, zf), dark);
            float yb = Y - 0.85f * lh, yt = Y - 0.15f * lh, skew = (yt - yb) * 0.6f;
            int ns = Count(X, 0.12f, 16);
            float sw = Mathf.Max(0.002f, (X - 0.04f - skew) / ns * 0.5f);
            for (int i = 0; i < ns; i++)
            {
                float xa = -hx + 0.02f + 2f * sw * i;
                if (xa + sw + skew > hx) break;
                for (int f = -1; f <= 1; f += 2)
                    Art.QuadF(mb, new Vector3(xa, yb, f * zs), new Vector3(xa + sw, yb, f * zs), new Vector3(xa + sw + skew, yt, f * zs), new Vector3(xa + skew, yt, f * zs),
                              new Vector3(0f, 0f, f), hazard, 0.2f);
            }
        }

        static void Porthole(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, e = Mathf.Min(0.012f, 0.3f * hz), zf = hz - e;
            Color metal = Art.J(c.Prop, rng), bolt = Art.Lighten(c.Prop, 0.15f), glow = c.Emissive;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float xo = sd * hx, xi = sd * (hx - pw);
                Art.BoxMM(mb, new Vector3(Mathf.Min(xo, xi), 0f, -zf), new Vector3(Mathf.Max(xo, xi), Y - lh, zf), metal);
            }
            Art.BoxMM(mb, new Vector3(-hx, Y - lh, -zf), new Vector3(hx, Y, zf), Art.Darken(metal, 0.1f));
            float gr = Mathf.Min(0.35f * lh, 0.12f * X), rr = Mathf.Min(0.012f, Mathf.Min(0.12f * pw, 0.12f * lh)), dome = Mathf.Min(0.008f, 0.6f * e);
            int nr = Count(Y - lh, 0.15f, 12);
            for (int sd = -1; sd <= 1; sd += 2)
                for (int i = 0; i < nr; i++)
                    Art.Disc(mb, new Vector3(sd * (hx - pw * 0.5f), (Y - lh) * (i + 0.5f) / nr, zf + 0.001f), Vector3.forward, rr, 6, bolt, 0f, dome);
            int nt = Count(X, 0.15f, 12);
            for (int i = 0; i < nt; i++)
            {
                float x = -hx + X * (i + 0.5f) / nt;
                if (Mathf.Abs(x) < gr + rr * 2f) continue;
                Art.Disc(mb, new Vector3(x, Y - 0.2f * lh, zf + 0.001f), Vector3.forward, rr, 6, bolt, 0f, dome);
            }
            var gc = new Vector3(0f, Y - 0.55f * lh, zf + 0.002f);
            Art.Ring(mb, gc, Vector3.forward, gr * 0.75f, gr, 16, bolt, 0.1f);
            Art.Disc(mb, gc, Vector3.forward, gr * 0.75f, 12, glow, 0.8f);
        }

        static void CoralArch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, rise = Mathf.Min(0.35f, 0.2f * Y);
            Color a = Art.J(c.Prop, rng), b = Art.J(c.PropAccent, rng), glow = Art.Vivid(c.Emissive, 0.4f, 1f);
            float[] t = { 0f, 0.5f, 1f }, sr = { 0.85f, 1f, 0.9f };
            int k = Count(Y - lh, 0.35f, 6);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float cx = sd * (hx - pw * 0.5f), top = Y - lh * 0.5f;
                for (int j = 0; j < k; j++)
                    Art.Lump(mb, cx, 0f, pw * 0.5f, hz, top * j / k, top * (j + 1) / k, t, sr, 8, (j & 1) == 0 ? a : b, Art.Lighten(a, 0.2f), rng, 0.12f);
                for (int i = 0; i < 3; i++)
                {
                    float py = Y * rng.Range(0.15f, 0.75f);
                    Art.IcoIn(mb, new Vector3(cx - pw * 0.15f, py, hz * 0.55f), new Vector3(cx + pw * 0.15f, py + 0.3f * pw, hz * 0.95f), glow, rng, 0.1f, 0f, 0.8f);
                }
            }
            int n = Count(X, 0.3f, 8);
            for (int i = 0; i < n; i++)
            {
                float x0 = -hx + X * i / n, x1 = i == n - 1 ? hx : -hx + X * (i + 1) / n, ov = 0.2f * (x1 - x0);
                Art.IcoIn(mb, new Vector3(Mathf.Max(-hx, x0 - ov), Y - lh, -hz), new Vector3(Mathf.Min(hx, x1 + ov), Y + 0.3f * rise * rng.Range(0.5f, 1f), hz),
                          (i & 1) == 0 ? a : b, rng, 0.15f, 0f);
            }
            float br = Mathf.Min(0.06f, Mathf.Min(0.8f * hz, 0.05f * X));
            for (int i = 0; i < 3; i++)
            {
                float x = Mathf.Lerp(-hx + br, hx - br, (i + 0.5f) / 3f + rng.Range(-0.08f, 0.08f));
                x = Mathf.Clamp(x, -hx + br, hx - br);
                mb.Cylinder(new Vector3(x, Y - 0.3f * lh, 0f), br, br * 0.4f, rise * rng.Range(0.7f, 1.1f), 5, a, capTop: false, emissive: 0.1f, windTop: 0.3f);
                mb.Cylinder(new Vector3(x, Y - 0.3f * lh + rise * 1.1f, 0f), br * 0.4f, 0f, br * 1.2f, 5, glow, emissive: 0.8f, windBase: 0.3f, windTop: 0.35f);
            }
        }

        /// <summary>club_door, or neon_window when <paramref name="window"/>: a dark frame with brass trim and neon tubes.</summary>
        static void ClubDoor(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng, bool window)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, e = Mathf.Min(0.006f, 0.2f * hz), zf = hz - e, zs = zf + 0.5f * e;
            Color frame = window ? Art.Mix(new Color(0.5f, 0.23f, 0.18f), c.PropAccent, 0.3f) : Art.Darken(c.PropAccent, 0.45f);
            Color brass = Art.Lighten(c.PropAccent, 0.15f), neon = c.Neon;
            float tw = Mathf.Min(0.02f, 0.15f * pw);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float xo = sd * hx, xi = sd * (hx - pw);
                Art.BoxMM(mb, new Vector3(Mathf.Min(xo, xi), 0f, -zf), new Vector3(Mathf.Max(xo, xi), Y - lh, zf), Art.J(frame, rng));
                Art.BoxMM(mb, new Vector3(Mathf.Min(xi, xi + sd * tw), 0f, -hz), new Vector3(Mathf.Max(xi, xi + sd * tw), Y - lh, hz), brass);
                float na = sd * (hx - 0.6f * pw), nb = sd * (hx - 0.45f * pw);
                for (int f = -1; f <= 1; f += 2)
                    Art.RectZ(mb, f * zs, Mathf.Min(na, nb), Mathf.Max(na, nb), 0.05f * Y, Y - lh - 0.02f * Y, f, neon, 1f);
            }
            Art.BoxMM(mb, new Vector3(-hx, Y - lh, -zf), new Vector3(hx, Y, zf), frame);
            Art.RectZ(mb, zs, -hx + pw, hx - pw, Y - 0.9f * lh, Y - 0.78f * lh, 1f, neon, 1f);
            if (window)
            {
                // a neon cocktail glass on the lintel
                float g = Mathf.Min(0.35f * lh, 0.15f * X), yc = Y - 0.55f * lh;
                Art.TriF(mb, new Vector3(-g * 0.5f, yc + g * 0.3f, zs), new Vector3(g * 0.5f, yc + g * 0.3f, zs), new Vector3(0f, yc - g * 0.15f, zs), Vector3.forward, neon, 1f);
                Art.RectZ(mb, zs, -g * 0.04f, g * 0.04f, yc - 0.4f * g, yc - 0.15f * g, 1f, neon, 1f);
                Art.RectZ(mb, zs, -g * 0.25f, g * 0.25f, yc - 0.45f * g, yc - 0.4f * g, 1f, neon, 1f);
                return;
            }
            // marquee bulbs along the lintel
            int nb2 = Count(X, 0.1f, 14);
            float bs = Mathf.Min(0.015f, Mathf.Min(0.2f * lh, 0.3f * X / nb2)), by = Y - 0.55f * lh;
            for (int i = 0; i < nb2; i++)
            {
                float x = -hx + X * (i + 0.5f) / nb2;
                Art.BoxMM(mb, new Vector3(x - bs, by - bs, zf), new Vector3(x + bs, by + bs, hz), c.Emissive, null, 1f);
            }
        }

        static void HedgeArch(MeshBuilder mb, Vector3 s, WorldColors c, Rng rng)
        {
            Dims(s, out float X, out float Y, out _, out float hx, out float hz, out _);
            float pw = 0.149f * X, lh = 0.149f * Y, rise = Mathf.Min(0.3f, 0.15f * Y);
            Color dark = Art.Darken(c.Prop, 0.12f), centre = Art.Vivid(c.Emissive, 0.6f, 0.95f);
            int k = Count(Y, Mathf.Max(0.2f, 1.2f * pw), 8);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float x0 = sd > 0 ? hx - pw : -hx, x1 = sd > 0 ? hx : -hx + pw;
                for (int j = 0; j < k; j++)
                {
                    float y0 = Mathf.Max(0f, Y * j / k - 0.03f * Y), y1 = Mathf.Min(Y, Y * (j + 1) / k + 0.03f * Y);
                    Art.IcoIn(mb, new Vector3(x0, y0, -hz), new Vector3(x1, y1, hz), Art.J((j & 1) == 0 ? c.Prop : dark, rng), rng, 0.15f, 0f, 0f, 0.1f, 0.3f);
                }
            }
            int n = Count(X, 0.3f, 8);
            for (int i = 0; i < n; i++)
            {
                float x0 = -hx + X * i / n, x1 = i == n - 1 ? hx : -hx + X * (i + 1) / n, ov = 0.2f * (x1 - x0);
                float mid = 1f - Mathf.Abs((i + 0.5f) / n - 0.5f) * 2f;
                Art.IcoIn(mb, new Vector3(Mathf.Max(-hx, x0 - ov), Y - lh, -hz), new Vector3(Mathf.Min(hx, x1 + ov), Y + rise * (0.3f + 0.7f * mid), hz),
                          Art.J(c.Prop, rng), rng, 0.15f, 0f, 0f, 0.2f, 0.5f);
            }
            Color pc = Petal(rng);
            float fr = Mathf.Min(0.03f, Mathf.Min(0.15f * pw, 0.15f * lh)), fz = hz - Mathf.Min(0.01f, 0.2f * hz);
            for (int i = 0; i < 10; i++)
            {
                float fx, fy;
                if (i >= 6) { fx = rng.Range(-hx + fr, hx - fr); fy = Y - lh * rng.Range(0.3f, 0.7f); }
                else { float sd = (i & 1) == 0 ? -1f : 1f; fx = sd * (hx - pw * rng.Range(0.3f, 0.7f)); fy = rng.Range(0.1f, 0.8f) * Y; }
                Art.Disc(mb, new Vector3(fx, fy, fz), Vector3.forward, fr, 5, Art.J(pc, rng), 0.2f);
                Art.Disc(mb, new Vector3(fx, fy, fz + 0.001f), Vector3.forward, fr * 0.35f, 5, centre, 0.4f);
            }
        }
    }
}

using System;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Shared helpers for the art library (<see cref="Props"/>, <see cref="Skins"/>, <see cref="Builds"/>):
    /// colour tweaks, faces that orient themselves, and primitives that stay inside a given box.
    /// </summary>
    internal static class Art
    {
        /// <summary>Largest |component| of a unit icosahedron vertex in <see cref="MeshBuilder.Ico"/> (1.618 / 1.902).</summary>
        public const float IcoK = 0.8507f;
        const float Tau = Mathf.PI * 2f;

        public static string Norm(string id) => string.IsNullOrEmpty(id) ? "" : id.Trim().ToLowerInvariant().Replace(' ', '_');

        // ---------- colour ----------

        public static Color J(Color c, Rng rng) => WorldColors.Jitter(c, rng, 0.08f);
        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);
        public static Color Lighten(Color a, float t) => Color.Lerp(a, new Color(1f, 1f, 1f, a.a), t);
        public static Color Darken(Color a, float t) { float f = 1f - t; return new Color(a.r * f, a.g * f, a.b * f, a.a); }
        public static Color Dull(Color a, float t)
        {
            float g = a.r * 0.3f + a.g * 0.59f + a.b * 0.11f;
            return Color.Lerp(a, new Color(g, g, g, a.a), t);
        }
        /// <summary>Raises saturation and value to at least the given amounts (for gatherables: "pick me").</summary>
        public static Color Vivid(Color a, float minSat, float minVal)
        {
            Color.RGBToHSV(a, out float h, out float s, out float v);
            var o = Color.HSVToRGB(h, Mathf.Clamp01(Mathf.Max(s, minSat)), Mathf.Clamp01(Mathf.Max(v, minVal)));
            o.a = a.a;
            return o;
        }
        public static Color Stone(WorldColors c) => Dull(Mix(c.GroundAccent, c.Ambient, 0.35f), 0.3f);
        public static Color Snow(WorldColors c) => Mix(Lighten(c.Ground, 0.3f), new Color(0.9f, 0.95f, 1f), 0.4f);
        public static Color Moss(WorldColors c) => Mix(c.GroundAccent, c.Prop, 0.35f);
        public static Color Metal(WorldColors c) => Dull(Mix(c.Prop, new Color(0.55f, 0.56f, 0.6f), 0.4f), 0.3f);

        // ---------- faces that orient themselves ----------

        /// <summary>A triangle whose front faces <paramref name="facing"/> (whatever order the corners come in).</summary>
        public static void TriF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col, float em = 0f,
                                float wa = 0f, float wb = 0f, float wc = 0f)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) >= 0f) mb.Tri(a, b, c, col, em, wa, wb, wc);
            else mb.Tri(a, c, b, col, em, wa, wc, wb);
        }

        /// <summary>A quad a-b-c-d (corners in ring order, either way round) facing <paramref name="facing"/>.</summary>
        public static void QuadF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color col, float em = 0f,
                                 float wa = 0f, float wb = 0f, float wc = 0f, float wd = 0f)
        {
            TriF(mb, a, b, c, facing, col, em, wa, wb, wc);
            TriF(mb, a, c, d, facing, col, em, wa, wc, wd);
        }

        /// <summary>A rectangle centred on <paramref name="c"/> facing <paramref name="n"/>: <paramref name="w"/> along <paramref name="r"/>, <paramref name="h"/> across.</summary>
        public static void Rect(MeshBuilder mb, Vector3 c, Vector3 n, Vector3 r, float w, float h, Color col, float em = 0f)
        {
            Vector3 u = Vector3.Cross(r, n).normalized * (h * 0.5f), s = r.normalized * (w * 0.5f);
            QuadF(mb, c - s - u, c + s - u, c + s + u, c - s + u, n, col, em);
        }

        /// <summary>A rectangle on a plane of constant z facing +Z (sign 1) or -Z, spanning x0..x1 and y0..y1.</summary>
        public static void RectZ(MeshBuilder mb, float z, float x0, float x1, float y0, float y1, float sign, Color col, float em = 0f)
            => QuadF(mb, new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), new Vector3(0f, 0f, sign), col, em);

        /// <summary>A rectangle on a plane of constant x facing +X (sign 1) or -X, spanning z0..z1 and y0..y1.</summary>
        public static void RectX(MeshBuilder mb, float x, float z0, float z1, float y0, float y1, float sign, Color col, float em = 0f)
            => QuadF(mb, new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), new Vector3(sign, 0f, 0f), col, em);

        /// <summary>A rectangle on a plane of constant y facing up (sign 1) or down, spanning x0..x1 and z0..z1.</summary>
        public static void RectY(MeshBuilder mb, float y, float x0, float x1, float z0, float z1, float sign, Color col, float em = 0f)
            => QuadF(mb, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), new Vector3(0f, sign, 0f), col, em);

        /// <summary>The four side walls (and optionally the underside) of a slab, without its top.</summary>
        public static void Walls(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float y1, Color col, float em = 0f, bool bottom = false)
        {
            RectZ(mb, z1, x0, x1, y0, y1, 1f, col, em);
            RectZ(mb, z0, x0, x1, y0, y1, -1f, col, em);
            RectX(mb, x1, z0, z1, y0, y1, 1f, col, em);
            RectX(mb, x0, z0, z1, y0, y1, -1f, col, em);
            if (bottom) RectY(mb, y0, x0, x1, z0, z1, -1f, col, em);
        }

        public static void BoxMM(MeshBuilder mb, Vector3 min, Vector3 max, Color col, Color? top = null, float em = 0f, bool bottom = false)
            => mb.Box((min + max) * 0.5f, max - min, col, top, em, bottom);

        /// <summary>A convex six-faced solid from 8 corners: 0-3 the bottom ring, 4-7 the top ring in the same order.</summary>
        public static void Hexa(MeshBuilder mb, Vector3[] p, Color col, Color? top = null, float em = 0f)
        {
            Vector3 ctr = Vector3.zero;
            for (int i = 0; i < 8; i++) ctr += p[i];
            ctr /= 8f;
            Face(mb, p[0], p[1], p[2], p[3], ctr, col, em);
            Face(mb, p[4], p[5], p[6], p[7], ctr, top ?? col, em);
            Face(mb, p[0], p[1], p[5], p[4], ctr, col, em);
            Face(mb, p[1], p[2], p[6], p[5], ctr, col, em);
            Face(mb, p[2], p[3], p[7], p[6], ctr, col, em);
            Face(mb, p[3], p[0], p[4], p[7], ctr, col, em);
        }

        static void Face(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 ctr, Color col, float em)
            => QuadF(mb, a, b, c, d, (a + b + c + d) * 0.25f - ctr, col, em);

        /// <summary>A flat disc (or a shallow cone/dimple with <paramref name="apex"/> ≠ 0) facing any direction.</summary>
        public static void Disc(MeshBuilder mb, Vector3 c, Vector3 n, float r, int sides, Color col, float em = 0f, float apex = 0f)
        {
            n.Normalize();
            Basis(n, out Vector3 u, out Vector3 v);
            Vector3 top = c + n * apex;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Tau / sides, a1 = (k + 1) * Tau / sides;
                TriF(mb, top, c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r, c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r, n, col, em);
            }
        }

        /// <summary>A flat ring (annulus) facing <paramref name="n"/>; <paramref name="twoSided"/> also faces it the other way.</summary>
        public static void Ring(MeshBuilder mb, Vector3 c, Vector3 n, float r0, float r1, int sides, Color col, float em = 0f, bool twoSided = false)
        {
            n.Normalize();
            Basis(n, out Vector3 u, out Vector3 v);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Tau / sides, a1 = (k + 1) * Tau / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                QuadF(mb, c + d0 * r0, c + d0 * r1, c + d1 * r1, c + d1 * r0, n, col, em);
                if (twoSided) QuadF(mb, c + d0 * r0, c + d0 * r1, c + d1 * r1, c + d1 * r0, -n, col, em);
            }
        }

        public static void Basis(Vector3 n, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(n, u);
        }

        /// <summary>
        /// A jittered icosahedron that stays inside the box min..max (whatever the jitter), resting on min.y when
        /// <paramref name="flatten"/> &gt; 0.
        /// </summary>
        public static void IcoIn(MeshBuilder mb, Vector3 min, Vector3 max, Color col, Rng rng, float jitter = 0.18f, float flatten = 0.45f,
                                 float em = 0f, float windBase = 0f, float windTop = 0f)
        {
            float k = IcoK * (1f + jitter) * 1.003f;
            Vector3 s = max - min;
            float rx = s.x * 0.5f / k, rz = s.z * 0.5f / k, ry, cy;
            if (flatten > 0f) { ry = s.y / (flatten + k); cy = min.y + flatten * ry; }
            else { ry = s.y * 0.5f / k; cy = (min.y + max.y) * 0.5f; }
            mb.Ico(new Vector3((min.x + max.x) * 0.5f, cy, (min.z + max.z) * 0.5f), new Vector3(rx, ry, rz), col, rng, jitter, em, windBase, windTop, flatten);
        }

        /// <summary>An upright prism scaled to an ellipse of half-widths ex, ez (radius fractions rBot/rTop ≤ 1 stay inside it).</summary>
        public static void Column(MeshBuilder mb, float cx, float cz, float ex, float ez, float y0, float h, int sides, float rBot, float rTop, Color col,
                                  bool capTop = true, float em = 0f, float windBase = 0f, float windTop = 0f, float angle = 0f, Color? cap = null, bool capBottom = false)
        {
            mb.Push(new Vector3(cx, y0, cz), Quaternion.identity, new Vector3(ex, 1f, ez));
            mb.Cylinder(Vector3.zero, rBot, rTop, h, sides, col, capTop, capBottom, em, windBase, windTop, angle, cap);
            mb.Pop();
        }

        /// <summary>A tube through <paramref name="pts"/> (rings exactly radii[i] from each point), with a twist-free frame.</summary>
        public static void Tube(MeshBuilder mb, Vector3[] pts, float[] radii, int sides, Color col, float[] wind = null, float em = 0f,
                                bool capStart = false, bool capEnd = false, Color? capCol = null)
        {
            int n = pts.Length;
            if (n < 2) return;
            sides = Mathf.Clamp(sides, 3, 16);
            var prev = new Vector3[sides];
            var cur = new Vector3[sides];
            Vector3 u = Vector3.right;
            for (int i = 0; i < n; i++)
            {
                Vector3 axis = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
                if (axis.sqrMagnitude < 1e-12f) axis = Vector3.up;
                axis.Normalize();
                Vector3 refV = Mathf.Abs(axis.z) < 0.95f ? Vector3.forward : Vector3.right;
                if (i == 0) u = Vector3.Cross(axis, refV);
                else
                {
                    u -= axis * Vector3.Dot(u, axis);
                    if (u.sqrMagnitude < 1e-8f) u = Vector3.Cross(axis, refV);
                }
                u.Normalize();
                Vector3 v = Vector3.Cross(u, axis);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Tau / sides;
                    cur[k] = pts[i] + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radii[i];
                }
                float wl = wind != null ? wind[Mathf.Max(0, i - 1)] : 0f, wh = wind != null ? wind[i] : 0f;
                if (i > 0)
                {
                    Vector3 mid = (pts[i - 1] + pts[i]) * 0.5f;
                    for (int k = 0; k < sides; k++)
                    {
                        int k1 = (k + 1) % sides;
                        Vector3 f = (prev[k] + cur[k1]) * 0.5f - mid;
                        TriF(mb, prev[k], cur[k], cur[k1], f, col, em, wl, wh, wh);
                        TriF(mb, prev[k], cur[k1], prev[k1], f, col, em, wl, wh, wl);
                    }
                }
                if (i == 0 && capStart) Fan(mb, cur, pts[0], -axis, capCol ?? col, em, wh);
                if (i == n - 1 && capEnd) Fan(mb, cur, pts[i], axis, capCol ?? col, em, wh);
                var t = prev; prev = cur; cur = t;
            }
        }

        static void Fan(MeshBuilder mb, Vector3[] ring, Vector3 c, Vector3 n, Color col, float em, float w)
        {
            for (int k = 0; k < ring.Length; k++) TriF(mb, c, ring[k], ring[(k + 1) % ring.Length], n, col, em, w, w, w);
        }

        /// <summary>
        /// A rock-like lump: rings at heights t[i] (0–1 of y0..y1) with radius fractions s[i] ≤ 1 of the ellipse ex, ez
        /// (jitter only shrinks), and a flat top at y1 when <paramref name="capTop"/>.
        /// </summary>
        public static void Lump(MeshBuilder mb, float cx, float cz, float ex, float ez, float y0, float y1, float[] t, float[] s, int sides,
                                Color col, Color top, Rng rng, float jitter, float em = 0f, bool capTop = true)
        {
            int n = t.Length;
            var ring = new Vector3[n * sides];
            float off = rng.Range(0f, Tau);
            for (int i = 0; i < n; i++)
                for (int k = 0; k < sides; k++)
                {
                    float a = off + k * Tau / sides, f = s[i] * (1f - jitter * rng.Next01());
                    ring[i * sides + k] = new Vector3(cx + Mathf.Cos(a) * ex * f, Mathf.Lerp(y0, y1, t[i]), cz + Mathf.Sin(a) * ez * f);
                }
            for (int i = 0; i + 1 < n; i++)
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    Vector3 a = ring[i * sides + k], b = ring[(i + 1) * sides + k], c = ring[(i + 1) * sides + k1], d = ring[i * sides + k1];
                    Vector3 f = (a + b + c + d) * 0.25f - new Vector3(cx, 0f, cz);
                    f.y = 0f;
                    QuadF(mb, a, b, c, d, f, (i & 1) == 0 ? col : Darken(col, 0.06f), em);
                }
            if (!capTop) return;
            Vector3 ctr = new Vector3(cx, y1, cz);
            for (int k = 0; k < sides; k++)
                TriF(mb, ctr, ring[(n - 1) * sides + k], ring[(n - 1) * sides + (k + 1) % sides], Vector3.up, top, em);
        }

        /// <summary>A ribbed fan (scallop) shell, <paramref name="size"/> wide, hinge towards -Z, lying on y = 0.</summary>
        public static void Shell(MeshBuilder mb, Vector3 pos, float yawDeg, float size, Color ridge, Color valley, float em)
        {
            float k = size / 1.45f;
            mb.Push(pos, Quaternion.Euler(0f, yawDeg, 0f), new Vector3(k, k, k));
            const int N = 6, R = 2 * N + 1;
            var top = new Vector3[R];
            var mid = new Vector3[R];
            var bot = new Vector3[R];
            Vector3 P = new Vector3(0f, 0f, -0.35f), H = new Vector3(0f, 0.16f, -0.35f);
            for (int i = 0; i < R; i++)
            {
                float th = Mathf.Deg2Rad * (-75f + 150f * i / (R - 1f));
                bool rv = (i & 1) == 1;
                Vector3 d = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                top[i] = P + d * (rv ? 0.75f : 0.71f) + Vector3.up * (rv ? 0.1f : 0.05f);
                mid[i] = P + d * 0.4f + Vector3.up * (rv ? 0.24f : 0.2f);
                bot[i] = P + d * (rv ? 0.75f : 0.71f);
            }
            for (int i = 0; i < R - 1; i++)
            {
                Color col = (i & 1) == 0 ? valley : ridge;
                TriF(mb, H, mid[i], mid[i + 1], Vector3.up, col, em);
                QuadF(mb, mid[i], top[i], top[i + 1], mid[i + 1], Vector3.up, col, em);
                TriF(mb, P, bot[i + 1], bot[i], Vector3.down, valley, em);
                Vector3 o = (top[i] + top[i + 1]) * 0.5f - P;
                o.y = 0f;
                QuadF(mb, top[i], bot[i], bot[i + 1], top[i + 1], o, col, em);
            }
            for (int e = 0; e < 2; e++)
            {
                int i = e == 0 ? 0 : R - 1;
                float th = Mathf.Deg2Rad * (e == 0 ? -75f : 75f);
                Vector3 side = new Vector3(Mathf.Cos(th), 0f, -Mathf.Sin(th)) * (e == 0 ? -1f : 1f);
                TriF(mb, P, H, mid[i], side, valley, em);
                TriF(mb, P, mid[i], top[i], side, valley, em);
                TriF(mb, P, top[i], bot[i], side, valley, em);
            }
            mb.Box(new Vector3(0f, 0.05f, -0.38f), new Vector3(0.38f, 0.1f, 0.12f), valley, ridge, em);
            mb.Pop();
        }

        /// <summary>Sets the fake-AO base to the current origin (if the caller hasn't set one) for a using block.</summary>
        public readonly struct Ao : IDisposable
        {
            readonly MeshBuilder mb;
            readonly float baseY, height;
            readonly bool set;

            public Ao(MeshBuilder mb, float height)
            {
                this.mb = mb;
                baseY = mb.AoBaseY;
                this.height = mb.AoHeight;
                set = float.IsNaN(mb.AoBaseY);
                if (!set) return;
                mb.AoBaseY = mb.Current.MultiplyPoint3x4(Vector3.zero).y;
                mb.AoHeight = Mathf.Max(0.005f, height);
            }

            public void Dispose()
            {
                if (!set || mb == null) return;
                mb.AoBaseY = baseY;
                mb.AoHeight = height;
            }
        }
    }

    /// <summary>
    /// The world's props, items and extras, built from primitives at the local origin (base at y = 0, facing +Z).
    /// Scatter props are background (least saturated); gatherable items are the most saturated and glow a little.
    /// </summary>
    public static class Props
    {
        public static bool Build(MeshBuilder mb, string kind, WorldColors c, Rng rng, float scale = 1f)
        {
            if (mb == null) return false;
            string k = Art.Norm(kind);
            float h = Height(k);
            if (h <= 0f) return false;
            rng ??= new Rng(1);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) scale = 1f;
            mb.Push(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, scale));
            try
            {
                using (new Art.Ao(mb, 0.15f * h * scale))
                    if (!ModelKit.Build(mb, k, c, rng, h)) Dispatch(mb, k, c, rng);
            }
            finally { mb.Pop(); }
            return true;
        }

        /// <summary>Footprint radius in metres at scale 1 (for spacing); 0 for an unknown kind.</summary>
        public static float Radius(string kind) => Art.Norm(kind) switch
        {
            "pine" => 1.7f, "snow_pine" => 1.7f, "rock" => 0.9f, "snow_rock" => 0.9f, "reef_rock" => 0.9f, "shell_rock" => 0.8f,
            "fern" => 0.7f, "palm" => 2.5f, "cactus" => 0.7f, "dune_grass" => 0.4f, "ice" => 0.8f,
            "antenna" => 0.8f, "panel" => 1.6f, "tank" => 1.3f, "light_pylon" => 0.45f,
            "coral" => 0.7f, "kelp" => 0.6f, "building" => 6.2f, "street_lamp" => 0.3f, "hydrant" => 0.25f,
            "oak" => 2.6f, "bush" => 0.7f, "flower_patch" => 0.7f, "stone" => 0.1f, "log_small" => 0.22f,
            "lantern" => 0.15f, "crystal_cluster" => 0.5f, "flower" => 0.1f, "cloud" => 3.3f,
            "kindling" => 0.06f, "shell" => 0.05f, "sun_shard" => 0.05f, "snowball" => 0.05f, "energy_cell" => 0.03f,
            "pearl" => 0.05f, "record" => 0.08f, "seed" => 0.03f,
            // Set dressing from the model kit only.
            "mushroom" => 0.25f, "stump" => 0.45f, "tent" => 1.6f, "log_stack" => 0.8f,
            "lantern_hanging" => 0.2f, "rover" => 1.4f, "crate_space" => 0.8f, "console" => 0.7f, "bar_stool" => 0.25f,
            "lounge_chair" => 0.5f, "speaker" => 0.3f, "potted_plant" => 0.35f,
            "fall_tree" => 2.4f, "crater" => 2.2f, "crystal_rock" => 0.9f, "meteor_rock" => 0.9f, "pumpkin" => 0.4f, "corn" => 0.35f,
            "fence" => 0.9f, "canoe" => 1.6f, "lily" => 0.4f, "parasol" => 1f, "cone" => 0.2f, "traffic_light" => 0.4f, "present" => 0.3f,
            "candy_cane" => 0.25f, "bookcase" => 0.7f, "floor_lamp" => 0.3f, "teddy" => 0.3f, "radio" => 0.2f, "generator" => 0.9f, "barrels" => 0.6f,
            _ => 0f
        };

        /// <summary>Rough height in metres at scale 1 (for fake AO); 0 for an unknown kind.</summary>
        static float Height(string k) => k switch
        {
            "pine" => 6.5f, "snow_pine" => 6.5f, "rock" => 1f, "snow_rock" => 1f, "reef_rock" => 1f, "shell_rock" => 0.7f,
            "fern" => 0.6f, "palm" => 6f, "cactus" => 2.4f, "dune_grass" => 0.7f, "ice" => 1.6f,
            "antenna" => 3f, "panel" => 1.6f, "tank" => 3f, "light_pylon" => 4.2f,
            "coral" => 1f, "kelp" => 3f, "building" => 13f, "street_lamp" => 3.5f, "hydrant" => 0.7f,
            "oak" => 6f, "bush" => 0.9f, "flower_patch" => 0.35f, "stone" => 0.05f, "log_small" => 0.12f,
            "lantern" => 0.5f, "crystal_cluster" => 0.8f, "flower" => 0.25f, "cloud" => 2.5f,
            "kindling" => 0.03f, "shell" => 0.02f, "sun_shard" => 0.09f, "snowball" => 0.08f, "energy_cell" => 0.09f,
            "pearl" => 0.06f, "record" => 0.01f, "seed" => 0.08f,
            _ => KitHeight(k)
        };

        /// <summary>Kinds only the model kit has (0 when it is off or missing, so they are simply not placed).</summary>
        static float KitHeight(string k)
        {
            float h = k switch
            {
                "mushroom" => 0.35f, "stump" => 0.6f, "tent" => 2.2f, "log_stack" => 0.9f,
                "lantern_hanging" => 0.6f, "rover" => 1.8f, "crate_space" => 1.3f, "console" => 1.4f, "bar_stool" => 0.8f,
                "lounge_chair" => 0.9f, "speaker" => 1.3f, "potted_plant" => 1.2f,
                "fall_tree" => 6f, "crater" => 0.8f, "crystal_rock" => 1.4f, "meteor_rock" => 1.2f, "pumpkin" => 0.45f, "corn" => 1.9f,
                "fence" => 0.9f, "canoe" => 0.5f, "lily" => 0.12f, "parasol" => 2.4f, "cone" => 0.6f, "traffic_light" => 3.2f, "present" => 0.5f,
                "candy_cane" => 1.4f, "bookcase" => 1.9f, "floor_lamp" => 1.6f, "teddy" => 0.6f, "radio" => 0.35f, "generator" => 1.6f, "barrels" => 1.1f,
                _ => 0f
            };
            if (h <= 0f && MadeKinds.Has(k)) h = 1f;   // a thing to make: the maker scales it to the size asked for
            return h > 0f && ModelKit.Has(k) ? h : 0f;
        }

        static void Dispatch(MeshBuilder mb, string k, WorldColors c, Rng rng)
        {
            switch (k)
            {
                case "pine": Pine(mb, c, rng, false); break;
                case "snow_pine": Pine(mb, c, rng, true); break;
                case "rock": Rock(mb, Art.Stone(c), rng, 0.5f, 2f, null); break;
                case "snow_rock": Rock(mb, Art.Stone(c), rng, 0.5f, 2f, Art.Snow(c)); break;
                case "reef_rock": ReefRock(mb, c, rng); break;
                case "shell_rock": ShellRock(mb, c, rng); break;
                case "fern": Fern(mb, c, rng); break;
                case "palm": Palm(mb, c, rng); break;
                case "cactus": Cactus(mb, c, rng); break;
                case "dune_grass": DuneGrass(mb, c, rng); break;
                case "ice": Shards(mb, Art.Mix(Art.Lighten(c.Horizon, 0.45f), c.GroundAccent, 0.2f), rng, 3, 0.6f, 2.4f, 0.12f, false, c); break;
                case "antenna": Antenna(mb, c, rng); break;
                case "panel": Panel(mb, c, rng); break;
                case "tank": Tank(mb, c, rng); break;
                case "light_pylon": Pylon(mb, c, rng); break;
                case "coral": Coral(mb, c, rng); break;
                case "kelp": Kelp(mb, c, rng); break;
                case "building": Building(mb, c, rng); break;
                case "street_lamp": StreetLamp(mb, c, rng); break;
                case "hydrant": Hydrant(mb, c); break;
                case "oak": Oak(mb, c, rng); break;
                case "bush": Bush(mb, c, rng); break;
                case "flower_patch": FlowerPatch(mb, c, rng); break;
                case "stone": Stone(mb, c, rng); break;
                case "log_small": LogSmall(mb, c, rng); break;
                case "lantern": Lantern(mb, c); break;
                case "crystal_cluster": Shards(mb, Art.Vivid(c.Emissive, 0.55f, 0.95f), rng, 4, 0.3f, 0.75f, 0.6f, true, c); break;
                case "flower": Flower(mb, c, rng); break;
                case "cloud": Cloud(mb, c, rng); break;
                case "kindling": Kindling(mb, c, rng); break;
                case "shell": Art.Shell(mb, Vector3.zero, 0f, 0.085f, ShellColor(c), Art.Lighten(ShellColor(c), 0.35f), 0.35f); break;
                case "sun_shard": SunShard(mb, c); break;
                case "snowball": Snowball(mb, c, rng); break;
                case "energy_cell": EnergyCell(mb, c); break;
                case "pearl": Pearl(mb, c, rng, 1f); break;
                case "record": Record(mb, c); break;
                case "seed": Seed(mb, c, rng); break;
            }
        }

        internal static Color ShellColor(WorldColors c) => Art.Vivid(Art.Mix(new Color(1f, 0.55f, 0.5f), c.Horizon, 0.4f), 0.55f, 0.95f);

        // ---------- natural scatter ----------

        static void Pine(MeshBuilder mb, WorldColors c, Rng rng, bool snow)
        {
            float H = rng.Range(5f, 8f), tr = 0.03f * H;
            Color bark = Art.J(Art.Darken(c.PropAccent, 0.1f), rng), snowC = Art.Snow(c);
            mb.Cylinder(Vector3.zero, tr, tr * 0.7f, 0.3f * H, 6, bark, capTop: false, windTop: 0.3f);
            float[] b = { 0.16f, 0.4f, 0.62f }, h = { 0.44f, 0.36f, 0.38f }, r = { 0.24f, 0.18f, 0.12f };
            for (int i = 0; i < 3; i++)
            {
                float y0 = b[i] * H, hh = h[i] * H, rr = r[i] * H * rng.Range(0.9f, 1.08f), ang = rng.Range(0f, 1.05f);
                mb.Cylinder(new Vector3(0f, y0, 0f), rr, 0f, hh, 6, Art.J(c.Prop, rng), capBottom: true,
                            windBase: y0 / H, windTop: (y0 + hh) / H, angleOffset: ang);
                if (!snow) continue;
                float sy = y0 + hh * 0.5f;
                mb.Cylinder(new Vector3(0f, sy, 0f), rr * 0.5f + 0.04f, 0f, hh * 0.5f + 0.05f, 6, Art.J(snowC, rng), capBottom: true,
                            windBase: sy / H, windTop: Mathf.Min(1f, (y0 + hh + 0.05f) / H), angleOffset: ang);
            }
        }

        static void Rock(MeshBuilder mb, Color col, Rng rng, float sMin, float sMax, Color? cap)
        {
            float s = rng.Range(sMin, sMax);
            float w = s * rng.Range(0.5f, 0.6f), d = s * rng.Range(0.4f, 0.5f), h = s * rng.Range(0.55f, 0.75f);
            Art.IcoIn(mb, new Vector3(-w, 0f, -d), new Vector3(w, h, d), Art.J(col, rng), rng, 0.2f, 0.4f);
            if (cap.HasValue)
                Art.IcoIn(mb, new Vector3(-w * 0.78f, h * 0.55f, -d * 0.78f), new Vector3(w * 0.78f, h * 1.06f, d * 0.78f), Art.J(cap.Value, rng), rng, 0.12f, 0.5f);
            if (rng.Next01() < 0.65f)
            {
                float t = s * rng.Range(0.3f, 0.45f), ox = w * rng.Range(0.7f, 1f) * (rng.Next01() < 0.5f ? -1f : 1f), oz = d * rng.Range(-0.6f, 0.6f);
                Art.IcoIn(mb, new Vector3(ox - t * 0.5f, 0f, oz - t * 0.5f), new Vector3(ox + t * 0.5f, t * 0.6f, oz + t * 0.5f), Art.J(col, rng), rng, 0.22f, 0.35f);
            }
        }

        static void ReefRock(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Rock(mb, Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.35f), 0.25f), rng, 0.6f, 1.6f, null);
            int n = 2 + rng.Range(0, 3);
            Color inner = Art.Darken(c.PropAccent, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), d = rng.Range(0.35f, 0.6f), h = rng.Range(0.15f, 0.4f), r = rng.Range(0.05f, 0.09f);
                mb.Cylinder(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), r, r * 1.3f, h, 5, Art.J(i % 2 == 0 ? c.Prop : c.PropAccent, rng), capColor: inner);
            }
        }

        static void ShellRock(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Rock(mb, Art.Stone(c), rng, 0.5f, 1.1f, null);
            Color sh = Art.Mix(new Color(1f, 0.82f, 0.72f), c.Horizon, 0.3f);
            for (int i = 0; i < 2; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                Art.Shell(mb, new Vector3(Mathf.Cos(a) * 0.55f, 0f, Mathf.Sin(a) * 0.5f), rng.Range(0f, 360f), 0.14f, Art.J(sh, rng), Art.Lighten(sh, 0.3f), 0f);
            }
            // a starfish
            float sa = rng.Range(0f, Mathf.PI * 2f);
            Vector3 sp = new Vector3(Mathf.Cos(sa) * 0.6f, 0.012f, Mathf.Sin(sa) * 0.6f);
            Color star = Art.Vivid(c.Horizon, 0.55f, 0.85f);
            for (int i = 0; i < 5; i++)
            {
                float a = sa + i * Mathf.PI * 0.4f;
                var pts = new[] { sp, sp + new Vector3(Mathf.Cos(a), -0.08f, Mathf.Sin(a)) * 0.1f };
                Art.Tube(mb, pts, new[] { 0.022f, 0.007f }, 4, star, null, 0f, false, true);
            }
        }

        static void Fern(MeshBuilder mb, WorldColors c, Rng rng)
        {
            int n = 7 + rng.Range(0, 3);
            float L = rng.Range(0.55f, 0.9f), off = rng.Range(0f, Mathf.PI * 2f);
            Color leaf = Art.Mix(c.Prop, c.GroundAccent, 0.25f);
            for (int i = 0; i < n; i++)
            {
                float a = off + i * Mathf.PI * 2f / n + rng.Range(-0.2f, 0.2f), len = L * rng.Range(0.85f, 1.1f);
                var dir = new Vector3(Mathf.Cos(a), rng.Range(0.55f, 0.9f), Mathf.Sin(a));
                mb.Frond(new Vector3(0f, 0.02f, 0f), dir, len, len * 0.26f, 3, len * 0.3f, Art.J(leaf, rng), 0f, 1f);
            }
            mb.Cylinder(Vector3.zero, 0.05f, 0f, 0.12f, 5, Art.Darken(leaf, 0.25f), capTop: false);
        }

        static void Palm(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(5f, 7f), bend = rng.Range(0.5f, 1.3f) * (rng.Next01() < 0.5f ? -1f : 1f);
            const int N = 7;
            var pts = new Vector3[N];
            var rad = new float[N];
            var wind = new float[N];
            for (int i = 0; i < N; i++)
            {
                float t = i / (N - 1f);
                pts[i] = new Vector3(bend * t * t, t * H, 0f);
                rad[i] = Mathf.Lerp(0.2f, 0.12f, t);
                wind[i] = t * 0.8f;
            }
            Art.Tube(mb, pts, rad, 6, Art.J(Art.Mix(c.PropAccent, c.GroundAccent, 0.3f), rng), wind);
            Vector3 top = pts[N - 1];
            mb.Ico(top, new Vector3(0.28f, 0.22f, 0.28f), Art.Darken(c.Prop, 0.15f), rng, 0.15f, 0f, 0.8f, 0.85f);
            float off = rng.Range(0f, Mathf.PI * 2f);
            for (int f = 0; f < 7; f++)
            {
                float a = off + f * Mathf.PI * 2f / 7f + rng.Range(-0.15f, 0.15f);
                mb.Frond(top, new Vector3(Mathf.Cos(a), rng.Range(0.15f, 0.45f), Mathf.Sin(a)), rng.Range(2.2f, 2.9f), 0.55f, 4, 1.1f, Art.J(c.Prop, rng), 0.8f, 1f);
            }
            Color nut = Art.Darken(Art.Mix(c.PropAccent, c.Prop, 0.3f), 0.2f);
            for (int i = 0; i < 3; i++)
            {
                float a = off + i * 2.1f;
                mb.Ico(top + new Vector3(Mathf.Cos(a) * 0.2f, -0.25f, Mathf.Sin(a) * 0.2f), Vector3.one * 0.12f, nut, rng, 0.1f, 0f, 0.8f, 0.8f);
            }
        }

        static void Oak(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(5.6f, 7.8f);
            Color bark = Art.J(c.PropAccent, rng);
            mb.Cylinder(Vector3.zero, 0.065f * H, 0.04f * H, 0.55f * H, 7, bark, capTop: false, windTop: 0.35f);
            float ba = rng.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < 2; i++)
            {
                float a = ba + i * Mathf.PI, dx = Mathf.Cos(a) * 0.17f * H, dz = Mathf.Sin(a) * 0.17f * H;
                var pts = new[] { new Vector3(0f, 0.4f * H, 0f), new Vector3(dx * 0.55f, 0.52f * H, dz * 0.55f), new Vector3(dx, 0.62f * H, dz) };
                Art.Tube(mb, pts, new[] { 0.035f * H, 0.026f * H, 0.018f * H }, 5, bark, new[] { 0.3f, 0.4f, 0.5f });
            }
            float cy = 0.68f * H, off = rng.Range(0f, Mathf.PI * 2f);
            mb.Ico(new Vector3(0f, cy, 0f), new Vector3(0.26f, 0.21f, 0.26f) * H, Art.J(c.Prop, rng), rng, 0.18f, 0f, 0.6f, 1f);
            for (int i = 0; i < 4; i++)
            {
                float a = off + i * Mathf.PI * 0.5f + rng.Range(-0.3f, 0.3f);
                var p = new Vector3(Mathf.Cos(a) * 0.2f * H, cy + rng.Range(-0.08f, 0.05f) * H, Mathf.Sin(a) * 0.2f * H);
                mb.Ico(p, new Vector3(0.17f, 0.14f, 0.17f) * (H * rng.Range(0.85f, 1.1f)), Art.J(c.Prop, rng), rng, 0.18f, 0f, 0.55f, 0.95f);
            }
        }

        static void Bush(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float s = rng.Range(0.6f, 1.2f);
            Color leaf = Art.Mix(c.Prop, c.GroundAccent, 0.2f);
            Art.IcoIn(mb, new Vector3(-0.5f * s, 0f, -0.45f * s), new Vector3(0.5f * s, 0.8f * s, 0.45f * s), Art.J(leaf, rng), rng, 0.2f, 0.4f, 0f, 0f, 0.5f);
            for (int i = -1; i <= 1; i += 2)
            {
                float t = s * rng.Range(0.45f, 0.6f), ox = i * s * 0.45f, oz = rng.Range(-0.2f, 0.2f) * s;
                Art.IcoIn(mb, new Vector3(ox - t * 0.5f, 0f, oz - t * 0.5f), new Vector3(ox + t * 0.5f, t * 0.75f, oz + t * 0.5f), Art.J(leaf, rng), rng, 0.2f, 0.4f, 0f, 0f, 0.4f);
            }
        }

        static readonly Color[] Petals =
        {
            new Color(1f, 0.55f, 0.7f), new Color(0.98f, 0.96f, 0.92f), new Color(1f, 0.85f, 0.3f), new Color(0.75f, 0.6f, 0.98f)
        };

        static void FlowerPatch(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color stem = Art.Mix(c.Prop, c.GroundAccent, 0.3f), mid = Art.Vivid(c.Emissive, 0.6f, 0.9f);
            for (int i = 0; i < 3; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                mb.Frond(new Vector3(rng.Range(-0.2f, 0.2f), 0.01f, rng.Range(-0.2f, 0.2f)), new Vector3(Mathf.Cos(a), 0.5f, Mathf.Sin(a)), 0.3f, 0.08f, 2, 0.08f, Art.J(stem, rng), 0f, 1f);
            }
            int n = 8 + rng.Range(0, 4);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(rng.Next01()) * 0.55f, h = rng.Range(0.18f, 0.4f);
                var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                mb.Cylinder(p, 0.012f, 0.008f, h, 3, stem, capTop: false, windTop: 1f);
                Color pc = Art.J(Art.Mix(Petals[rng.Range(0, Petals.Length)], c.Emissive, 0.15f), rng);
                mb.Cylinder(p + new Vector3(0f, h, 0f), 0.015f, 0.06f, 0.03f, 5, pc, emissive: 0.1f, windBase: 1f, windTop: 1f, angleOffset: rng.Range(0f, 1.2f), capColor: mid);
            }
        }

        static void Cactus(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(1.6f, 3f), r = Mathf.Clamp(0.07f * H, 0.11f, 0.2f);
            Color g = Art.J(c.Prop, rng);
            mb.Cylinder(Vector3.zero, r, r * 0.95f, H - r, 8, g, capTop: false);
            mb.Cylinder(new Vector3(0f, H - r, 0f), r * 0.95f, r * 0.35f, r, 8, g);
            int arms = 1 + rng.Range(0, 2);
            float yaw0 = rng.Range(0f, 360f);
            for (int i = 0; i < arms; i++)
            {
                float y = H * rng.Range(0.35f, 0.55f), ar = r * 0.65f, outX = r + rng.Range(0.22f, 0.34f), up = rng.Range(0.18f, 0.3f) * H;
                mb.Push(Vector3.zero, Quaternion.Euler(0f, yaw0 + i * 180f + rng.Range(-20f, 20f), 0f), Vector3.one);
                var pts = new[]
                {
                    new Vector3(r * 0.3f, y, 0f), new Vector3(outX, y, 0f), new Vector3(outX + ar * 0.6f, y + ar * 1.4f, 0f),
                    new Vector3(outX + ar * 0.6f, y + ar * 1.4f + up, 0f)
                };
                Art.Tube(mb, pts, new[] { ar, ar, ar, ar * 0.9f }, 8, g, null, 0f, false, true);
                mb.Pop();
            }
            if (rng.Next01() < 0.5f)
                mb.Cylinder(new Vector3(0f, H - 0.02f, 0f), 0.03f, 0.07f, 0.05f, 5, Art.Mix(new Color(1f, 0.45f, 0.6f), c.PropAccent, 0.2f), emissive: 0.15f);
        }

        static void DuneGrass(MeshBuilder mb, WorldColors c, Rng rng)
        {
            int n = 7 + rng.Range(0, 4);
            Color g = Art.Mix(c.Prop, c.GroundAccent, 0.45f);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), o = rng.Range(0f, 0.08f), lean = rng.Range(0.2f, 0.5f);
                mb.Frond(new Vector3(Mathf.Cos(a) * o, 0f, Mathf.Sin(a) * o), new Vector3(Mathf.Cos(a) * lean, 1f, Mathf.Sin(a) * lean),
                         rng.Range(0.45f, 0.9f), 0.05f, 2, rng.Range(0.1f, 0.25f), Art.J(g, rng), 0f, 1f);
            }
        }

        /// <summary>Ice shards or a glowing crystal cluster: tilted hex prisms with pointed tips.</summary>
        static void Shards(MeshBuilder mb, Color col, Rng rng, int min, float hMin, float hMax, float em, bool baseRock, WorldColors c)
        {
            if (baseRock)
                Art.IcoIn(mb, new Vector3(-0.32f, 0f, -0.28f), new Vector3(0.32f, 0.2f, 0.28f), Art.J(Art.Stone(c), rng), rng, 0.15f, 0.4f);
            int n = min + rng.Range(0, 3);
            float spread = baseRock ? 0.2f : 0.5f;
            for (int i = 0; i < n; i++)
            {
                float h = i == 0 ? hMax * rng.Range(0.85f, 1f) : rng.Range(hMin, hMax * 0.7f), r = h * rng.Range(0.1f, 0.15f);
                float a = rng.Range(0f, Mathf.PI * 2f), d = i == 0 ? 0f : rng.Range(0.4f, 1f) * spread;
                mb.Push(new Vector3(Mathf.Cos(a) * d, baseRock ? 0.05f : -0.05f, Mathf.Sin(a) * d),
                        Quaternion.Euler(rng.Range(-15f, 15f), rng.Range(0f, 60f), rng.Range(-15f, 15f)), Vector3.one);
                Color ci = Art.J(col, rng);
                mb.Cylinder(Vector3.zero, r, r * 0.85f, h, 6, ci, capTop: false, emissive: em);
                mb.Cylinder(new Vector3(0f, h, 0f), r * 0.85f, 0f, r * 1.8f, 6, Art.Lighten(ci, 0.25f), emissive: Mathf.Min(1f, em + 0.1f));
                mb.Pop();
            }
        }

        static void Coral(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float s = rng.Range(0.6f, 1.6f);
            Color col = Art.J(c.Prop, rng), tip = Art.Lighten(col, 0.3f);
            if (rng.Next01() < 0.6f)
            {
                CoralBranch(mb, Vector3.zero, Quaternion.Euler(rng.Range(-8f, 8f), 0f, rng.Range(-8f, 8f)), 0.36f * s, 0.06f * s, 2, col, tip, rng);
                return;
            }
            Color alt = Art.J(c.PropAccent, rng), inner = Art.Darken(alt, 0.5f);
            Art.IcoIn(mb, new Vector3(-0.3f * s, 0f, -0.28f * s), new Vector3(0.3f * s, 0.35f * s, 0.28f * s), Art.J(Art.Mix(col, alt, 0.5f), rng), rng, 0.12f, 0.45f);
            int n = 3 + rng.Range(0, 3);
            for (int i = 0; i < n; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f), d = rng.Range(0.2f, 0.4f) * s, h = rng.Range(0.3f, 0.8f) * s, r = rng.Range(0.05f, 0.09f) * s;
                mb.Cylinder(new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), r, r * 1.25f, h, 6, i % 2 == 0 ? alt : col, capColor: inner);
            }
        }

        static void CoralBranch(MeshBuilder mb, Vector3 p, Quaternion q, float len, float r, int depth, Color col, Color tip, Rng rng)
        {
            mb.Push(p, q, Vector3.one);
            mb.Cylinder(Vector3.zero, r, r * 0.72f, len, 5, col, capTop: false);
            if (depth == 0) mb.Cylinder(new Vector3(0f, len, 0f), r * 0.72f, 0f, r * 1.6f, 5, tip);
            else
            {
                int k = 2 + rng.Range(0, 2);
                float off = rng.Range(0f, 360f);
                for (int i = 0; i < k; i++)
                    CoralBranch(mb, new Vector3(0f, len, 0f), Quaternion.Euler(0f, off + i * 360f / k, 0f) * Quaternion.Euler(rng.Range(22f, 42f), 0f, 0f),
                                len * rng.Range(0.7f, 0.9f), r * 0.72f, depth - 1, col, tip, rng);
            }
            mb.Pop();
        }

        static void Kelp(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color stalkC = Art.Mix(c.GroundAccent, new Color(0.5f, 0.52f, 0.16f), 0.55f);
            Art.IcoIn(mb, new Vector3(-0.18f, 0f, -0.15f), new Vector3(0.18f, 0.14f, 0.15f), Art.J(Art.Stone(c), rng), rng, 0.15f, 0.4f);
            int stalks = 2 + rng.Range(0, 3);
            for (int s = 0; s < stalks; s++)
            {
                float H = rng.Range(2f, 4f), a = rng.Range(0f, Mathf.PI * 2f), ph = rng.Range(0f, Mathf.PI * 2f);
                Vector3 b = new Vector3(Mathf.Cos(a) * 0.08f, 0.05f, Mathf.Sin(a) * 0.08f);
                const int N = 7;
                var pts = new Vector3[N];
                var rad = new float[N];
                var wind = new float[N];
                for (int i = 0; i < N; i++)
                {
                    float t = i / (N - 1f);
                    pts[i] = b + new Vector3(Mathf.Sin(t * 4f + ph) * 0.12f * t, t * H, Mathf.Cos(t * 3f + ph) * 0.1f * t);
                    rad[i] = Mathf.Lerp(0.03f, 0.012f, t);
                    wind[i] = t;
                }
                Color sc = Art.J(stalkC, rng);
                Art.Tube(mb, pts, rad, 3, sc, wind);
                for (int i = 1; i < N - 1; i++)
                {
                    float t = i / (N - 1f), ba = a + ((i & 1) == 0 ? 1.4f : -1.4f);
                    mb.Frond(pts[i], new Vector3(Mathf.Cos(ba) * 0.6f, 1f, Mathf.Sin(ba) * 0.6f), rng.Range(0.45f, 0.75f), 0.16f, 2, 0.08f,
                             Art.Lighten(sc, 0.08f), t, Mathf.Min(1f, t + 0.25f));
                }
            }
        }

        // ---------- built scatter ----------

        static void Antenna(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color metal = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.35f);
            mb.Box(new Vector3(0f, 0.15f, 0f), new Vector3(0.8f, 0.3f, 0.8f), dark, c.PropAccent);
            mb.Cylinder(new Vector3(0f, 0.3f, 0f), 0.07f, 0.05f, 2.6f, 6, metal, capTop: false);
            mb.Push(new Vector3(0f, 2.3f, 0.12f), Quaternion.Euler(-35f, rng.Range(-30f, 30f), 0f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.08f, 0.7f, 0.28f, 10, metal, capColor: dark);
            mb.Cylinder(new Vector3(0f, 0.28f, 0f), 0.015f, 0.015f, 0.45f, 4, c.PropAccent, capTop: false);
            mb.Box(new Vector3(0f, 0.75f, 0f), new Vector3(0.07f, 0.07f, 0.07f), c.Emissive, null, 1f);
            mb.Pop();
            mb.Ico(new Vector3(0f, 2.95f, 0f), Vector3.one * 0.07f, c.Emissive, null, 0f, 1f);
        }

        static void Panel(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color frame = Art.J(c.Prop, rng), cell = Art.Mix(Art.Darken(c.Top, 0.1f), c.Emissive, 0.25f);
            mb.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.1f, 0.5f), Art.Darken(frame, 0.3f));
            mb.Cylinder(new Vector3(0f, 0.1f, 0f), 0.07f, 0.06f, 0.95f, 6, frame, capTop: false);
            mb.Push(new Vector3(0f, 1.08f, 0f), Quaternion.Euler(-28f, 0f, 0f), Vector3.one);
            mb.Box(Vector3.zero, new Vector3(3f, 0.08f, 1.6f), frame, Art.Darken(frame, 0.2f));
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                    Art.Rect(mb, new Vector3(-1.125f + i * 0.75f, 0.042f, -0.39f + j * 0.78f), Vector3.up, Vector3.right, 0.68f, 0.72f, cell, 0.2f);
            mb.Pop();
        }

        static void Tank(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(2f, 3f), R = rng.Range(0.8f, 1.1f);
            Color body = Art.J(c.Prop, rng), dark = Art.Darken(c.Prop, 0.4f);
            for (int i = 0; i < 4; i++)
            {
                float a = 0.785f + i * Mathf.PI * 0.5f;
                mb.Box(new Vector3(Mathf.Cos(a) * R * 0.7f, 0.2f, Mathf.Sin(a) * R * 0.7f), new Vector3(0.14f, 0.4f, 0.14f), dark);
            }
            mb.Cylinder(new Vector3(0f, 0.4f, 0f), R, R, H, 10, body, capTop: false, capBottom: true);
            mb.Cylinder(new Vector3(0f, 0.4f + H, 0f), R, R * 0.55f, 0.32f, 10, body);
            for (int i = 0; i < 2; i++)
                mb.Cylinder(new Vector3(0f, 0.4f + H * (0.25f + 0.5f * i), 0f), R + 0.03f, R + 0.03f, 0.14f, 10, c.PropAccent, capTop: false);
            float lz = R + 0.06f;
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(s * 0.16f, 0.4f + H * 0.5f, lz), new Vector3(0.04f, H, 0.04f), dark);
            for (int i = 0; i < 4; i++) mb.Box(new Vector3(0f, 0.6f + i * (H - 0.4f) / 3.5f, lz), new Vector3(0.32f, 0.03f, 0.03f), dark);
            mb.Box(new Vector3(0f, 0.4f + H + 0.37f, 0f), new Vector3(0.12f, 0.1f, 0.12f), c.Emissive, null, 1f);
        }

        static void Pylon(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(3.5f, 5f);
            Color metal = Art.J(c.Prop, rng);
            mb.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.4f, 0.6f), Art.Darken(metal, 0.3f), c.PropAccent);
            mb.Cylinder(new Vector3(0f, 0.4f, 0f), 0.18f, 0.11f, H - 0.9f, 4, metal, capTop: false, angleOffset: Mathf.PI * 0.25f);
            for (int i = 0; i < 2; i++)
            {
                float y = 0.4f + (H - 0.9f) * (0.3f + 0.3f * i), w = Mathf.Lerp(0.18f, 0.11f, (y - 0.4f) / (H - 0.9f)) * 1.45f;
                mb.Box(new Vector3(0f, y, 0f), new Vector3(w, 0.06f, w), c.Emissive, null, 0.8f);
            }
            mb.Cylinder(new Vector3(0f, H - 0.6f, 0f), 0.12f, 0.2f, 0.06f, 4, metal, angleOffset: Mathf.PI * 0.25f);
            mb.Box(new Vector3(0f, H - 0.3f, 0f), new Vector3(0.26f, 0.5f, 0.26f), c.Emissive, null, 1f);
            mb.Cylinder(new Vector3(0f, H - 0.05f, 0f), 0.24f, 0.06f, 0.14f, 4, metal, angleOffset: Mathf.PI * 0.25f);
        }

        static void Building(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float H = rng.Range(6f, 20f), W = rng.Range(5f, 9f), D = rng.Range(5f, 9f);
            Color wall = Art.J(Art.Mix(c.Prop, c.Fog, 0.15f), rng), roof = Art.Darken(wall, 0.25f);
            Color glass = Art.Darken(Art.Mix(c.Prop, c.Top, 0.5f), 0.3f);
            mb.Box(new Vector3(0f, H * 0.5f, 0f), new Vector3(W, H, D), wall, roof);
            mb.Box(new Vector3(0f, H + 0.15f, 0f), new Vector3(W + 0.2f, 0.3f, D + 0.2f), Art.Darken(wall, 0.12f), roof, 0f, true);
            int floors = Mathf.Clamp(Mathf.FloorToInt((H - 1.5f) / 3f), 1, 6);
            float fh = (H - 1.5f) / floors;
            for (int f = 0; f < 4; f++)
            {
                Vector3 n = f == 0 ? Vector3.forward : f == 1 ? Vector3.back : f == 2 ? Vector3.right : Vector3.left;
                float faceW = f < 2 ? W : D, depth = f < 2 ? D * 0.5f : W * 0.5f;
                Vector3 r = Vector3.Cross(n, Vector3.up);
                int cols = Mathf.Clamp(Mathf.FloorToInt(faceW / 2.2f), 1, 4);
                float cw = faceW / cols, ww = Mathf.Min(1.1f, cw * 0.5f), wh = Mathf.Min(1.5f, fh * 0.5f);
                for (int y = 0; y < floors; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        bool lit = rng.Next01() < 0.15f;
                        Color wc = lit ? (rng.Next01() < 0.3f ? c.Neon : c.Emissive) : glass;
                        Vector3 p = n * (depth + 0.02f) + Vector3.up * (1.5f + (y + 0.5f) * fh) + r * (-faceW * 0.5f + (x + 0.5f) * cw);
                        Art.Rect(mb, p, n, r, ww, wh, wc, lit ? 1f : 0f);
                    }
            }
            Art.Rect(mb, new Vector3(0f, 0.9f, D * 0.5f + 0.02f), Vector3.forward, Vector3.right, 1.2f, 1.8f, Art.Mix(c.Emissive, glass, 0.4f), 0.5f);
            if (rng.Next01() < 0.5f)
            {
                // a water tower on the roof
                float tx = rng.Range(-0.25f, 0.25f) * W, tz = rng.Range(-0.25f, 0.25f) * D, top = H + 0.3f;
                for (int i = 0; i < 4; i++)
                {
                    float a = 0.785f + i * Mathf.PI * 0.5f;
                    mb.Box(new Vector3(tx + Mathf.Cos(a) * 0.55f, top + 0.4f, tz + Mathf.Sin(a) * 0.55f), new Vector3(0.1f, 0.8f, 0.1f), roof);
                }
                mb.Cylinder(new Vector3(tx, top + 0.8f, tz), 0.8f, 0.8f, 1.4f, 6, Art.Mix(c.PropAccent, wall, 0.4f), capTop: false, capBottom: true);
                mb.Cylinder(new Vector3(tx, top + 2.2f, tz), 0.85f, 0f, 0.5f, 6, roof);
            }
            else
            {
                mb.Box(new Vector3(W * 0.2f, H + 0.6f, -D * 0.2f), new Vector3(1.4f, 0.6f, 1f), roof);
                mb.Cylinder(new Vector3(-W * 0.25f, H + 0.3f, D * 0.2f), 0.04f, 0.02f, 3f, 4, roof, capTop: false);
                mb.Ico(new Vector3(-W * 0.25f, H + 3.3f, D * 0.2f), Vector3.one * 0.12f, c.Neon, null, 0f, 1f);
            }
        }

        static void StreetLamp(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color metal = Art.J(Art.Darken(Art.Mix(c.Prop, new Color(0.2f, 0.2f, 0.22f), 0.4f), 0.1f), rng);
            mb.Cylinder(Vector3.zero, 0.16f, 0.12f, 0.35f, 6, metal);
            mb.Cylinder(new Vector3(0f, 0.35f, 0f), 0.055f, 0.045f, 2.75f, 6, metal, capTop: false);
            mb.Cylinder(new Vector3(0f, 3.1f, 0f), 0.09f, 0.09f, 0.06f, 6, metal);
            mb.Cylinder(new Vector3(0f, 3.16f, 0f), 0.1f, 0.16f, 0.26f, 6, c.Emissive, capTop: false, emissive: 1f);
            mb.Cylinder(new Vector3(0f, 3.42f, 0f), 0.2f, 0f, 0.12f, 6, metal, capBottom: true);
            mb.Cylinder(new Vector3(0f, 3.54f, 0f), 0.02f, 0f, 0.06f, 4, metal);
        }

        static void Hydrant(MeshBuilder mb, WorldColors c)
        {
            Color red = Art.Mix(new Color(0.78f, 0.16f, 0.12f), c.PropAccent, 0.3f), cap = Art.Darken(red, 0.25f);
            mb.Cylinder(Vector3.zero, 0.16f, 0.16f, 0.05f, 8, cap);
            mb.Cylinder(new Vector3(0f, 0.05f, 0f), 0.12f, 0.11f, 0.45f, 8, red, capTop: false);
            mb.Cylinder(new Vector3(0f, 0.5f, 0f), 0.14f, 0.14f, 0.05f, 8, cap);
            mb.Cylinder(new Vector3(0f, 0.55f, 0f), 0.12f, 0.04f, 0.12f, 8, red);
            mb.Cylinder(new Vector3(0f, 0.67f, 0f), 0.035f, 0.035f, 0.04f, 5, cap);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push(new Vector3(s * 0.1f, 0.36f, 0f), Quaternion.Euler(0f, 0f, -s * 90f), Vector3.one);
                mb.Cylinder(Vector3.zero, 0.045f, 0.045f, 0.08f, 6, cap);
                mb.Pop();
            }
            mb.Push(new Vector3(0f, 0.3f, 0.09f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.06f, 0.06f, 0.08f, 6, cap);
            mb.Pop();
        }

        static void Stone(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float s = rng.Range(0.1f, 0.2f);
            Art.IcoIn(mb, new Vector3(-s * 0.5f, 0f, -s * 0.4f), new Vector3(s * 0.5f, s * 0.35f, s * 0.4f), Art.J(Art.Stone(c), rng), rng, 0.15f, 0.5f);
        }

        static void LogSmall(MeshBuilder mb, WorldColors c, Rng rng)
        {
            float L = 0.4f * rng.Range(0.9f, 1.1f), r = rng.Range(0.045f, 0.065f);
            Color bark = Art.J(c.PropAccent, rng), cut = Art.Lighten(Art.Mix(c.PropAccent, new Color(0.9f, 0.75f, 0.5f), 0.5f), 0.2f);
            mb.Log(new Vector3(0f, r, 0f), L, r, 7, bark, cut);
        }

        // ---------- extras ----------

        static void Lantern(MeshBuilder mb, WorldColors c)
        {
            Color paper = Art.Lighten(Art.Vivid(c.Emissive, 0.6f, 0.9f), 0.1f), cap = Art.Darken(c.PropAccent, 0.3f);
            mb.Cylinder(Vector3.zero, 0f, 0.025f, 0.06f, 5, cap, windBase: 0.65f, windTop: 0.62f);
            mb.Cylinder(new Vector3(0f, 0.06f, 0f), 0.05f, 0.06f, 0.02f, 8, cap, capTop: false, capBottom: true, windBase: 0.62f, windTop: 0.6f);
            mb.Cylinder(new Vector3(0f, 0.08f, 0f), 0.09f, 0.13f, 0.09f, 8, paper, capTop: false, emissive: 0.9f, windBase: 0.6f, windTop: 0.55f);
            mb.Cylinder(new Vector3(0f, 0.17f, 0f), 0.13f, 0.09f, 0.09f, 8, paper, capTop: false, emissive: 0.9f, windBase: 0.55f, windTop: 0.5f);
            mb.Cylinder(new Vector3(0f, 0.26f, 0f), 0.06f, 0.05f, 0.02f, 8, cap, windBase: 0.5f, windTop: 0.48f);
            mb.Cylinder(new Vector3(0f, 0.28f, 0f), 0.004f, 0.004f, 0.06f, 3, cap, capTop: false, windBase: 0.48f, windTop: 0f);
        }

        static void Flower(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color stem = Art.Mix(c.Prop, c.GroundAccent, 0.3f), petal = Art.J(Art.Mix(Petals[rng.Range(0, Petals.Length)], c.Emissive, 0.1f), rng);
            mb.Cylinder(Vector3.zero, 0.006f, 0.004f, 0.22f, 4, stem, capTop: false, windTop: 1f);
            for (int s = -1; s <= 1; s += 2)
                mb.Frond(new Vector3(0f, 0.05f + (s + 1) * 0.02f, 0f), new Vector3(s, 0.6f, 0.2f), 0.08f, 0.035f, 2, 0.01f, Art.J(stem, rng), 0.2f, 0.5f);
            Vector3 head = new Vector3(0f, 0.22f, 0f);
            float off = rng.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < 5; i++)
            {
                float a = off + i * Mathf.PI * 0.4f;
                mb.Frond(head, new Vector3(Mathf.Cos(a), 0.25f, Mathf.Sin(a)), 0.045f, 0.035f, 1, 0.005f, petal, 1f, 1f);
            }
            mb.Ico(head + new Vector3(0f, 0.005f, 0f), Vector3.one * 0.014f, Art.Vivid(c.Emissive, 0.6f, 0.95f), rng, 0.1f, 0.3f, 1f, 1f);
        }

        static void Cloud(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color cc = Art.Lighten(c.Horizon, 0.55f);
            float[] x = { 0f, -1.6f, 1.6f, -2.6f, 2.6f, 0.6f, -0.7f }, y = { 0f, 0f, 0f, 0f, 0f, 0.7f, 0.6f }, r = { 1.5f, 1.1f, 1.15f, 0.7f, 0.7f, 0.95f, 0.9f };
            for (int i = 0; i < x.Length; i++)
            {
                float rr = r[i] * rng.Range(0.85f, 1.1f);
                var rad = new Vector3(rr, rr * 0.75f, rr * 0.85f);
                mb.Ico(new Vector3(x[i] + rng.Range(-0.15f, 0.15f), y[i] + 0.4f * rad.y, rng.Range(-0.3f, 0.3f)), rad, Art.J(cc, rng), rng, 0.15f, 0.1f, 0f, 0f, 0.4f);
            }
        }

        // ---------- gatherable items (6–12 cm, the most saturated, glowing a little) ----------

        static void Kindling(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color wood = Art.Vivid(Art.Mix(c.PropAccent, new Color(0.7f, 0.42f, 0.2f), 0.4f), 0.6f, 0.62f);
            Color cut = Art.Lighten(wood, 0.45f), twine = Art.Vivid(c.Emissive, 0.7f, 0.95f);
            const float r = 0.008f;
            var pos = new[] { new Vector3(0f, r, -0.0085f), new Vector3(0f, r, 0.0085f), new Vector3(0.003f, r * 2.7f, 0f) };
            for (int i = 0; i < 3; i++)
            {
                float L = 0.1f * rng.Range(0.9f, 1.08f);
                mb.Push(pos[i], Quaternion.Euler(0f, rng.Range(-7f, 7f), 90f), Vector3.one);
                mb.Cylinder(new Vector3(0f, -L * 0.5f, 0f), r, r * 0.85f, L, 5, Art.J(wood, rng), true, true, 0.3f, 0f, 0f, 0f, cut);
                mb.Pop();
            }
            mb.Box(new Vector3(0f, 0.0152f, 0f), new Vector3(0.013f, 0.031f, 0.037f), twine, null, 0.45f);
        }

        static void SunShard(MeshBuilder mb, WorldColors c)
        {
            Color gold = Art.Vivid(Art.Mix(c.Emissive, new Color(1f, 0.72f, 0.18f), 0.55f), 0.75f, 1f), tip = Art.Lighten(gold, 0.3f);
            mb.Push(new Vector3(0f, 0.002f, 0f), Quaternion.Euler(5f, 0f, -6f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.017f, 0.02f, 0.055f, 6, gold, false, true, 0.45f);
            mb.Cylinder(new Vector3(0f, 0.055f, 0f), 0.02f, 0f, 0.035f, 6, tip, emissive: 0.5f);
            mb.Pop();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push(new Vector3(s * 0.021f, 0.002f, 0.006f), Quaternion.Euler(0f, 0f, -s * 26f), Vector3.one);
                mb.Cylinder(Vector3.zero, 0.008f, 0.009f, 0.026f, 6, gold, false, true, 0.4f);
                mb.Cylinder(new Vector3(0f, 0.026f, 0f), 0.009f, 0f, 0.016f, 6, tip, emissive: 0.5f);
                mb.Pop();
            }
        }

        static void Snowball(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color snow = Art.Mix(new Color(0.86f, 0.94f, 1f), Art.Lighten(c.Ground, 0.2f), 0.3f);
            mb.Ico(new Vector3(0f, 0.6f * 0.045f, 0f), Vector3.one * 0.045f, snow, rng, 0.1f, 0.35f, 0f, 0f, 0.6f);
            mb.Ico(new Vector3(0.022f, 0.05f, 0.012f), Vector3.one * 0.017f, Art.Mix(snow, new Color(0.7f, 0.86f, 1f), 0.5f), rng, 0.1f, 0.45f);
        }

        static void EnergyCell(MeshBuilder mb, WorldColors c)
        {
            Color body = Art.Vivid(c.PropAccent, 0.75f, 0.85f), glow = Art.Vivid(c.Emissive, 0.7f, 1f), dark = Art.Darken(c.Prop, 0.5f);
            mb.Cylinder(Vector3.zero, 0.016f, 0.022f, 0.008f, 8, body, false, true, 0.3f);
            mb.Cylinder(new Vector3(0f, 0.008f, 0f), 0.022f, 0.022f, 0.064f, 8, body, false, false, 0.3f);
            mb.Cylinder(new Vector3(0f, 0.072f, 0f), 0.022f, 0.013f, 0.008f, 8, body, true, false, 0.3f);
            mb.Cylinder(new Vector3(0f, 0.08f, 0f), 0.006f, 0.006f, 0.008f, 6, dark, true, false, 0.25f);
            mb.Cylinder(new Vector3(0f, 0.033f, 0f), 0.0235f, 0.0235f, 0.014f, 8, glow, false, false, 1f);
        }

        /// <summary>An open clam with a pearl (also the reef souvenir at a larger <paramref name="k"/>).</summary>
        internal static void Pearl(MeshBuilder mb, WorldColors c, Rng rng, float k)
        {
            Color shell = Art.Vivid(c.PropAccent, 0.55f, 0.8f), lining = Art.Lighten(Art.Mix(c.Emissive, new Color(1f, 0.8f, 0.9f), 0.5f), 0.3f);
            Color pearl = Art.Mix(new Color(1f, 0.97f, 0.98f), c.Emissive, 0.2f);
            mb.Push(Vector3.zero, Quaternion.identity, new Vector3(k, k, k));
            mb.Cylinder(Vector3.zero, 0.022f, 0.045f, 0.016f, 8, shell, true, true, 0.3f, 0f, 0f, 0f, lining);
            mb.Push(new Vector3(0f, 0.016f, -0.045f), Quaternion.Euler(-70f, 0f, 0f), Vector3.one);
            mb.Cylinder(new Vector3(0f, 0f, 0.045f), 0.045f, 0.022f, 0.012f, 8, shell, true, true, 0.3f, 0f, 0f, 0f, lining);
            mb.Pop();
            mb.Ico(new Vector3(0f, 0.029f, 0.004f), Vector3.one * 0.016f, pearl, rng, 0.05f, 0.5f);
            mb.Pop();
        }

        static void Record(MeshBuilder mb, WorldColors c)
        {
            Color sleeve = Art.Vivid(Art.Mix(c.PropAccent, c.Neon, 0.3f), 0.65f, 0.8f), vinyl = new Color(0.07f, 0.06f, 0.09f), label = Art.Vivid(c.Neon, 0.7f, 1f);
            mb.Push(Vector3.zero, Quaternion.Euler(0f, 12f, 0f), Vector3.one);
            mb.Box(new Vector3(0f, 0.0025f, 0f), new Vector3(0.095f, 0.005f, 0.095f), sleeve, null, 0.3f);
            mb.Pop();
            Vector3 dc = new Vector3(0.012f, 0.005f, 0.005f);
            mb.Cylinder(dc, 0.048f, 0.048f, 0.004f, 16, vinyl, true, false, 0.3f);
            mb.Disc(dc + new Vector3(0f, 0.0042f, 0f), 0.021f, 12, label, 0.5f);
            mb.Disc(dc + new Vector3(0f, 0.0044f, 0f), 0.0035f, 6, vinyl, 0.2f);
        }

        static void Seed(MeshBuilder mb, WorldColors c, Rng rng)
        {
            Color nut = Art.Vivid(Art.Mix(c.PropAccent, new Color(0.75f, 0.45f, 0.15f), 0.5f), 0.7f, 0.75f);
            Color cup = Art.Vivid(Art.Darken(Art.Mix(c.Prop, c.PropAccent, 0.5f), 0.2f), 0.5f, 0.45f), glow = Art.Vivid(c.Emissive, 0.7f, 1f);
            Art.IcoIn(mb, new Vector3(-0.022f, 0f, -0.022f), new Vector3(0.022f, 0.048f, 0.022f), nut, rng, 0.06f, 0.4f, 0.3f);
            mb.Cylinder(new Vector3(0f, 0.038f, 0f), 0.023f, 0.025f, 0.012f, 8, cup, emissive: 0.25f);
            mb.Cylinder(new Vector3(0f, 0.05f, 0f), 0.007f, 0f, 0.022f, 5, glow, emissive: 1f);
            mb.Ico(new Vector3(0f, 0.074f, 0f), Vector3.one * 0.006f, glow, null, 0f, 1f);
        }
    }
}

using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// What the table builds when the quest is done (on the table top at the origin, inside a circle of diameter
    /// <c>footprint</c>, no taller than 1.2 × footprint), and the small souvenir (8–12 cm) left on the real table after.
    /// </summary>
    public static class Builds
    {
        const float Tau = Mathf.PI * 2f;

        public static bool Result(MeshBuilder mb, string build, WorldColors c, Rng rng, float footprint)
        {
            if (mb == null) return false;
            string k = Art.Norm(build);
            if (!IsResult(k)) return false;
            if (!(footprint > 0f) || float.IsInfinity(footprint)) footprint = 0.4f;
            footprint = Mathf.Clamp(footprint, 0.05f, 2f);
            rng ??= new Rng(1);
            using (new Art.Ao(mb, 0.12f * footprint)) BuildResult(mb, k, c, rng, footprint);
            return true;
        }

        public static bool Souvenir(MeshBuilder mb, string kind, WorldColors c, Rng rng)
        {
            if (mb == null) return false;
            string k = Art.Norm(kind);
            rng ??= new Rng(1);
            const float F = 0.085f, plinth = 0.008f;
            string build;
            switch (k)
            {
                case "sandcastle": build = "sandcastle"; break;
                case "sun_crystal": build = "sun_crystal"; break;
                case "snowman": build = "snowman"; break;
                case "star_globe": build = "star_map"; break;
                case "jukebox": build = "jukebox"; break;
                case "bonsai": build = "blossom_tree"; break;
                case "lantern": case "pearl_shell": build = null; break;
                default: return false;
            }
            using (new Art.Ao(mb, 0.015f))
            {
                if (k == "lantern") { MiniLantern(mb, c); return true; }
                if (k != "bonsai") Plinth(mb, c, plinth);
                mb.Push(Vector3.up * (k == "bonsai" ? 0f : plinth));
                if (build != null) BuildResult(mb, build, c, rng, F);
                else Props.Pearl(mb, c, rng, 1.05f);
                mb.Pop();
            }
            return true;
        }

        static bool IsResult(string k)
        {
            switch (k)
            {
                case "fire": case "sandcastle": case "sun_crystal": case "snowman":
                case "star_map": case "anemone_glow": case "jukebox": case "blossom_tree":
                    return true;
                default: return false;
            }
        }

        static void BuildResult(MeshBuilder mb, string k, WorldColors c, Rng rng, float F)
        {
            switch (k)
            {
                case "fire": Fire(mb, c, rng, F); break;
                case "sandcastle": Sandcastle(mb, c, rng, F); break;
                case "sun_crystal": SunCrystal(mb, c, rng, F); break;
                case "snowman": Snowman(mb, c, rng, F); break;
                case "star_map": StarMap(mb, c, rng, F); break;
                case "anemone_glow": AnemoneGlow(mb, c, rng, F); break;
                case "jukebox": Jukebox(mb, c, F); break;
                case "blossom_tree": BlossomTree(mb, c, rng, F); break;
            }
        }

        static void Plinth(MeshBuilder mb, WorldColors c, float h)
        {
            Color wood = Art.Darken(c.PropAccent, 0.3f);
            mb.Cylinder(Vector3.zero, 0.05f, 0.048f, h, 12, wood, true, false, 0f, 0f, 0f, 0f, Art.Lighten(wood, 0.12f));
        }

        // ---------- results (R = footprint / 2) ----------

        static void Fire(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F;
            Color stone = Art.Stone(c), bark = c.PropAccent, cut = Art.Lighten(Art.Mix(c.PropAccent, new Color(0.9f, 0.75f, 0.5f), 0.5f), 0.15f);
            Color ember = Art.Mix(c.Emissive, new Color(0.7f, 0.18f, 0.05f), 0.5f);
            const int ns = 9;
            float sr = 0.13f * R;
            for (int i = 0; i < ns; i++)
            {
                float a = i * Tau / ns + rng.Range(-0.12f, 0.12f);
                var p = new Vector3(Mathf.Cos(a) * 0.8f * R, 0f, Mathf.Sin(a) * 0.8f * R);
                float h = sr * rng.Range(1f, 1.5f);
                Art.IcoIn(mb, p - new Vector3(sr, 0f, sr), p + new Vector3(sr, h, sr), Art.J(stone, rng), rng, 0.2f, 0.4f);
            }
            Art.Disc(mb, new Vector3(0f, 0.003f, 0f), Vector3.up, 0.5f * R, 10, ember, 0.8f);
            for (int i = 0; i < 4; i++)
            {
                float a = rng.Range(0f, Tau), d = rng.Range(0f, 0.3f) * R, q = 0.08f * R;
                var p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                Art.IcoIn(mb, p - new Vector3(q, 0f, q), p + new Vector3(q, q * 1.2f, q), Art.Mix(ember, Color.black, 0.25f), rng, 0.2f, 0.35f, 0.7f);
            }
            // a log tepee, open in the middle for the flames
            float apexY = 0.75f * F, baseR = 0.55f * R, lr = 0.07f * R, off = rng.Range(0f, Tau);
            for (int i = 0; i < 6; i++)
            {
                float a = off + i * Tau / 6f;
                var b = new Vector3(Mathf.Cos(a) * baseR, lr, Mathf.Sin(a) * baseR);
                var apex = new Vector3(-Mathf.Cos(a) * 0.06f * R, apexY * rng.Range(0.92f, 1f), -Mathf.Sin(a) * 0.06f * R);
                var top = apex + (apex - b) * 0.12f;
                Art.Tube(mb, new[] { b, top }, new[] { lr, lr * 0.8f }, 6, Art.J(bark, rng), null, 0f, true, true, cut);
            }
        }

        static void Merlons(MeshBuilder mb, float cx, float y, float cz, float r, int n, float s, Color col, float off)
        {
            for (int i = 0; i < n; i++)
            {
                float a = off + i * Tau / n, d = r - s * 0.5f;
                mb.Box(new Vector3(cx + Mathf.Cos(a) * d, y + s * 0.5f, cz + Mathf.Sin(a) * d), new Vector3(s, s, s), col);
            }
        }

        static void Sandcastle(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F, y0 = 0.12f * F;
            Color sand = Art.Mix(c.Ground, new Color(0.93f, 0.8f, 0.55f), 0.3f), wet = Art.Darken(sand, 0.15f);
            Color flag = Art.Vivid(Art.Mix(c.PropAccent, new Color(0.9f, 0.25f, 0.3f), 0.5f), 0.7f, 0.85f), pole = Art.Darken(c.PropAccent, 0.3f);
            Art.Column(mb, 0f, 0f, R, R, 0f, y0, 10, 0.95f, 0.85f, wet, true, 0f, 0f, 0f, 0f, sand);
            // four corner towers with crenellations
            float tr = 0.2f * R, th = 0.42f * F, td = 0.55f * R;
            for (int k = 0; k < 4; k++)
            {
                float a = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f, x = Mathf.Cos(a) * td, z = Mathf.Sin(a) * td;
                Art.Column(mb, x, z, tr, tr, y0, th, 8, 1f, 0.92f, Art.J(sand, rng), true);
                Merlons(mb, x, y0 + th, z, tr * 0.92f, 3, 0.3f * tr, sand, k * 0.5f);
            }
            // walls between them
            float wd = td * 0.7071f, wt = 0.12f * R, wh = 0.26f * F, ms = 0.7f * wt;
            for (int k = 0; k < 4; k++)
            {
                bool alongX = k < 2;
                float sg = (k & 1) == 0 ? 1f : -1f;
                Vector3 mn = alongX ? new Vector3(-wd, y0, sg * wd - wt * 0.5f) : new Vector3(sg * wd - wt * 0.5f, y0, -wd);
                Vector3 mx = alongX ? new Vector3(wd, y0 + wh, sg * wd + wt * 0.5f) : new Vector3(sg * wd + wt * 0.5f, y0 + wh, wd);
                Art.BoxMM(mb, mn, mx, Art.J(sand, rng));
                for (int i = 0; i < 2; i++)
                {
                    float u = (i == 0 ? -0.35f : 0.35f) * wd;
                    var p = alongX ? new Vector3(u, y0 + wh + ms * 0.5f, sg * wd) : new Vector3(sg * wd, y0 + wh + ms * 0.5f, u);
                    mb.Box(p, new Vector3(ms, ms, ms), sand);
                }
            }
            // the keep: two tiers, a cone roof and a little flag
            float k1 = 0.33f * F, k2 = 0.22f * F;
            Art.Column(mb, 0f, 0f, 0.3f * R, 0.3f * R, y0, k1, 10, 1f, 0.95f, Art.J(sand, rng), true);
            Merlons(mb, 0f, y0 + k1, 0f, 0.28f * R, 5, 0.09f * R, sand, 0f);
            Art.Column(mb, 0f, 0f, 0.19f * R, 0.19f * R, y0 + k1, k2, 8, 1f, 0.95f, Art.J(sand, rng), true);
            float top = y0 + k1 + k2, poleTop = Mathf.Min(1.12f * F, top + 0.34f * F), pr = Mathf.Max(0.0006f, 0.02f * R);
            Art.Column(mb, 0f, 0f, 0.22f * R, 0.22f * R, top, 0.14f * F, 8, 1f, 0f, wet, false);
            mb.Cylinder(new Vector3(0f, top + 0.1f * F, 0f), pr, pr, poleTop - (top + 0.1f * F), 4, pole);
            Vector3 f0 = new Vector3(0f, poleTop, 0f), f1 = new Vector3(0f, poleTop - 0.1f * F, 0f), f2 = new Vector3(0.24f * R, poleTop - 0.05f * F, 0f);
            Art.TriF(mb, f0, f1, f2, Vector3.forward, flag, 0.3f);
            Art.TriF(mb, f0, f1, f2, Vector3.back, flag, 0.3f);
        }

        static void SunCrystal(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F;
            Color stone = Art.Mix(Art.Stone(c), c.PropAccent, 0.25f), gold = Art.Vivid(Art.Mix(c.Emissive, new Color(1f, 0.75f, 0.25f), 0.5f), 0.7f, 1f), tip = Art.Lighten(gold, 0.35f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Tau / 8f;
                mb.Push(new Vector3(Mathf.Cos(a) * 0.72f * R, 0f, Mathf.Sin(a) * 0.72f * R), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), Vector3.one);
                mb.Box(new Vector3(0f, 0.05f * F, 0f), new Vector3(0.22f * R, 0.1f * F * rng.Range(0.8f, 1.1f), 0.32f * R), Art.J(stone, rng), Art.Lighten(stone, 0.1f));
                mb.Pop();
            }
            Art.Disc(mb, new Vector3(0f, 0.004f, 0f), Vector3.up, 0.5f * R, 8, gold, 0.6f);
            float cr = 0.22f * R, ch = 0.7f * F;
            mb.Cylinder(Vector3.zero, cr * 0.8f, cr, ch, 6, gold, false, true, 0.9f);
            mb.Cylinder(new Vector3(0f, ch, 0f), cr, 0f, 0.3f * F, 6, tip, emissive: 0.95f);
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f, h = 0.3f * F * rng.Range(0.75f, 1f);
                var d = new Vector3(Mathf.Cos(a) * 0.26f, 1f, Mathf.Sin(a) * 0.26f);
                mb.Push(new Vector3(Mathf.Cos(a) * 0.38f * R, 0f, Mathf.Sin(a) * 0.38f * R), Quaternion.FromToRotation(Vector3.up, d), Vector3.one);
                mb.Cylinder(Vector3.zero, 0.08f * R, 0.1f * R, h, 6, gold, false, true, 0.8f);
                mb.Cylinder(new Vector3(0f, h, 0f), 0.1f * R, 0f, 0.12f * F, 6, tip, emissive: 0.9f);
                mb.Pop();
            }
        }

        static void Snowman(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F;
            Color snow = Art.Mix(Art.Snow(c), new Color(0.95f, 0.97f, 1f), 0.5f), coal = new Color(0.08f, 0.08f, 0.1f);
            Color carrot = new Color(1f, 0.5f, 0.15f), scarf = c.Emissive, twig = Art.Darken(c.PropAccent, 0.2f);
            float hb = 0.7f * R, hm = 0.52f * R, hh = 0.36f * R;
            Art.IcoIn(mb, new Vector3(-hb, 0f, -hb), new Vector3(hb, 1.5f * hb, hb), snow, rng, 0.08f, 0.45f, 0.05f);
            float m0 = 0.98f * R, m1 = m0 + 1.5f * hm;
            Art.IcoIn(mb, new Vector3(-hm, m0, -hm), new Vector3(hm, m1, hm), snow, rng, 0.08f, 0.45f, 0.05f);
            float h0 = 1.7f * R, h1 = h0 + 1.7f * hh, hc = (h0 + h1) * 0.5f;
            Art.IcoIn(mb, new Vector3(-hh, h0, -hh), new Vector3(hh, h1, hh), snow, rng, 0.06f, 0f, 0.05f);
            // the scarf at the neck, with a tail
            Art.Column(mb, 0f, 0f, 0.4f * R, 0.4f * R, h0 - 0.04f * R, 0.1f * R, 8, 1f, 1f, scarf, false, 0.2f);
            Art.BoxMM(mb, new Vector3(0.12f * R, h0 - 0.4f * R, 0.3f * R), new Vector3(0.24f * R, h0 + 0.02f * R, 0.4f * R), scarf, null, 0.2f);
            // coal eyes, a carrot nose, coal buttons
            float e = 0.055f * R, ez = 0.8f * hh;
            for (int s = -1; s <= 1; s += 2)
                Art.IcoIn(mb, new Vector3(s * 0.13f * R - e, hc + 0.06f * R - e, ez - e), new Vector3(s * 0.13f * R + e, hc + 0.06f * R + e, ez + e), coal, null, 0f, 0f);
            mb.Push(new Vector3(0f, hc - 0.04f * R, 0.7f * hh), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.055f * R, 0f, 0.3f * R, 5, carrot, emissive: 0.1f);
            mb.Pop();
            for (int i = 0; i < 3; i++)
            {
                float y = m0 + (0.45f + 0.3f * i) * (m1 - m0) * 0.75f, z = 0.82f * hm * Mathf.Sqrt(Mathf.Max(0.2f, 1f - Mathf.Pow((y - (m0 + m1) * 0.5f) / (0.75f * (m1 - m0)), 2f)));
                mb.Box(new Vector3(0f, y, z), new Vector3(0.07f * R, 0.07f * R, 0.05f * R), coal);
            }
            // twig arms
            for (int s = -1; s <= 1; s += 2)
            {
                float ay = m0 + 0.55f * (m1 - m0);
                var a0 = new Vector3(s * 0.4f * R, ay, 0f);
                var a1 = new Vector3(s * 0.88f * R, ay + 0.35f * R, 0f);
                Art.Tube(mb, new[] { a0, a1 }, new[] { 0.035f * R, 0.025f * R }, 4, twig);
                Art.Tube(mb, new[] { Vector3.Lerp(a0, a1, 0.7f), Vector3.Lerp(a0, a1, 0.7f) + new Vector3(s * 0.05f * R, 0.16f * R, 0f) }, new[] { 0.02f * R, 0.015f * R }, 4, twig);
            }
        }

        static void StarMap(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F, bh = 0.08f * F;
            Color metal = Art.Metal(c), dark = Art.Darken(c.Prop, 0.5f), holo = c.Emissive, planet = Art.Vivid(c.PropAccent, 0.6f, 0.95f);
            mb.Cylinder(Vector3.zero, 0.6f * R, 0.5f * R, bh, 10, dark, true, false, 0f, 0f, 0f, 0f, metal);
            Art.Ring(mb, new Vector3(0f, bh + 0.0005f, 0f), Vector3.up, 0.3f * R, 0.42f * R, 12, holo, 1f);
            float gy = bh + 0.05f * F + 0.85f * R, gr = 0.82f * R, band = 0.05f * R;
            var center = new Vector3(0f, gy, 0f);
            Quaternion[] rots = { Quaternion.Euler(12f, 0f, 8f), Quaternion.Euler(90f, 30f, 0f), Quaternion.Euler(90f, -45f, 0f) };
            for (int i = 0; i < rots.Length; i++)
            {
                mb.Push(center, rots[i], Vector3.one);
                Art.Ring(mb, Vector3.zero, Vector3.up, gr - band, gr, 24, holo, 1f, true);
                mb.Pop();
            }
            mb.Ico(center, Vector3.one * 0.28f * R, planet, rng, 0.08f, 1f);
            float ma = rng.Range(0f, Tau);
            mb.Ico(center + rots[0] * new Vector3(Mathf.Cos(ma), 0f, Mathf.Sin(ma)) * (gr - band * 0.5f), Vector3.one * 0.08f * R, holo, rng, 0.05f, 1f);
            for (int i = 0; i < 6; i++)
            {
                var d = new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                if (d.sqrMagnitude < 0.01f) d = Vector3.right;
                mb.Box(center + d.normalized * rng.Range(0.4f, 0.7f) * R, Vector3.one * 0.04f * R, holo, null, 1f);
            }
        }

        static void AnemoneGlow(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F, top = 0.35f * F;
            Color body = Art.Vivid(c.PropAccent, 0.55f, 0.75f), arm = Art.Vivid(c.Prop, 0.55f, 0.85f), glow = Art.Vivid(c.Emissive, 0.5f, 1f);
            Color rock = Art.Dull(Art.Mix(c.GroundAccent, c.PropAccent, 0.3f), 0.2f);
            Art.IcoIn(mb, new Vector3(-0.68f * R, 0f, -0.65f * R), new Vector3(0.68f * R, 0.18f * F, 0.65f * R), rock, rng, 0.15f, 0.4f);
            Art.Column(mb, 0f, 0f, 0.4f * R, 0.4f * R, 0.1f * F, top - 0.1f * F, 10, 1f, 0.85f, body, true, 0.1f, 0f, 0.15f, 0f, Art.Lighten(body, 0.25f));
            for (int i = 0; i < 24; i++)
            {
                bool outer = (i & 1) == 0;
                float a = i * Tau / 24f + rng.Range(-0.1f, 0.1f), rr0 = outer ? 0.28f * R : 0.15f * R;
                float t = Mathf.Deg2Rad * (outer ? rng.Range(35f, 55f) : rng.Range(10f, 25f));
                var d = new Vector3(Mathf.Cos(a) * Mathf.Sin(t), Mathf.Cos(t), Mathf.Sin(a) * Mathf.Sin(t));
                float L = Mathf.Min(0.55f * F * rng.Range(0.8f, 1f), Mathf.Min((0.86f * R - rr0) / Mathf.Sin(t), 0.8f * F / Mathf.Cos(t)));
                mb.Push(new Vector3(Mathf.Cos(a) * rr0, top, Mathf.Sin(a) * rr0), Quaternion.FromToRotation(Vector3.up, d), Vector3.one);
                mb.Cylinder(Vector3.zero, 0.05f * R, 0.025f * R, 0.82f * L, 4, Art.J(arm, rng), false, false, 0.15f, 0.3f, 0.9f);
                mb.Cylinder(new Vector3(0f, 0.82f * L, 0f), 0.025f * R, 0f, 0.18f * L, 4, glow, false, false, 1f, 0.9f, 1f);
                mb.Pop();
            }
        }

        static void Jukebox(MeshBuilder mb, WorldColors c, float F)
        {
            float R = 0.5f * F, hw = 0.575f * R, hd = 0.275f * R, bh = 0.62f * F, foot = 0.05f * F;
            float o1 = Mathf.Max(0.0004f, 0.0015f * F), o2 = 2f * o1;
            Color wood = Art.Darken(c.PropAccent, 0.2f), chrome = Art.Lighten(Art.Metal(c), 0.2f), dark = new Color(0.1f, 0.08f, 0.1f);
            Art.BoxMM(mb, new Vector3(-hw, 0f, -hd), new Vector3(hw, foot, hd), dark);
            Art.BoxMM(mb, new Vector3(-hw, foot, -hd), new Vector3(hw, bh, hd), wood);
            // the arched top, its front in glowing bands
            const int A = 12;
            float[] fr = { 0f, 0.35f, 0.55f, 0.75f, 0.9f, 1f };
            Color[] fc = { chrome, c.Neon, c.Emissive, Art.Vivid(c.PropAccent, 0.6f, 1f), wood };
            float[] fe = { 0.3f, 1f, 1f, 1f, 0f };
            for (int k = 0; k < A; k++)
            {
                float t0 = Mathf.PI * k / A, t1 = Mathf.PI * (k + 1) / A, tm = (t0 + t1) * 0.5f;
                Vector3 d0 = new Vector3(Mathf.Cos(t0), Mathf.Sin(t0), 0f), d1 = new Vector3(Mathf.Cos(t1), Mathf.Sin(t1), 0f);
                Vector3 b = new Vector3(0f, bh, 0f);
                Art.QuadF(mb, b + d0 * hw + Vector3.back * hd, b + d0 * hw + Vector3.forward * hd, b + d1 * hw + Vector3.forward * hd, b + d1 * hw + Vector3.back * hd,
                          new Vector3(Mathf.Cos(tm), Mathf.Sin(tm), 0f), wood);
                Art.TriF(mb, b + Vector3.back * hd, b + d0 * hw + Vector3.back * hd, b + d1 * hw + Vector3.back * hd, Vector3.back, wood);
                for (int r = 0; r < 5; r++)
                {
                    Vector3 z = Vector3.forward * hd;
                    Art.QuadF(mb, b + d0 * (hw * fr[r]) + z, b + d0 * (hw * fr[r + 1]) + z, b + d1 * (hw * fr[r + 1]) + z, b + d1 * (hw * fr[r]) + z, Vector3.forward, fc[r], fe[r]);
                }
            }
            // a grille, selector buttons and glowing side pillars
            Art.RectZ(mb, hd + o1, -0.7f * hw, 0.7f * hw, 0.12f * F, 0.4f * F, 1f, dark);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.14f * F + i * 0.05f * F;
                Art.RectZ(mb, hd + o2, -0.65f * hw, 0.65f * hw, y, y + 0.015f * F, 1f, chrome, 0.2f);
            }
            for (int i = 0; i < 5; i++)
            {
                float x = -0.5f * hw + i * 0.25f * hw, s = 0.04f * R;
                Art.BoxMM(mb, new Vector3(x - s, 0.47f * F, hd), new Vector3(x + s, 0.47f * F + 2f * s, hd + o2 + s), i == 2 ? c.Neon : c.Emissive, null, 0.6f);
            }
            for (int sd = -1; sd <= 1; sd += 2)
                Art.Column(mb, sd * (hw - 0.06f * R), hd - 0.06f * R, 0.06f * R, 0.06f * R, foot, bh - foot, 6, 1f, 1f, c.Neon, false, 0.8f);
        }

        static void BlossomTree(MeshBuilder mb, WorldColors c, Rng rng, float F)
        {
            float R = 0.5f * F, potH = 0.16f * F;
            Color pot = Art.Vivid(Art.Mix(c.PropAccent, new Color(0.35f, 0.45f, 0.6f), 0.5f), 0.4f, 0.55f), soil = new Color(0.22f, 0.16f, 0.12f), bark = Art.Darken(c.PropAccent, 0.25f);
            Color pink = Art.Mix(new Color(1f, 0.7f, 0.82f), c.Emissive, 0.15f), pink2 = Art.Lighten(pink, 0.25f);
            Art.Column(mb, 0f, 0f, 0.7f * R, 0.7f * R, 0f, potH, 10, 0.85f, 1f, pot, true, 0f, 0f, 0f, 0f, soil);
            Art.Column(mb, 0f, 0f, 0.74f * R, 0.74f * R, potH - 0.025f * F, 0.025f * F, 10, 1f, 1f, Art.Lighten(pot, 0.15f), false);
            var trunk = new[]
            {
                new Vector3(0f, potH - 0.01f * F, 0f), new Vector3(0.12f * R, 0.3f * F, 0.05f * R), new Vector3(-0.08f * R, 0.45f * F, -0.02f * R), new Vector3(0.05f * R, 0.62f * F, 0f)
            };
            Art.Tube(mb, trunk, new[] { 0.12f * R, 0.09f * R, 0.075f * R, 0.06f * R }, 6, bark, null, 0f, false, true);
            Vector3[] tips =
            {
                new Vector3(0.48f * R, 0.6f * F, 0.15f * R), new Vector3(-0.5f * R, 0.66f * F, -0.12f * R), new Vector3(-0.1f * R, 0.68f * F, 0.48f * R)
            };
            Vector3[] from = { trunk[1], trunk[2], trunk[2] };
            for (int i = 0; i < 3; i++) Art.Tube(mb, new[] { from[i], tips[i] }, new[] { 0.055f * R, 0.03f * R }, 5, bark);
            Vector3[] blooms =
            {
                new Vector3(0.05f * R, 0.8f * F, 0f), tips[0], tips[1], tips[2],
                new Vector3(0.3f * R, 0.75f * F, -0.3f * R), new Vector3(-0.3f * R, 0.78f * F, 0.25f * R), new Vector3(0.1f * R, 0.7f * F, 0.35f * R)
            };
            float b = 0.28f * R;
            for (int i = 0; i < blooms.Length; i++)
            {
                Vector3 p = blooms[i], hs = new Vector3(b, 0.75f * b, b) * rng.Range(0.85f, 1f);
                Art.IcoIn(mb, p - hs, p + hs, Art.J((i & 1) == 0 ? pink : pink2, rng), rng, 0.15f, 0f, 0.15f, 0.5f, 1f);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = rng.Range(0f, Tau), d = rng.Range(0.25f, 0.55f) * R;
                Art.Disc(mb, new Vector3(Mathf.Cos(a) * d, potH + 0.0005f, Mathf.Sin(a) * d), Vector3.up, 0.05f * R, 5, pink2, 0.1f);
            }
        }

        // ---------- souvenir-only ----------

        static void MiniLantern(MeshBuilder mb, WorldColors c)
        {
            Color wood = Art.Darken(c.PropAccent, 0.25f), paper = Art.Lighten(Art.Vivid(c.Emissive, 0.6f, 0.95f), 0.1f);
            Art.BoxMM(mb, new Vector3(-0.03f, 0f, -0.03f), new Vector3(0.03f, 0.008f, 0.03f), wood);
            Art.BoxMM(mb, new Vector3(-0.024f, 0.008f, -0.024f), new Vector3(0.024f, 0.07f, 0.024f), paper, null, 0.9f);
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                {
                    float x = a == 0 ? -0.027f : 0.021f, z = b == 0 ? -0.027f : 0.021f;
                    Art.BoxMM(mb, new Vector3(x, 0.008f, z), new Vector3(x + 0.006f, 0.072f, z + 0.006f), wood);
                }
            mb.Cylinder(new Vector3(0f, 0.072f, 0f), 0.045f, 0f, 0.022f, 4, wood, true, true, 0f, 0f, 0f, Mathf.PI * 0.25f);
            mb.Box(new Vector3(0f, 0.097f, 0f), new Vector3(0.008f, 0.008f, 0.008f), wood);
        }
    }
}

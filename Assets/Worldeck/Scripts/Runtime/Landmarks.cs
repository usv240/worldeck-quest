using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The one thing on the horizon that makes a world a real place (Atlas, Look.Landmark): the pyramids, an iron
    /// tower, a white dome, a pagoda. Built from the same flat-shaded shapes as everything else, in metres, standing
    /// on the origin with its front towards +Z (the player). Impressions in the app's own style, not models of the
    /// real buildings.
    /// </summary>
    public static class Landmarks
    {
        static Color H(string hex) => WorldColors.Hex(hex, Color.gray);
        const float Q = Mathf.PI * 0.25f;   // a square prism with its faces to the axes

        /// <summary>How far away it stands (metres).</summary>
        public static float Distance(string kind)
        {
            switch (kind)
            {
                case "peak": return 300f;
                case "pyramids": return 170f;
                case "dome": return 140f;   // its garden runs 100 m towards you: further off, so that ends well short of your room
                case "wall": return 150f;
                case "step_pyramid": return 105f;
                case "spire": return 190f;
                case "tower": return 150f;
                case "stone_circle": return 46f;
                case "windmill": return 60f;
                case "lighthouse": return 85f;
                default: return 120f;
            }
        }

        /// <summary>The ground it takes up (metres, radius): nothing else is scattered there.</summary>
        public static float Radius(string kind)
        {
            switch (kind)
            {
                case "peak": return 130f;
                case "pyramids": return 120f;
                case "wall": return 120f;
                case "step_pyramid": case "mosque": return 45f;
                case "onion_domes": return 36f;
                case "spire": return 45f;
                case "colosseum": return 50f;
                case "dome": return 64f;
                case "tower": return 32f;
                case "stone_circle": return 20f;
                case "windmill": case "lighthouse": return 14f;
                default: return 30f;
            }
        }

        /// <summary>How far in front of it (towards you) its forecourt runs: nothing is scattered there. 0 for none.</summary>
        public static float Forecourt(string kind) => kind == "dome" ? 102f : 0f;

        /// <summary>A pointed arch in the XY plane at the push's origin, facing +Z: an opening or a blind niche. Seen from both sides.</summary>
        static void Arch(MeshBuilder mb, float width, float straight, float point, Color c)
        {
            float w = width * 0.5f;
            Vector3 bl = new Vector3(-w, 0f, 0f), br = new Vector3(w, 0f, 0f), tl = new Vector3(-w, straight, 0f), tr = new Vector3(w, straight, 0f);
            Vector3 sl = new Vector3(-w * 0.62f, straight + point * 0.62f, 0f), sr = new Vector3(w * 0.62f, straight + point * 0.62f, 0f), top = new Vector3(0f, straight + point, 0f);
            mb.Quad(bl, tl, tr, br, c); mb.Quad(br, tr, tl, bl, c);
            foreach (var (a, b, d) in new[] { (tl, sl, tr), (sl, sr, tr), (sl, top, sr) })
            {
                mb.Tri(a, b, d, c); mb.Tri(a, d, b, c);
            }
        }

        /// <summary>
        /// The white marble palace of India's morning: a red terrace, the marble plinth, a hall with a great arched
        /// doorway on each face and niches on its cut corners, the onion dome with four small domed kiosks round it,
        /// a balconied minaret at each corner of the plinth, and the long water with cypress rows running towards you.
        /// </summary>
        static void Palace(MeshBuilder mb)
        {
            Color marble = H("#F6F1E6"), shade = H("#DED6C6"), deep = H("#7E786E"), sand = H("#B5694C"), sandDark = H("#96533C"), gold = H("#E8C46A");
            Color water = H("#86B4D2"), path = H("#D8C6A4"), cypress = H("#2C573A");
            const float O = Mathf.PI / 8f;   // an eight-sided prism with a face to each axis
            // The terrace and the plinth.
            mb.Box(new Vector3(0f, 0.1f, 0f), new Vector3(124f, 5.8f, 100f), sand, sandDark);            // top at 3
            mb.Box(new Vector3(0f, 6f, 0f), new Vector3(88f, 6f, 88f), marble, shade);               // top at 9
            mb.Box(new Vector3(0f, 4.5f, 46f), new Vector3(16f, 9f, 8f), shade);                     // the stair up to it
            const float y0 = 9f, hall = 30f;
            // The hall: a square with its corners cut.
            mb.Cylinder(new Vector3(0f, y0, 0f), 25f, 25f, hall, 8, marble, true, false, 0f, 0f, 0f, O, shade);
            mb.Cylinder(new Vector3(0f, y0 + hall, 0f), 25.8f, 25.8f, 1.6f, 8, shade, true, false, 0f, 0f, 0f, O);
            for (int k = 0; k < 4; k++)
            {
                // Each face: a tall frame standing proud of the wall, a deep pointed doorway in it.
                mb.Push(Vector3.zero, Quaternion.Euler(0f, k * 90f, 0f), Vector3.one);
                mb.Box(new Vector3(0f, y0 + 17.5f, 23.6f), new Vector3(18f, 35f, 2.6f), marble, shade);
                mb.Push(new Vector3(0f, y0 + 1.5f, 24.96f)); Arch(mb, 11f, 17f, 8f, deep); mb.Pop();
                mb.Push(new Vector3(0f, y0 + 1.5f, 24.99f)); Arch(mb, 4.6f, 7.5f, 3.2f, H("#4E4A44")); mb.Pop();   // the door inside it
                mb.Pop();
                // Each cut corner: two niches, one above the other.
                mb.Push(Vector3.zero, Quaternion.Euler(0f, 45f + k * 90f, 0f), Vector3.one);
                foreach (float ny in new[] { y0 + 2f, y0 + 16f })
                {
                    mb.Push(new Vector3(0f, ny, 23.2f)); Arch(mb, 6f, 6.5f, 3.6f, deep); mb.Pop();
                }
                mb.Pop();
            }
            // The dome: a drum, then the bulb swelling out and drawing in to a point.
            float y = y0 + hall + 1.6f;
            mb.Cylinder(new Vector3(0f, y, 0f), 12.2f, 12.2f, 7f, 24, marble, false);
            mb.Cylinder(new Vector3(0f, y + 6f, 0f), 12.8f, 12.8f, 1.2f, 24, shade, true);
            y += 7f;
            var rings = new[] { (12.2f, 14.4f, 3.2f), (14.4f, 15.3f, 4f), (15.3f, 14.6f, 4f), (14.6f, 12.2f, 4f), (12.2f, 8.2f, 3.8f), (8.2f, 3.4f, 3.4f), (3.4f, 0.9f, 2.6f) };
            foreach (var (r0, r1, h) in rings) { mb.Cylinder(new Vector3(0f, y, 0f), r0, r1, h, 24, marble, false); y += h; }
            mb.Cylinder(new Vector3(0f, y, 0f), 1.1f, 0.5f, 2.4f, 8, gold, true, false, 0.3f);
            mb.Cylinder(new Vector3(0f, y + 2.4f, 0f), 0.5f, 0f, 5.2f, 6, gold, true, false, 0.3f);
            foreach (var (sx, sz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
            {
                // A small domed kiosk on the roof beside the great dome: eight posts under a little bulb.
                var at = new Vector3(sx * 15.5f, y0 + hall + 1.6f, sz * 15.5f);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f + O;
                    mb.Cylinder(at + new Vector3(Mathf.Cos(a) * 3.3f, 0f, Mathf.Sin(a) * 3.3f), 0.38f, 0.38f, 5f, 4, marble, false);
                }
                mb.Cylinder(at + Vector3.up * 5f, 4.3f, 4.3f, 0.8f, 8, shade, true, true, 0f, 0f, 0f, O);
                mb.Cylinder(at + Vector3.up * 5.8f, 3.4f, 4f, 1.4f, 12, marble, false);
                mb.Cylinder(at + Vector3.up * 7.2f, 4f, 2.6f, 1.8f, 12, marble, false);
                mb.Cylinder(at + Vector3.up * 9f, 2.6f, 0f, 2.2f, 12, marble);
                mb.Cylinder(at + Vector3.up * 11.2f, 0.25f, 0f, 2f, 6, gold, true, false, 0.3f);
                // A minaret at the corner of the plinth: three balconies, a kiosk on top.
                var foot = new Vector3(sx * 40f, y0, sz * 40f);
                mb.Cylinder(foot, 3.4f, 3.4f, 1.6f, 8, shade, true);
                float my = y0 + 1.6f, r = 2.7f;
                foreach (float seg in new[] { 14f, 13f, 12f })
                {
                    mb.Cylinder(new Vector3(foot.x, my, foot.z), r, r - 0.3f, seg, 12, marble, false);
                    my += seg; r -= 0.3f;
                    mb.Cylinder(new Vector3(foot.x, my - 0.4f, foot.z), r + 1.15f, r + 1.15f, 0.9f, 12, shade, true, true);
                }
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    mb.Cylinder(new Vector3(foot.x + Mathf.Cos(a) * 1.5f, my + 0.5f, foot.z + Mathf.Sin(a) * 1.5f), 0.22f, 0.22f, 3f, 4, marble, false);
                }
                mb.Cylinder(new Vector3(foot.x, my + 3.5f, foot.z), 2.3f, 2.3f, 0.5f, 10, shade, true, true);
                mb.Cylinder(new Vector3(foot.x, my + 4f, foot.z), 1.8f, 2.1f, 1f, 10, marble, false);
                mb.Cylinder(new Vector3(foot.x, my + 5f, foot.z), 2.1f, 0f, 2.6f, 10, marble);
                mb.Cylinder(new Vector3(foot.x, my + 7.6f, foot.z), 0.2f, 0f, 1.8f, 6, gold, true, false, 0.3f);
            }
            // The garden in front: the long water between two paths, a row of cypresses either side.
            const float z0 = 50f, len = 40f;
            mb.Box(new Vector3(0f, -1.2f, z0 + len * 0.5f), new Vector3(8f, 3.5f, len), water, null, 0.12f);
            foreach (float px in new[] { -7.5f, 7.5f }) mb.Box(new Vector3(px, -1.15f, z0 + len * 0.5f), new Vector3(7f, 3.5f, len), path);
            mb.Box(new Vector3(0f, -1.1f, z0 + len + 5f), new Vector3(22f, 3.7f, 10f), path, shade);   // the raised pool at the near end
            mb.Box(new Vector3(0f, -1.05f, z0 + len + 5f), new Vector3(9f, 3.7f, 6f), water, null, 0.12f);
            for (float cz = z0 + 4f; cz < z0 + len; cz += 8f)
                foreach (float cx in new[] { -13.5f, 13.5f })
                {
                    mb.Cylinder(new Vector3(cx, -2f, cz), 0.45f, 0.45f, 3.4f, 5, H("#5A4634"), false);
                    mb.Cylinder(new Vector3(cx, 1.2f, cz), 1.7f, 1.2f, 3f, 6, cypress, false, true);
                    mb.Cylinder(new Vector3(cx, 4.2f, cz), 1.2f, 0f, 6.2f, 6, cypress);
                }
        }

        public static bool Has(string kind) => System.Array.IndexOf(Atlas.Landmarks, kind ?? "") >= 0;

        /// <summary>Adds the landmark to the builder (in its own space). <paramref name="ground"/> tints the lower slopes of a peak.</summary>
        public static void Build(MeshBuilder mb, string kind, Color ground, Rng rng)
        {
            switch (kind)
            {
                case "pyramids":
                {
                    Color sand = H("#D9B77E"), shade = H("#C29A62");
                    mb.Cylinder(new Vector3(0f, 0f, 0f), 78f, 0f, 96f, 4, sand, false, false, 0f, 0f, 0f, Q);
                    mb.Cylinder(new Vector3(-118f, 0f, 46f), 54f, 0f, 66f, 4, shade, false, false, 0f, 0f, 0f, Q);
                    mb.Cylinder(new Vector3(104f, 0f, 62f), 34f, 0f, 42f, 4, sand, false, false, 0f, 0f, 0f, Q);
                    break;
                }
                case "tower":
                {
                    // An iron lattice tower: four splayed legs under two platforms, tapering to a mast.
                    Color iron = H("#5E4E44"), dark = H("#3E342E"), lamp = H("#6E5E52");
                    const float lit = 0f;   // unlit: a dark lattice against the dusk
                    foreach (var (sx, sz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                    {
                        mb.Push(new Vector3(sx * 17f, 0f, sz * 17f), Quaternion.Euler(sz * -17f, 0f, sx * 17f), Vector3.one);
                        mb.Cylinder(Vector3.zero, 4.2f, 2.6f, 36f, 4, iron, false, false, lit, 0f, 0f, Q);
                        mb.Pop();
                    }
                    mb.Box(new Vector3(0f, 34f, 0f), new Vector3(30f, 2.4f, 30f), dark, null, lit, true);
                    mb.Cylinder(new Vector3(0f, 35f, 0f), 13f, 6.5f, 34f, 4, iron, false, false, lit, 0f, 0f, Q);
                    mb.Box(new Vector3(0f, 69.5f, 0f), new Vector3(13f, 1.8f, 13f), dark, null, lit, true);
                    mb.Cylinder(new Vector3(0f, 70f, 0f), 6f, 1.4f, 62f, 4, iron, false, false, lit, 0f, 0f, Q);
                    mb.Box(new Vector3(0f, 132.5f, 0f), new Vector3(4f, 3f, 4f), lamp, null, 0f, true);
                    mb.Cylinder(new Vector3(0f, 134f, 0f), 0.6f, 0.2f, 14f, 4, dark);
                    break;
                }
                case "dome":
                case "mosque":
                {
                    if (kind == "dome") { Palace(mb); break; }
                    // A white marble palace: a great bulbed dome over a square hall, a slim tower at each corner.
                    // Or (mosque) a stone hall under a broad lead-blue dome, with taller, thinner minarets.
                    bool mosque = kind == "mosque";
                    Color marble = mosque ? H("#CFC6B2") : H("#F1ECE0"), shade = mosque ? H("#B0A792") : H("#D9D2C2"), gold = H("#E8C46A");
                    Color roof = mosque ? H("#7089A0") : marble;
                    mb.Box(new Vector3(0f, 3f, 0f), new Vector3(78f, 6f, 78f), shade);
                    mb.Box(new Vector3(0f, 19f, 0f), new Vector3(mosque ? 48f : 40f, 26f, mosque ? 48f : 40f), marble);
                    mb.Box(new Vector3(0f, 16f, (mosque ? 24f : 20f) + 0.2f), new Vector3(11f, 18f, 0.8f), H("#8A8478"));   // the great doorway
                    if (mosque)
                    {
                        mb.Cylinder(new Vector3(0f, 32f, 0f), 20f, 20f, 4f, 16, marble, false);
                        mb.Cylinder(new Vector3(0f, 36f, 0f), 20f, 17f, 5f, 16, roof, false);
                        mb.Cylinder(new Vector3(0f, 41f, 0f), 17f, 10.5f, 5f, 16, roof, false);
                        mb.Cylinder(new Vector3(0f, 46f, 0f), 10.5f, 0f, 4f, 16, roof);
                        mb.Cylinder(new Vector3(0f, 50f, 0f), 0.5f, 0f, 5f, 6, gold, true, false, 0.3f);
                    }
                    else
                    {
                        mb.Cylinder(new Vector3(0f, 32f, 0f), 13f, 15.5f, 6f, 16, marble, false);
                        mb.Cylinder(new Vector3(0f, 38f, 0f), 15.5f, 14f, 7f, 16, marble, false);
                        mb.Cylinder(new Vector3(0f, 45f, 0f), 14f, 8f, 7f, 16, marble, false);
                        mb.Cylinder(new Vector3(0f, 52f, 0f), 8f, 0f, 8f, 16, marble);
                        mb.Cylinder(new Vector3(0f, 59f, 0f), 0.6f, 0f, 6f, 6, gold, true, false, 0.3f);
                    }
                    float tall = mosque ? 58f : 40f, thick = mosque ? 1.9f : 2.6f;
                    foreach (var (sx, sz) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                    {
                        var at = new Vector3(sx * 35f, 6f, sz * 35f);
                        mb.Cylinder(at, thick, thick * 0.77f, tall, 8, marble, false);
                        mb.Cylinder(at + Vector3.up * (tall * 0.62f), thick * 1.35f, thick * 1.35f, 1.2f, 8, shade);
                        mb.Cylinder(at + Vector3.up * tall, thick * 1.25f, thick * 1.25f, 1.2f, 8, shade);
                        mb.Cylinder(at + Vector3.up * (tall + 1.2f), thick, 0f, mosque ? 10f : 5f, 8, roof);
                        var small = new Vector3(sx * (mosque ? 17f : 14f), 32f, sz * (mosque ? 17f : 14f));
                        mb.Cylinder(small, 4f, 4.6f, 2.5f, 10, marble, false);
                        mb.Cylinder(small + Vector3.up * 2.5f, 4.6f, 0f, mosque ? 3.4f : 5f, 10, roof);
                    }
                    break;
                }
                case "onion_domes":
                {
                    // A cathedral of painted towers, each under its own onion dome.
                    Color brick = H("#B5523E"), trim = H("#EFE6D6"), gold = H("#E8C46A");
                    mb.Box(new Vector3(0f, 2f, 0f), new Vector3(54f, 4f, 46f), trim);
                    mb.Box(new Vector3(0f, 12f, 0f), new Vector3(42f, 16f, 34f), brick, trim);
                    mb.Box(new Vector3(0f, 10f, 17.2f), new Vector3(7f, 12f, 0.8f), H("#5A4038"));
                    var towers = new[]
                    {
                        (0f, 0f, 6f, 46f, "#E8C46A"), (-15f, 11f, 4.2f, 30f, "#3F7FBF"), (15f, 11f, 4.2f, 32f, "#4FA36A"),
                        (-15f, -11f, 4.2f, 35f, "#D9483A"), (15f, -11f, 4.2f, 29f, "#7EC3D9")
                    };
                    foreach (var (x, z, r, h, hex) in towers)
                    {
                        Color dome = H(hex);
                        var at = new Vector3(x, 4f, z);
                        mb.Cylinder(at, r, r * 0.9f, h, 8, brick, false);
                        mb.Cylinder(at + Vector3.up * h, r * 1.15f, r * 1.15f, 1.4f, 8, trim);
                        float y = h + 1.4f;
                        mb.Cylinder(at + Vector3.up * y, r * 0.85f, r * 1.45f, r * 0.9f, 10, dome, false);
                        mb.Cylinder(at + Vector3.up * (y + r * 0.9f), r * 1.45f, r * 0.95f, r * 0.9f, 10, dome, false);
                        mb.Cylinder(at + Vector3.up * (y + r * 1.8f), r * 0.95f, 0f, r * 1.7f, 10, dome);
                        mb.Cylinder(at + Vector3.up * (y + r * 3.4f), 0.4f, 0f, r * 1.1f, 6, gold, true, false, 0.3f);
                    }
                    break;
                }
                case "step_pyramid":
                {
                    // A stepped temple pyramid: nine terraces, a stair up the front, a small temple on top.
                    Color stone = H("#B9B2A0"), shade = H("#9E9786"), stair = H("#CFC8B6"), dark = H("#4A463E");
                    for (int i = 0; i < 9; i++)
                    {
                        float w = 62f - i * 5.6f, y = 1.6f + i * 3.2f;
                        mb.Box(new Vector3(0f, y, 0f), new Vector3(w, 3.2f, w), i % 2 == 0 ? stone : shade);
                        mb.Box(new Vector3(0f, y, w * 0.5f + 0.9f), new Vector3(9f, 3.2f, 4.6f), stair);
                    }
                    mb.Box(new Vector3(0f, 31.8f, 0f), new Vector3(13f, 6f, 11f), stone, shade);
                    mb.Box(new Vector3(0f, 31f, 5.6f), new Vector3(3.6f, 4.4f, 0.5f), dark, null, 0f, true);
                    break;
                }
                case "wall":
                {
                    // A great wall along the crest of its own ridge, a watchtower every few turns.
                    Color stone = H("#B7AB94"), shade = H("#93876F"), dark = H("#4A463E");
                    Color hill = Color.Lerp(ground, H("#5E7A4A"), 0.5f) * 0.85f, back = hill * 0.8f;
                    hill.a = back.a = 1f;
                    const int n = 12;
                    var top = new Vector3[n + 1];
                    for (int j = 0; j <= n; j++)
                        top[j] = new Vector3(-168f + j * 28f, 20f + 13f * Mathf.Sin(j * 0.9f + 0.6f) + 7f * Mathf.Sin(j * 2.1f), 9f * Mathf.Sin(j * 1.3f));
                    for (int j = 0; j < n; j++)
                    {
                        Vector3 a = top[j], b = top[j + 1];
                        float fa = a.y * 1.7f + 14f, fb = b.y * 1.7f + 14f;
                        Vector3 af = new Vector3(a.x, -2f, a.z + fa), bf = new Vector3(b.x, -2f, b.z + fb);
                        Vector3 ab = new Vector3(a.x, -2f, a.z - fa), bb = new Vector3(b.x, -2f, b.z - fb);
                        mb.Quad(bf, b, a, af, hill);
                        mb.Quad(ab, a, b, bb, back);
                        if (j == 0) mb.Tri(af, a, ab, back);
                        if (j == n - 1) mb.Tri(bb, b, bf, back);
                        Vector3 along = b - a;
                        mb.Push((a + b) * 0.5f, Quaternion.LookRotation(along.normalized, Vector3.up), Vector3.one);
                        mb.Box(new Vector3(0f, 2.6f, 0f), new Vector3(5f, 7f, along.magnitude + 0.8f), stone, shade, 0f, true);
                        mb.Pop();
                        if (j % 3 == 1)
                        {
                            mb.Box(a + Vector3.up * 5.5f, new Vector3(9f, 13f, 9f), stone, shade, 0f, true);
                            mb.Box(a + Vector3.up * 12.7f, new Vector3(10.6f, 1.4f, 10.6f), shade);
                            mb.Box(a + new Vector3(0f, 8.5f, 4.6f), new Vector3(2.2f, 3f, 0.4f), dark, null, 0f, true);
                        }
                    }
                    break;
                }
                case "pagoda":
                {
                    Color wall = H("#C2463A"), roof = H("#34363F"), trim = H("#E8C46A");
                    mb.Box(new Vector3(0f, 1f, 0f), new Vector3(26f, 2f, 26f), H("#9A948A"));
                    for (int i = 0; i < 5; i++)
                    {
                        float w = 17f - i * 2.6f, y = 2f + i * 9f;
                        mb.Box(new Vector3(0f, y + 3f, 0f), new Vector3(w, 6f, w), wall);
                        mb.Cylinder(new Vector3(0f, y + 6f, 0f), w * 1.12f, w * 0.42f, 3f, 4, roof, true, true, 0f, 0f, 0f, Q);
                    }
                    mb.Cylinder(new Vector3(0f, 47f, 0f), 1.6f, 0f, 10f, 6, trim, true, false, 0.25f);
                    break;
                }
                case "clock_tower":
                {
                    Color stone = H("#B89C72"), dark = H("#5A5048"), face = H("#FFF3CF");
                    mb.Box(new Vector3(0f, 36f, 0f), new Vector3(13f, 72f, 13f), stone);
                    mb.Box(new Vector3(0f, 77f, 0f), new Vector3(15f, 10f, 15f), stone, dark);
                    foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                        mb.Box(new Vector3(0f, 63f, 0f) + d * 6.6f, new Vector3(d.x != 0f ? 0.5f : 8.5f, 8.5f, d.z != 0f ? 0.5f : 8.5f), face, null, 0.85f, true);
                    mb.Cylinder(new Vector3(0f, 82f, 0f), 10.6f, 0f, 24f, 4, dark, false, false, 0f, 0f, 0f, Q);
                    // The long hall beside it.
                    mb.Box(new Vector3(-34f, 11f, -4f), new Vector3(54f, 22f, 16f), stone, dark);
                    break;
                }
                case "windmill":
                {
                    Color body = H("#EDE4D2"), wood = H("#6A4A32"), sail = H("#F4EEE2");
                    mb.Cylinder(Vector3.zero, 6.2f, 3.6f, 19f, 8, body, false);
                    mb.Cylinder(new Vector3(0f, 19f, 0f), 4.2f, 0f, 4.6f, 8, wood);
                    for (int k = 0; k < 4; k++)
                    {
                        mb.Push(new Vector3(0f, 18.5f, 4.4f), Quaternion.Euler(0f, 0f, 28f + k * 90f), Vector3.one);
                        mb.Box(new Vector3(0f, 8.5f, 0f), new Vector3(0.5f, 17f, 0.4f), wood, null, 0f, true);
                        mb.Box(new Vector3(1.6f, 10f, 0f), new Vector3(2.8f, 12f, 0.2f), sail, null, 0f, true);
                        mb.Pop();
                    }
                    break;
                }
                case "arch":
                {
                    Color stone = H("#DCD0B8"), shade = H("#C2B49A");
                    mb.Box(new Vector3(-15f, 18f, 0f), new Vector3(13f, 36f, 16f), stone);
                    mb.Box(new Vector3(15f, 18f, 0f), new Vector3(13f, 36f, 16f), stone);
                    mb.Box(new Vector3(0f, 43f, 0f), new Vector3(43f, 14f, 16f), stone, null, 0f, true);
                    mb.Box(new Vector3(0f, 51.5f, 0f), new Vector3(47f, 3f, 18f), shade);
                    break;
                }
                case "colosseum":
                {
                    Color stone = H("#D2C3A6"), dark = H("#55483C");
                    const int n = 28;
                    // Part of the outer ring has fallen: the far side stands full height, the near side two storeys.
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                        float h = Mathf.Sin(a) < -0.2f ? 22f : 33f;
                        Vector3 d0 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                        mb.Quad(d0 * 44f, d0 * 44f + Vector3.up * h, d1 * 44f + Vector3.up * h, d1 * 44f, stone);
                        mb.Quad(d1 * 38f, d1 * 38f + Vector3.up * h, d0 * 38f + Vector3.up * h, d0 * 38f, dark);
                        mb.Quad(d0 * 44f + Vector3.up * h, d0 * 38f + Vector3.up * h, d1 * 38f + Vector3.up * h, d1 * 44f + Vector3.up * h, stone);
                        // Arches: a dark opening in each bay, on each storey.
                        Vector3 mid = (d0 + d1).normalized;
                        var face = Quaternion.LookRotation(mid, Vector3.up);
                        for (int s = 0; s * 11f + 9f < h; s++)
                        {
                            mb.Push(mid * 44.1f + Vector3.up * (5f + s * 11f), face, Vector3.one);
                            mb.Box(Vector3.zero, new Vector3(5.2f, 7.5f, 0.6f), dark, null, 0f, true);
                            mb.Pop();
                        }
                    }
                    break;
                }
                case "columns":
                {
                    Color marble = H("#ECE5D6"), shade = H("#CFC6B4");
                    mb.Box(new Vector3(0f, 1.5f, 0f), new Vector3(50f, 3f, 24f), shade);
                    for (int k = 0; k < 8; k++)
                        foreach (float z in new[] { 9.5f, -9.5f })
                            mb.Cylinder(new Vector3(-21f + k * 6f, 3f, z), 1.5f, 1.25f, 15f, 10, marble, false);
                    foreach (float x in new[] { -21f, 21f })
                        for (int k = 1; k < 3; k++) mb.Cylinder(new Vector3(x, 3f, -9.5f + k * 6.33f), 1.5f, 1.25f, 15f, 10, marble, false);
                    mb.Box(new Vector3(0f, 19.5f, 0f), new Vector3(47f, 3f, 22f), marble, null, 0f, true);
                    // The pediment: a low triangle at each end, the roof between.
                    Vector3 fl = new Vector3(-23.5f, 21f, 11f), fr = new Vector3(23.5f, 21f, 11f), ft = new Vector3(0f, 28f, 11f);
                    Vector3 bl = new Vector3(-23.5f, 21f, -11f), br = new Vector3(23.5f, 21f, -11f), bt = new Vector3(0f, 28f, -11f);
                    mb.Tri(fl, fr, ft, marble); mb.Tri(br, bl, bt, marble);
                    mb.Quad(fl, ft, bt, bl, shade); mb.Quad(br, bt, ft, fr, shade);
                    break;
                }
                case "peak":
                {
                    // A great mountain standing alone, snow from two-thirds up.
                    const int n = 26;
                    var jit = new float[n];
                    for (int i = 0; i < n; i++) jit[i] = rng != null ? rng.Range(0.84f, 1.14f) : 1f;
                    Color rock = Color.Lerp(ground * 0.55f, H("#6E7078"), 0.6f), snow = H("#F2F5FA"), snowShade = H("#D6DEEA");
                    rock.a = 1f;
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        float a0 = i * Mathf.PI * 2f / n, a1 = j * Mathf.PI * 2f / n;
                        Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                        Vector3 b0 = d0 * 125f * jit[i], b1 = d1 * 125f * jit[j];
                        Vector3 m0 = d0 * 52f * jit[i] + Vector3.up * 88f * jit[i], m1 = d1 * 52f * jit[j] + Vector3.up * 88f * jit[j];
                        Vector3 top = Vector3.up * 150f;
                        mb.Quad(b0, m0, m1, b1, rock);
                        mb.Tri(m0, top, m1, i % 2 == 0 ? snow : snowShade);
                    }
                    break;
                }
                case "stone_circle":
                {
                    Color stone = H("#8E8D88"), shade = H("#74736E");
                    const int n = 14;
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * Mathf.PI * 2f / n;
                        Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var face = Quaternion.LookRotation(d, Vector3.up);
                        if (k == 4 || k == 9) continue;   // a gap or two: some have fallen
                        mb.Push(d * 16f, face, Vector3.one);
                        mb.Box(new Vector3(0f, 3.3f, 0f), new Vector3(2.4f, 6.6f, 1.5f), k % 2 == 0 ? stone : shade, null, 0f, true);
                        mb.Pop();
                        if (k % 2 == 0 && k != 8 && k != 3)
                        {
                            float am = a + Mathf.PI / n;
                            Vector3 dm = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                            mb.Push(dm * 16f + Vector3.up * 7.2f, Quaternion.LookRotation(dm, Vector3.up), Vector3.one);
                            mb.Box(Vector3.zero, new Vector3(8.6f, 1.3f, 1.7f), shade, null, 0f, true);
                            mb.Pop();
                        }
                    }
                    break;
                }
                case "lighthouse":
                {
                    Color white = H("#F2EEE6"), red = H("#C8463C"), dark = H("#3C3A40"), lamp = H("#FFE9A8");
                    mb.Cylinder(Vector3.zero, 7f, 6f, 3f, 12, H("#8E8D88"));
                    mb.Cylinder(new Vector3(0f, 3f, 0f), 5f, 4.2f, 11f, 12, white, false);
                    mb.Cylinder(new Vector3(0f, 14f, 0f), 4.2f, 3.7f, 8f, 12, red, false);
                    mb.Cylinder(new Vector3(0f, 22f, 0f), 3.7f, 3.2f, 11f, 12, white, false);
                    mb.Cylinder(new Vector3(0f, 33f, 0f), 4.4f, 4.4f, 1.2f, 12, dark);
                    mb.Cylinder(new Vector3(0f, 34.2f, 0f), 2.5f, 2.5f, 4f, 10, lamp, false, false, 1f);
                    mb.Cylinder(new Vector3(0f, 38.2f, 0f), 3.2f, 0f, 3.6f, 10, red);
                    break;
                }
                case "spire":
                {
                    // A city's tallest tower among its neighbours, windows lit.
                    Color glass = H("#7E8EA4"), dark = H("#55627A"), lit = H("#FFE2A6");
                    mb.Box(new Vector3(0f, 60f, 0f), new Vector3(26f, 120f, 26f), glass, dark, 0.1f);
                    mb.Box(new Vector3(0f, 142f, 0f), new Vector3(18f, 44f, 18f), glass, dark, 0.1f);
                    mb.Box(new Vector3(0f, 178f, 0f), new Vector3(10f, 28f, 10f), dark, null, 0.1f);
                    mb.Cylinder(new Vector3(0f, 192f, 0f), 1.2f, 0.2f, 40f, 6, dark);
                    mb.Box(new Vector3(0f, 233f, 0f), new Vector3(2f, 2f, 2f), H("#FF5A4A"), null, 1f, true);
                    var blocks = new[] { (-42f, 10f, 22f, 78f), (38f, -6f, 26f, 96f), (-70f, -14f, 20f, 54f), (66f, 18f, 18f, 62f), (14f, 30f, 16f, 44f) };
                    foreach (var (x, z, w, h) in blocks)
                    {
                        mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(w, h, w), dark, glass, 0.06f);
                        // A few bands of lit windows on the side that faces you.
                        for (float y = 8f; y < h - 6f; y += 9f)
                            mb.Box(new Vector3(x, y, z + w * 0.5f + 0.15f), new Vector3(w * 0.8f, 1.6f, 0.3f), lit, null, 0.7f, true);
                    }
                    for (float y = 10f; y < 112f; y += 9f)
                        mb.Box(new Vector3(0f, y, 13.15f), new Vector3(21f, 1.6f, 0.3f), lit, null, 0.7f, true);
                    break;
                }
            }
        }
    }
}

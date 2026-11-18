using System.Collections.Generic;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck.Tests
{
    /// <summary>The procedural art library: every kit id builds, skins fit their furniture, budgets hold.</summary>
    public class ArtLibraryTests
    {
        static readonly Vector3[] Sizes =
        {
            new Vector3(0.4f, 0.45f, 0.4f), new Vector3(1.2f, 0.75f, 0.6f), new Vector3(2.2f, 0.9f, 0.9f),
            new Vector3(0.6f, 1.9f, 0.4f), new Vector3(1.0f, 2.1f, 0.1f)
        };
        /// <summary>The test sizes plus the extremes of the 0.3–3 m range, for the triangle budget.</summary>
        static readonly Vector3[] BudgetSizes =
        {
            new Vector3(0.4f, 0.45f, 0.4f), new Vector3(1.2f, 0.75f, 0.6f), new Vector3(2.2f, 0.9f, 0.9f),
            new Vector3(0.6f, 1.9f, 0.4f), new Vector3(1.0f, 2.1f, 0.1f), new Vector3(3f, 3f, 3f),
            new Vector3(3f, 3f, 0.3f), new Vector3(0.3f, 3f, 3f), new Vector3(3f, 0.3f, 0.3f), new Vector3(0.3f, 0.3f, 0.3f)
        };
        static readonly int[] Seeds = { 1, 7, 42 };
        static readonly string[] ExtraKinds = { "lantern", "crystal_cluster", "flower", "cloud" };
        static readonly string[] AddedKinds = { "stone", "log_small" };
        static readonly float[] Footprints = { 0.25f, 0.4f, 0.5f };
        const float Tol = 0.01f;

        // ---------- the kit's ids ----------

        static List<string> SkinIds()
        {
            var ids = new List<string>();
            foreach (var b in Kit.Biomes)
                foreach (var kv in b.Skins)
                    if (kv.Value != "none" && !ids.Contains(kv.Value)) ids.Add(kv.Value);
            return ids;
        }

        static bool IsFrame(string skin)
        {
            foreach (var b in Kit.Biomes)
                foreach (var kv in b.Skins)
                    if (kv.Value == skin && (kv.Key == Role.Door || kv.Key == Role.Window)) return true;
            return false;
        }

        /// <summary>The colours of the first world that uses this skin.</summary>
        static WorldColors ColorsFor(string skin)
        {
            foreach (var b in Kit.Biomes)
                foreach (var kv in b.Skins)
                    if (kv.Value == skin) return WorldColors.From(b.Palette);
            return WorldColors.From(Kit.Biomes[0].Palette);
        }

        static string ItemKind(BiomeDef b) => b.Item.Replace(' ', '_');

        static void AssertFinite(MeshBuilder mb, string what)
        {
            foreach (var p in mb.Positions)
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z),
                               what + " has a NaN or infinite vertex");
        }

        // ---------- coverage ----------

        [Test]
        public void EveryKitIdBuilds()
        {
            foreach (var b in Kit.Biomes)
            {
                var c = WorldColors.From(b.Palette);
                foreach (var kv in b.Skins)
                {
                    var mb = new MeshBuilder();
                    Assert.IsTrue(Skins.Build(mb, kv.Value, new Vector3(1f, 0.8f, 0.6f), c, new Rng(3)), b.Id + " skin " + kv.Value);
                    if (kv.Value != "none") Assert.Greater(mb.TriangleCount, 0, b.Id + " skin " + kv.Value + " is empty");
                    AssertFinite(mb, kv.Value);
                }
                foreach (var kind in b.Scatter)
                {
                    var mb = new MeshBuilder();
                    Assert.IsTrue(Props.Build(mb, kind, c, new Rng(3)), b.Id + " scatter " + kind);
                    Assert.Greater(mb.TriangleCount, 0, kind);
                    Assert.Greater(Props.Radius(kind), 0f, kind + " radius");
                    AssertFinite(mb, kind);
                }
                Assert.IsTrue(Props.Build(new MeshBuilder(), ItemKind(b), c, new Rng(3)), b.Id + " item " + b.Item);
                Assert.IsTrue(Props.Build(new MeshBuilder(), b.Item, c, new Rng(3)), b.Id + " item as written: " + b.Item);
                Assert.IsTrue(Builds.Result(new MeshBuilder(), b.Build, c, new Rng(3), 0.4f), b.Id + " build " + b.Build);
                Assert.IsTrue(Builds.Souvenir(new MeshBuilder(), b.Souvenir, c, new Rng(3)), b.Id + " souvenir " + b.Souvenir);
            }
            var c0 = WorldColors.From(Kit.Biomes[0].Palette);
            foreach (var kind in ExtraKinds) Assert.IsTrue(Props.Build(new MeshBuilder(), kind, c0, new Rng(3)), "extra " + kind);
            foreach (var kind in AddedKinds) Assert.IsTrue(Props.Build(new MeshBuilder(), kind, c0, new Rng(3)), "prop " + kind);
        }

        [Test]
        public void UnknownIdsBuildNothing()
        {
            var c = WorldColors.From(Kit.Biomes[0].Palette);
            var mb = new MeshBuilder();
            Assert.IsFalse(Props.Build(mb, "unicorn", c, new Rng(1)));
            Assert.IsFalse(Skins.Build(mb, "unicorn", Vector3.one, c, new Rng(1)));
            Assert.IsFalse(Builds.Result(mb, "unicorn", c, new Rng(1), 0.4f));
            Assert.IsFalse(Builds.Souvenir(mb, "unicorn", c, new Rng(1)));
            Assert.IsFalse(Props.Build(mb, "firefly_glow", c, new Rng(1)), "firefly_glow is particles, not a mesh");
            Assert.AreEqual(0f, Props.Radius("unicorn"));
            Assert.IsTrue(Skins.Build(mb, "none", Vector3.one, c, new Rng(1)));
            Assert.AreEqual(0, mb.TriangleCount);
            Assert.IsTrue(float.IsNaN(mb.AoBaseY), "the fake-AO base is restored");
        }

        // ---------- skins fit their furniture ----------

        [Test]
        public void SkinsStayInsideTheirBox()
        {
            var fails = new List<string>();
            foreach (var id in SkinIds())
            {
                bool rise = Skins.MayRiseAbove(id), frame = IsFrame(id);
                var c = ColorsFor(id);
                foreach (var size in Sizes)
                    foreach (int seed in Seeds)
                    {
                        var mb = new MeshBuilder();
                        Assert.IsTrue(Skins.Build(mb, id, size, c, new Rng(seed)), id);
                        AssertFinite(mb, id);
                        float hx = size.x * 0.5f, hz = size.z * 0.5f;
                        foreach (var p in mb.Positions)
                        {
                            bool outside = Mathf.Abs(p.x) > hx + Tol || Mathf.Abs(p.z) > hz + Tol || p.y < -Tol || (!rise && p.y > size.y + Tol);
                            bool opening = frame && Mathf.Abs(p.x) < 0.35f * size.x - Tol && p.y < 0.85f * size.y - Tol;
                            if (!outside && !opening) continue;
                            fails.Add($"{id} at {size.x}x{size.y}x{size.z} seed {seed}: {(opening ? "in the opening" : "outside the box")} at ({p.x:F3}, {p.y:F3}, {p.z:F3})");
                            break;
                        }
                    }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [Test]
        public void SurfaceSkinsHaveAFlatTop()
        {
            int surfaces = 0;
            var fails = new List<string>();
            foreach (var id in SkinIds())
            {
                if (!Skins.FlatTop(id)) continue;
                surfaces++;
                var c = ColorsFor(id);
                foreach (var size in Sizes)
                    foreach (int seed in Seeds)
                    {
                        var mb = new MeshBuilder();
                        Skins.Build(mb, id, size, c, new Rng(seed));
                        int hit = TopCoverage(mb, size);
                        if (hit < 70) fails.Add($"{id} at {size.x}x{size.y}x{size.z} seed {seed}: flat top covers {hit} of 100 points");
                    }
            }
            Assert.AreEqual(8, surfaces, "every world's table is a flat-top skin");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>How many of a 10×10 grid of footprint points lie under an up-facing triangle at the box top (±1 cm).</summary>
        static int TopCoverage(MeshBuilder mb, Vector3 size)
        {
            var tops = new List<Vector3>();
            var pos = mb.Positions;
            for (int i = 0; i + 2 < pos.Count; i += 3)
            {
                Vector3 a = pos[i], b = pos[i + 1], d = pos[i + 2];
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (n.sqrMagnitude < 1e-14f) continue;
                if (n.normalized.y < 0.99f) continue;
                if (Mathf.Abs(a.y - size.y) > Tol || Mathf.Abs(b.y - size.y) > Tol || Mathf.Abs(d.y - size.y) > Tol) continue;
                tops.Add(a); tops.Add(b); tops.Add(d);
            }
            int hit = 0;
            for (int gx = 0; gx < 10; gx++)
                for (int gz = 0; gz < 10; gz++)
                {
                    float x = ((gx + 0.5f) / 10f - 0.5f) * size.x, z = ((gz + 0.5f) / 10f - 0.5f) * size.z;
                    for (int t = 0; t < tops.Count; t += 3)
                        if (Inside(x, z, tops[t], tops[t + 1], tops[t + 2])) { hit++; break; }
                }
            return hit;
        }

        static bool Inside(float x, float z, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = Edge(x, z, a, b), d2 = Edge(x, z, b, c), d3 = Edge(x, z, c, a);
            bool neg = d1 < -1e-7f || d2 < -1e-7f || d3 < -1e-7f, pos = d1 > 1e-7f || d2 > 1e-7f || d3 > 1e-7f;
            return !(neg && pos);
        }

        static float Edge(float x, float z, Vector3 p, Vector3 q) => (x - q.x) * (p.z - q.z) - (p.x - q.x) * (z - q.z);

        // ---------- results and souvenirs fit the table ----------

        [Test]
        public void ResultsFitTheTableTop()
        {
            var fails = new List<string>();
            foreach (var b in Kit.Biomes)
            {
                var c = WorldColors.From(b.Palette);
                foreach (float f in Footprints)
                    foreach (int seed in Seeds)
                    {
                        var mb = new MeshBuilder();
                        Assert.IsTrue(Builds.Result(mb, b.Build, c, new Rng(seed), f), b.Build);
                        AssertFinite(mb, b.Build);
                        foreach (var p in mb.Positions)
                        {
                            if (new Vector2(p.x, p.z).magnitude <= f * 0.5f + Tol && p.y >= -Tol && p.y <= 1.2f * f + Tol) continue;
                            fails.Add($"{b.Build} at footprint {f} seed {seed}: vertex at ({p.x:F3}, {p.y:F3}, {p.z:F3})");
                            break;
                        }
                    }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [Test]
        public void SouvenirsAreSmall()
        {
            foreach (var b in Kit.Biomes)
            {
                var mb = new MeshBuilder();
                Assert.IsTrue(Builds.Souvenir(mb, b.Souvenir, WorldColors.From(b.Palette), new Rng(5)), b.Souvenir);
                AssertFinite(mb, b.Souvenir);
                var bounds = mb.Bounds;
                float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                Assert.That(size, Is.InRange(0.06f, 0.13f), b.Souvenir + " should be an 8–12 cm miniature");
                Assert.GreaterOrEqual(bounds.min.y, -Tol, b.Souvenir + " sits on the table");
            }
        }

        [Test]
        public void ItemsAreHandSized()
        {
            foreach (var b in Kit.Biomes)
            {
                var mb = new MeshBuilder();
                Props.Build(mb, ItemKind(b), WorldColors.From(b.Palette), new Rng(5));
                AssertFinite(mb, b.Item);
                var size = mb.Bounds.size;
                float big = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                Assert.That(big, Is.InRange(0.05f, 0.14f), b.Item + " should be 6–12 cm");
            }
        }

        // ---------- budgets ----------

        [Test]
        public void TriangleBudgetsHold()
        {
            var fails = new List<string>();
            foreach (var b in Kit.Biomes)
            {
                var c = WorldColors.From(b.Palette);
                foreach (int seed in Seeds)
                {
                    foreach (var kind in b.Scatter) Check(fails, kind, Tris(m => Props.Build(m, kind, c, new Rng(seed))), 400);
                    foreach (var kind in AddedKinds) Check(fails, kind, Tris(m => Props.Build(m, kind, c, new Rng(seed))), 400);
                    foreach (var kind in ExtraKinds) Check(fails, kind, Tris(m => Props.Build(m, kind, c, new Rng(seed))), 300);
                    Check(fails, b.Item, Tris(m => Props.Build(m, ItemKind(b), c, new Rng(seed))), 200);
                    foreach (float f in Footprints) Check(fails, b.Build, Tris(m => Builds.Result(m, b.Build, c, new Rng(seed), f)), 1500);
                    Check(fails, b.Souvenir, Tris(m => Builds.Souvenir(m, b.Souvenir, c, new Rng(seed))), 600);
                    foreach (var kv in b.Skins)
                        foreach (var size in BudgetSizes)
                            Check(fails, kv.Value + " " + size, Tris(m => Skins.Build(m, kv.Value, size, c, new Rng(seed))), 1500);
                }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        static int Tris(System.Func<MeshBuilder, bool> build)
        {
            var mb = new MeshBuilder();
            build(mb);
            return mb.TriangleCount;
        }

        static void Check(List<string> fails, string what, int tris, int budget)
        {
            if (tris > budget) fails.Add($"{what}: {tris} triangles (budget {budget})");
        }
    }
}

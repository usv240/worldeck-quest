using NUnit.Framework;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>The artist-made props (CC0 Kenney models): they load, stand the right way up, face outward, and fit the budget.</summary>
    public class ModelKitTests
    {
        static readonly string[] KitKinds = { "pine", "snow_pine", "rock", "snow_rock", "fern", "palm", "cactus", "dune_grass", "oak", "bush",
            "flower_patch", "flower", "stone", "log_small", "building", "street_lamp", "antenna", "mushroom", "stump", "tent", "log_stack",
            "lantern_hanging", "rover", "crate_space", "console", "bar_stool", "lounge_chair", "speaker", "potted_plant" };

        [Test]
        public void EveryKindHasModels()
        {
            foreach (var k in KitKinds) Assert.IsTrue(ModelKit.Has(k), k);
            Assert.GreaterOrEqual(ModelKit.ModelCount, 90);
        }

        [Test]
        public void PropsStandOnTheGroundAtTheirHeight()
        {
            var c = WorldColors.From(Kit.Biomes[0].Palette);
            foreach (var k in KitKinds)
                foreach (var m in ModelKit.Models(k))
                {
                    var mb = new MeshBuilder();
                    Assert.IsTrue(ModelKit.Build(mb, k, c, new Rng(1), 2f), k);
                    var b = mb.Bounds;
                    Assert.That(b.min.y, Is.InRange(-0.01f, 0.01f), k + " stands on the ground");
                    Assert.That(b.max.y, Is.InRange(1.95f, 2.05f), k + " is as tall as asked");
                }
        }

        [Test]
        public void FacesPointOutward()
        {
            // Mirroring or winding the wrong way turns a model inside out: most faces would point at its middle.
            var c = WorldColors.From(Kit.Biomes[0].Palette);
            foreach (var k in new[] { "pine", "rock", "cactus", "building", "mushroom" })
            {
                var mb = new MeshBuilder();
                ModelKit.Build(mb, k, c, new Rng(2), 1f);
                Vector3 centre = mb.Bounds.center;
                var p = mb.Positions;
                int outward = 0, all = 0;
                for (int i = 0; i + 2 < p.Count; i += 3)
                {
                    Vector3 n = Vector3.Cross(p[i + 1] - p[i], p[i + 2] - p[i]);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    Vector3 mid = (p[i] + p[i + 1] + p[i + 2]) / 3f;
                    all++;
                    if (Vector3.Dot(n, mid - centre) > 0f) outward++;
                }
                Assert.Greater(outward, all * 0.6f, k + ": " + outward + " of " + all + " faces point outward");
            }
        }

        [Test]
        public void SnowSettlesOnSnowyTrees()
        {
            var c = WorldColors.From(Kit.Biomes[0].Palette);
            var plain = new MeshBuilder();
            var snowy = new MeshBuilder();
            ModelKit.Build(plain, "pine", c, new Rng(4), 1f);
            ModelKit.Build(snowy, "snow_pine", c, new Rng(4), 1f);
            Assert.Greater(Whiteness(snowy), Whiteness(plain) + 0.1f);
        }

        static float Whiteness(MeshBuilder mb)
        {
            var mesh = mb.ToMesh("w");
            float sum = 0f;
            int n = mesh.vertexCount;
            foreach (var col in mesh.colors32) sum += Mathf.Min(col.r, Mathf.Min(col.g, col.b)) / 255f;
            Object.DestroyImmediate(mesh);
            return sum / Mathf.Max(1, n);
        }

        [Test]
        public void OffMeansMadeInCodeAgain()
        {
            var c = WorldColors.From(Kit.Biomes[0].Palette);
            try
            {
                ModelKit.Enabled = false;
                Assert.IsFalse(ModelKit.Has("pine"));
                Assert.IsTrue(Props.Build(new MeshBuilder(), "pine", c, new Rng(1)), "the code-made pine is still there");
            }
            finally { ModelKit.Enabled = true; }
        }
    }
}

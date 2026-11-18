using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck.Tests
{
    /// <summary>The "show, don't tell" layer: the see-through hand's poses, the loose things, the menu's words.</summary>
    public class GuidanceTests
    {
        static Material GhostMat()
        {
            var sh = Shader.Find("Worldeck/Ghost");
            Assert.NotNull(sh, "Ghost shader compiles and is found");
            return new Material(sh);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheGhostHandsPinchMeets(bool left)
        {
            var g = GhostHand.Make("test hand", null, GhostMat(), left);
            try
            {
                g.Pinch = 0f; g.Point = 0f; g.Curl = 0.1f;
                g.Apply();
                float open = Vector3.Distance(g.ThumbTip, g.IndexTip);
                g.Pinch = 1f;
                g.Apply();
                float closed = Vector3.Distance(g.ThumbTip, g.IndexTip);
                Assert.Greater(open, 0.04f, "open hand: thumb and index apart");
                Assert.Less(closed, 0.02f, "pinched: thumb meets the index tip");
                // The thumb is on the inner side: -X for a right hand, +X for a left.
                g.Pinch = 0f; g.Apply();
                Vector3 thumb = g.transform.InverseTransformPoint(g.ThumbTip);
                Assert.AreEqual(left ? 1f : -1f, Mathf.Sign(thumb.x), "thumb side");
            }
            finally { Object.DestroyImmediate(g.gameObject); }
        }

        [Test]
        public void PointingStraightensTheIndexAndFoldsTheRest()
        {
            var g = GhostHand.Make("test hand", null, GhostMat(), false);
            try
            {
                g.Point = 1f; g.Apply();
                float index = g.transform.InverseTransformPoint(g.IndexTip).z;
                Assert.Greater(index, 0.15f, "index reaches forward");
                g.PlaceBy(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 40f, 0f), GhostHand.Anchor.IndexTip);
                Assert.Less(Vector3.Distance(g.IndexTip, new Vector3(1f, 2f, 3f)), 0.001f, "placed by its fingertip");
            }
            finally { Object.DestroyImmediate(g.gameObject); }
        }

        [Test]
        public void CapsulesAreClosedAndFaceOutwards()
        {
            var m = GhostHand.Capsule(0.04f, 0.009f, 0.008f, 10, 3);
            Assert.Greater(m.vertexCount, 0);
            // Every triangle's winding agrees with its vertex normals (outward for Unity's front faces).
            var v = m.vertices; var n = m.normals; var t = m.triangles;
            int bad = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (face.sqrMagnitude < 1e-14f) continue;
                if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) bad++;
            }
            Assert.AreEqual(0, bad, "inward-facing triangles");
        }

        [Test]
        public void EveryWorldHasLooseThingsThatBuild()
        {
            foreach (var def in Kit.Biomes)
            {
                var r = Recipe.For(def.Id, 3);
                var colors = WorldColors.For(r);
                var kinds = Loose.KindsFor(def.Id);
                Assert.Greater(kinds.Length, 0, def.Id);
                foreach (var (kind, size) in kinds)
                {
                    var mb = new MeshBuilder();
                    Props.Build(mb, kind, colors, new Rng(5));
                    Assert.Greater(mb.TriangleCount, 0, def.Id + ": " + kind + " builds");
                    Assert.That(size, Is.InRange(0.03f, 0.12f), "small enough to pick up");
                    Assert.AreNotEqual(def.Item.Replace(' ', '_'), kind, def.Id + ": a loose thing must not look like the task's pieces");
                }
            }
        }

        [TestCase("COUCH", "sofa")]
        [TestCase("STORAGE", "shelf")]
        [TestCase("OTHER", "furniture")]
        [TestCase("", "furniture")]
        public void PlainNamesForScannedFurniture(string label, string plain) => Assert.AreEqual(plain, Emergency.Plain(label));

        [Test]
        public void EveryMenuButtonSaysWhatItDoes()
        {
            foreach (var cmd in new[] { "talk", "map", "undo", "freeze", "resume", "save", "help", "end", "today", "surprise", "describe", "saved", "codes", "scan", "close" })
                Assert.IsNotEmpty(HandPanel.HintFor(cmd), cmd);
        }

        [Test]
        public void TheHowItWorksCardCoversEveryWayToPlay()
        {
            string all = "";
            foreach (var row in HelpCard.Rows)
            {
                Assert.LessOrEqual(row.text.Length, 90, "a row fits two short lines: " + row.text);
                Assert.That(row.demo, Is.EqualTo("").Or.EqualTo("press").Or.EqualTo("menu").Or.EqualTo("move").Or.EqualTo("jump").Or.EqualTo("block").Or.EqualTo("tap"));
                all += row.text.ToLowerInvariant() + " ";
            }
            foreach (var word in new[] { "press", "menu", "pinch", "tap", "juniper", "palm", "stand up", "wrist" })
                StringAssert.Contains(word, all);
        }
    }
}

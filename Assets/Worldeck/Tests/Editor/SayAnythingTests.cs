using System.Collections.Generic;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck.Tests
{
    /// <summary>"Say anything": what counts as asking for a thing, what the relay may answer, and what gets built.</summary>
    public class SayAnythingTests
    {
        [TestCase("make me a lantern", "lantern", 1, "front")]
        [TestCase("Give me three shells on the table", "shell", 3, "table")]
        [TestCase("put a snowman on the floor please", "snowman", 1, "floor")]
        [TestCase("I want a christmas tree over there", "snow_pine", 1, "there")]
        [TestCase("can you make two lanterns on my sofa", "lantern", 2, "sofa")]
        [TestCase("create a sand castle", "sandcastle", 1, "front")]
        // The artist-made models, by name.
        [TestCase("put a tent over there", "tent", 1, "there")]
        [TestCase("make me a mars rover on the table", "rover", 1, "table")]
        [TestCase("add three mushrooms on the floor", "mushroom", 3, "floor")]
        [TestCase("can you make a bar stool", "bar_stool", 1, "front")]
        [TestCase("give me a potted plant on the table", "potted_plant", 1, "table")]
        [TestCase("I want a pile of logs", "log_stack", 1, "front")]
        public void LibraryThingsAreNamedWithoutAnyNetwork(string said, string kind, int count, string place)
        {
            var w = Wishes.Read(said);
            Assert.NotNull(w, said);
            Assert.AreEqual(kind, w.Kind, said);
            Assert.AreEqual(count, w.Count, said);
            Assert.AreEqual(place, w.Place, said);
        }

        [TestCase("make me a rubber duck", "rubber duck")]
        [TestCase("I'd like a grand piano to my left", "grand piano")]
        [TestCase("build a tiny red robot", "tiny red robot")]
        public void OtherThingsAreWishesForTheHelper(string said, string what)
        {
            var w = Wishes.Read(said);
            Assert.NotNull(w, said);
            Assert.AreEqual("", w.Kind);
            Assert.AreEqual(what, w.Object);
        }

        [TestCase("make it rain")]
        [TestCase("a beach at sunset")]
        [TestCase("make a beach")]
        [TestCase("add some fireflies")]
        [TestCase("I want a forest at night")]
        [TestCase("darker")]
        [TestCase("take me home")]
        [TestCase("")]
        public void WorldsAndWeatherAreNotWishes(string said) => Assert.IsNull(Wishes.Read(said), said);

        [Test]
        public void ChangesToMadeThingsAreUnderstood()
        {
            var names = new List<string> { "lantern", "rubber duck" };
            Assert.AreEqual(1.5f, Wishes.Change("bigger", names).Scale, 0.001f);
            Assert.AreEqual(2.2f, Wishes.Change("make it much bigger", names).Scale, 0.001f);
            Assert.Less(Wishes.Change("smaller please", names).Scale, 1f);
            Assert.AreEqual("#2f6fd6", Wishes.Change("make it blue", names).Color);
            Assert.AreEqual("rubber duck", Wishes.Change("paint the rubber duck red", names).Target);
            var gone = Wishes.Change("remove the rubber duck", names);
            Assert.IsTrue(gone.Remove);
            Assert.AreEqual("rubber duck", gone.Target);
            Assert.AreEqual("last", Wishes.Change("delete it", names).Target);
            Assert.AreEqual("all", Wishes.Change("remove everything", names).Target);
            // The world's own words stay the world's.
            Assert.IsNull(Wishes.Change("remove the fog", names));
            Assert.IsNull(Wishes.Change("make it rain", names));
            Assert.IsNull(Wishes.Change("darker", names));
            Assert.IsNull(Wishes.Change("bigger", new List<string>()), "nothing made: nothing to change");
        }

        [Test]
        public void TheRelaysAnswerIsReadWithNothingTrusted()
        {
            string json = "{\"safety\":\"ok\",\"say\":\"Here you go.\",\"source\":\"model\",\"actions\":[" +
                "{\"do\":\"make\",\"name\":\"Rubber Duck!!\",\"kind\":null,\"parts\":[\"sphere 0 0.3 0 0.7 0.55 0.5 0 0 0 #f5c518\",\"pyramid 0 0 0 1 1 1 0 0 0 #ffffff\",\"box 9 9 9 99 99 99 0 0 0 #00ff00\"],\"place\":\"moon\",\"count\":50,\"size\":40}," +
                "{\"do\":\"make\",\"name\":\"x\",\"kind\":\"nuke\",\"parts\":[],\"place\":\"front\",\"count\":1,\"size\":0.2}," +
                "{\"do\":\"change\",\"target\":\"last\",\"scale\":100,\"color\":\"red\",\"remove\":false}," +
                "{\"do\":\"event\",\"event\":\"format_disk\"}," +
                "{\"do\":\"event\",\"event\":\"storm\"}," +
                "{\"do\":\"run_code\"}," +
                "{\"do\":\"event\",\"event\":\"music\"},{\"do\":\"event\",\"event\":\"music\"},{\"do\":\"event\",\"event\":\"music\"}]}";
            var a = ActAnswer.Read(json, Wishes.Knows);
            Assert.IsNull(a.Error);
            Assert.AreEqual(ActAnswer.MaxActions, a.Actions.Count, "at most four actions");
            var duck = a.Actions[0];
            Assert.AreEqual("rubber duck", duck.Name);
            Assert.AreEqual(2, duck.Parts.Count, "the unknown shape is dropped");
            Assert.LessOrEqual(duck.Parts[1].Sx, 1.5f);
            Assert.AreEqual("front", duck.Place);
            Assert.AreEqual(ActAnswer.MaxCount, duck.Count);
            Assert.AreEqual(ActAnswer.MaxSize, duck.Size, 0.001f);
            Assert.AreEqual("change", a.Actions[1].Do, "a thing with an unknown kind and no parts is dropped");
            Assert.AreEqual(4f, a.Actions[1].Scale, 0.001f);
            Assert.AreEqual("", a.Actions[1].Color);
            Assert.AreEqual("storm", a.Actions[2].Event);

            Assert.AreEqual(0, ActAnswer.Read("{\"safety\":\"decline\",\"say\":\"no\",\"actions\":[{\"do\":\"event\",\"event\":\"storm\"}]}", Wishes.Knows).Actions.Count);
            Assert.NotNull(ActAnswer.Read("<html>502</html>", Wishes.Knows).Error);
            Assert.AreEqual("", ActAnswer.Read("{\"say\":\"visit http://x.example now\",\"actions\":[]}", Wishes.Knows).Say);
        }

        [Test]
        public void EveryLibraryThingBuilds()
        {
            var colors = WorldColors.For(Recipe.For("campfire", 1));
            foreach (string kind in Wishes.Library)
            {
                var mesh = Maker.BuildMesh(new ActItem { Do = "make", Kind = kind, Name = kind }, colors, 3, out float largest);
                Assert.Greater(mesh.vertexCount, 0, kind);
                Assert.Greater(largest, 0.01f, kind);
                Assert.AreEqual(0f, mesh.bounds.min.y, 0.001f, kind + " stands on its base");
                Assert.Greater(Wishes.SizeOf(kind), 0.04f, kind);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AThingFromPartsIsBuiltCentredOnItsBase()
        {
            var it = new ActItem { Do = "make", Name = "duck" };
            foreach (string line in new[] { "sphere 0.2 0.3 0 0.7 0.55 0.5 0 0 0 #f5c518", "cone 0.6 0.68 0 0.12 0.2 0.12 0 0 -90 #ef8a2b", "box 0.2 0.05 0 0.3 0.1 0.3 0 45 0 #ffffff", "cylinder 0.2 0.5 0 0.1 0.4 0.1 0 0 0 #333333" })
            {
                Assert.IsTrue(ActPart.TryParse(line, out var p), line);
                it.Parts.Add(p);
            }
            var mesh = Maker.BuildMesh(it, WorldColors.For(Recipe.For("campfire", 1)), 1, out float largest);
            Assert.Greater(mesh.vertexCount, 60);
            Assert.AreEqual(0f, mesh.bounds.min.y, 0.001f);
            Assert.AreEqual(0f, mesh.bounds.center.x, 0.001f);
            Assert.AreEqual(0f, mesh.bounds.center.z, 0.001f);
            Assert.Greater(largest, 0.5f);
            Object.DestroyImmediate(mesh);
        }
    }
}

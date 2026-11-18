using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Any place: composed worlds from the kit, offline places, and whatever Claude composes, all kept safe.</summary>
    public class ComposerTests
    {
        [Test]
        public void EveryPlaceIsWhollyMadeOfTheKit()
        {
            foreach (var p in Places.All)
            {
                Assert.NotNull(Kit.Get(p.Base), p.Id + " base");
                var before = p.Look.Copy();
                var r = p.Recipe(3);
                Assert.NotNull(r.Look, p.Id);
                CollectionAssert.AreEqual(before.Scatter, r.Look.Scatter, p.Id + ": every prop it scatters exists");
                foreach (var kv in before.Skins) Assert.AreEqual(kv.Value, r.Look.Skins[kv.Key], p.Id + ": skin " + kv.Value + " for " + kv.Key);
                foreach (var hex in new[] { before.Palette.Top, before.Palette.Horizon, before.Palette.Fog, before.Palette.Ground, before.Palette.GroundAccent, before.Palette.Prop, before.Palette.PropAccent, before.Palette.Emissive, before.Palette.Ambient })
                    Assert.IsTrue(Look.IsHex(hex), p.Id + " colour " + hex);
                Assert.AreEqual(before.Horizon, r.Look.Horizon, p.Id + " horizon");
                Assert.AreEqual(p.Look.Name, r.Def.Name, p.Id + ": the app reads the composed world");
                Assert.AreEqual(r.Def.Scatter, r.Look.Scatter);
            }
        }

        [Test]
        public void EveryKitPropIsReallyInTheModelKit()
        {
            foreach (var k in Look.KitKinds) Assert.IsTrue(ModelKit.Has(k), k);
        }

        [TestCase("take me to a volcano", "volcano")]
        [TestCase("Mars", "mars")]
        [TestCase("I want to stand on the moon", "moon")]
        [TestCase("a haunted forest at midnight", "haunted")]
        [TestCase("cherry blossoms please", "sakura")]
        [TestCase("a jungle", "jungle")]
        [TestCase("pirate cove", "pirate_cove")]
        [TestCase("tokyo in the rain", "neon_city")]
        [TestCase("a castle on a hill", "castle")]
        [TestCase("northern lights", "aurora")]
        [TestCase("a dragon's lair", "dragon_lair")]
        [TestCase("a prehistoric jungle", "prehistoric")]
        [TestCase("an enchanted forest", "enchanted")]
        [TestCase("floating islands above the clouds", "sky_islands")]
        public void PlacesAreFoundByTheirWords(string said, string id) => Assert.AreEqual(id, Places.Find(said)?.Id, said);

        [TestCase("that's mine")]
        [TestCase("make me a candy cane")]
        [TestCase("a forest at night")]
        [TestCase("make it rain")]
        [TestCase("make me a dragon")]
        [TestCase("")]
        public void OtherTalkIsNoPlace(string said) => Assert.IsNull(Places.Find(said), said);

        [Test]
        public void SayingAPlaceOpensItWithEditsOnTop()
        {
            var h = Words.Parse("a volcano at midnight", Recipe.For("campfire", 1), 7);
            Assert.AreEqual(Act.NewWorld, h.Act);
            Assert.NotNull(h.Result.Look);
            Assert.AreEqual("Volcano at nightfall", h.Result.Look.Name);
            Assert.AreEqual(0f, h.Result.Hour, 0.01f, "midnight, as asked");
            Assert.AreEqual("Volcano at nightfall.", h.Reply, "the caption reads right");
            Assert.IsFalse(h.AskAi, "known offline: no network needed");
        }

        [TestCase("Mars", "On Mars.")]
        [TestCase("the moon", "On the Moon.")]
        [TestCase("a haunted forest", "Haunted forest.")]
        public void PlaceCaptionsReadRight(string said, string reply) => Assert.AreEqual(reply, Words.Parse(said, Recipe.For("campfire", 1), 2).Reply);

        [TestCase("add lava")]
        [TestCase("make it more volcanic")]
        [TestCase("add some pumpkins")]
        [TestCase("with a bit of jungle")]
        [TestCase("add northern lights")]
        public void SomethingToAddIsNotATrip(string said)
        {
            var h = Words.Parse(said, Recipe.For("campfire", 1), 2);
            Assert.IsFalse(h.Act == Act.NewWorld && h.Result?.Look != null, said + " should not open a composed place");
        }

        [TestCase("take me to the moon", "moon")]
        [TestCase("a volcano", "volcano")]
        [TestCase("let's go to a pirate cove", "pirate_cove")]
        [TestCase("make it Mars", "mars")]
        [TestCase("put me on the moon", "moon")]
        [TestCase("turn it into a volcano", "volcano")]
        [TestCase("no, the moon", "moon")]
        public void ADestinationIsATrip(string said, string id)
        {
            var h = Words.Parse(said, Recipe.For("campfire", 1), 2);
            Assert.AreEqual(Act.NewWorld, h.Act, said);
            Assert.AreEqual(Places.Get(id).Look.Name, h.Result.Look?.Name, said);
        }

        [Test]
        public void NoTrademarkedOrClumsyTitles()
        {
            foreach (var p in Places.All)
            {
                Assert.IsFalse(p.Look.Name.ToLowerInvariant().Contains("candy land"), p.Id);
                Assert.IsFalse(p.Look.Name.Contains(" of ice"), p.Id);
            }
        }

        [Test]
        public void EditsKeepThePlace()
        {
            var mars = Places.Get("mars").Recipe(2);
            var h = Words.Parse("make it darker", mars, 3);
            Assert.AreEqual(Act.Edit, h.Act);
            Assert.NotNull(h.Result.Look, "still Mars");
            Assert.AreEqual("On Mars", h.Result.Look.Name);
        }

        [Test]
        public void AKitWorldAfterAPlaceIsTheKitWorld()
        {
            var mars = Places.Get("mars").Recipe(2);
            var h = Words.Parse("take me to the desert", mars, 3);
            Assert.AreEqual(Act.NewWorld, h.Act);
            Assert.IsNull(h.Result.Look, "the plain desert, not Mars");
        }

        [Test]
        public void APlaceSurvivesSavingAndLoading()
        {
            var r = Places.Get("haunted").Recipe(5);
            var back = Recipe.FromJson(Json.Parse(Json.Write(r.ToJson())) as Dictionary<string, object>);
            Assert.NotNull(back.Look);
            Assert.AreEqual(r.Look.Name, back.Look.Name);
            CollectionAssert.AreEqual(r.Look.Scatter, back.Look.Scatter);
            Assert.AreEqual(r.Look.Palette.Ground, back.Look.Palette.Ground);
            Assert.IsTrue(r.SameWorld(back));
        }

        [Test]
        public void WhatClaudeComposesIsCheckedAgainstTheKit()
        {
            var json = "{\"biome\":\"desert\",\"title\":\"Sunken temple\",\"look\":{\"name\":\"Sunken temple\",\"horizon\":\"volcano\",\"terrain\":500," +
                       "\"scatter\":[\"rock\",\"dragon\",\"crystal_rock\",\"<script>\"],\"sky\":\"nowhere\",\"ambience\":\"amb_nothing\",\"tint\":9," +
                       "\"skins\":{\"surface\":\"stone_altar\",\"seat\":\"throne_of_lies\",\"window\":\"none\"}," +
                       "\"palette\":{\"top\":\"#112233\",\"ground\":\"red\",\"emissive\":\"#FFAA00\"}}}";
            var r = Recipe.FromJson(Json.Parse(json) as Dictionary<string, object>);
            Assert.NotNull(r.Look);
            CollectionAssert.AreEqual(new[] { "rock", "crystal_rock" }, r.Look.Scatter, "only props the app has");
            Assert.AreEqual(Kit.Get("desert").Horizon, r.Look.Horizon, "an unknown horizon falls back");
            Assert.LessOrEqual(r.Look.TerrainHeight, 30f);
            Assert.AreEqual("desert", r.Look.SkyFrom, "an unknown sky falls back to the base world's");
            Assert.AreEqual(Kit.Get("desert").Ambience, r.Look.Ambience);
            Assert.AreEqual(1f, r.Look.Tint);
            Assert.AreEqual("stone_altar", r.Look.Skins[Role.Surface]);
            Assert.IsFalse(r.Look.Skins.ContainsKey(Role.Seat), "an unknown skin is dropped");
            Assert.AreEqual("#112233", r.Look.Palette.Top);
            Assert.AreEqual(Kit.Get("desert").Palette.Ground, r.Look.Palette.Ground, "a colour that isn't one falls back");
        }

        [Test]
        public void ThingsAreMadeNotPlacesOpened()
        {
            Assert.AreEqual("pumpkin", Wishes.Read("make me three pumpkins")?.Kind);
            Assert.AreEqual("candy_cane", Wishes.Read("make me a candy cane")?.Kind);
            Assert.AreEqual("parasol", Wishes.Read("put a beach umbrella on the table")?.Kind);
        }
    }
}

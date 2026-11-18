using System;
using System.Collections.Generic;
using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    public class WordsTests
    {
        static Heard Say(string text, Recipe cur = null) => Words.Parse(text, cur ?? Recipe.For("campfire"), 3);

        [TestCase("take me to a beach at sunset", "beach", 18.5f)]
        [TestCase("a jazz bar at 2 a.m. in the rain", "city", 2f)]
        [TestCase("space station", "space", -1f)]
        [TestCase("underwater coral reef", "reef", -1f)]
        [TestCase("the desert at dawn", "desert", 6.5f)]
        [TestCase("a cozy cabin in the mountains", "snow", -1f)]
        [TestCase("a picnic in a meadow", "meadow", -1f)]
        [TestCase("mars", "desert", -1f)]
        public void KnownPlacesBuildTheirWorld(string text, string biome, float hour)
        {
            var h = Say(text);
            Assert.AreEqual(Act.NewWorld, h.Act, text);
            Assert.AreEqual(biome, h.Result.Biome, text);
            if (hour >= 0) Assert.AreEqual(hour, h.Result.Hour, 0.01f, text);
            Assert.IsNotEmpty(h.Reply);
            Assert.IsNotEmpty(h.Result.Title);
        }

        [Test]
        public void EditsChangeTheCurrentWorld()
        {
            var fire = Recipe.For("campfire");
            Assert.AreEqual(Act.Edit, Say("darker", fire).Act);
            Assert.Less(Say("darker", fire).Result.Light, fire.Light);
            Assert.AreEqual(Precip.Rain, Say("make it rain", fire).Result.Precip);
            Assert.Greater(Say("more trees", fire).Result.Density, fire.Density);
            Assert.IsFalse(Say("no fireflies", fire).Result.Extras.Contains(Extras.Fireflies));
            Assert.IsTrue(Say("add northern lights", fire).Result.Extras.Contains(Extras.Aurora));
            var storm = Say("storm", fire).Result;
            Assert.AreEqual(Precip.Rain, storm.Precip);
            Assert.Greater(storm.Wind, 0.7f);
            Assert.AreEqual(Act.Edit, Say("clear skies please", Say("make it rain", fire).Result).Act);
            Assert.AreEqual(Precip.None, Say("clear skies please", Say("make it rain", fire).Result).Result.Precip);
        }

        [TestCase("take me home", Act.End)]
        [TestCase("my room", Act.End)]
        [TestCase("back to my room", Act.End)]
        [TestCase("take me home", Act.End)]
        [TestCase("freeze", Act.Freeze)]
        [TestCase("resume", Act.Resume)]
        [TestCase("undo", Act.Undo)]
        [TestCase("save this as my happy place", Act.Save)]
        [TestCase("surprise me", Act.Surprise)]
        [TestCase("today's world", Act.Daily)]
        [TestCase("help", Act.Help)]
        public void Controls(string text, Act act) => Assert.AreEqual(act, Say(text).Act);

        [Test]
        public void SaveTakesItsName() => Assert.AreEqual("My happy place", Say("save this as my happy place").SaveName);

        [Test]
        public void UnknownPlacesAskTheAiAndKeepWhatWasUnderstood()
        {
            var castle = Say("a medieval village");
            Assert.AreEqual(Act.Unclear, castle.Act);
            Assert.IsTrue(castle.AskAi);
            Assert.AreEqual(3, castle.Choices.Length);
            var hog = Say("hogwarts at night");
            Assert.IsTrue(hog.AskAi, "unknown name goes to the AI");
            Assert.AreEqual(Act.Edit, hog.Act, "and 'at night' is still understood if the AI can't be asked");
        }

        [Test]
        public void MumblingAsksOneQuestion()
        {
            var h = Say("uh");
            Assert.AreEqual(Act.Unclear, h.Act);
            Assert.IsFalse(h.AskAi);
            Assert.IsNotEmpty(h.Question);
        }

        [TestCase("2 am", 2f)]
        [TestCase("10 pm", 22f)]
        [TestCase("two in the morning", 2f)]
        [TestCase("seven o'clock in the evening", 19f)]
        [TestCase("12 am", 0f)]
        [TestCase("6:30 pm", 18.5f)]
        public void ClockTimes(string text, float hour) => Assert.AreEqual(hour, Words.ClockHour(text).Value, 0.01f);

        [Test]
        public void TitlesAreCleanAndShort()
        {
            Assert.AreEqual("", Words.CleanTitle("fuck island"));
            Assert.LessOrEqual(Words.CleanTitle(new string('a', 100)).Length, Words.MaxTitle);
            StringAssert.DoesNotContain("<", Words.CleanTitle("Beach, at night<script>"));
            Assert.IsFalse(Words.IsClean("go to sh it hole".Replace(" ", "")));
            Assert.IsTrue(Words.IsClean("a grape field in Essex"));
        }
    }

    public class RecipeTests
    {
        [Test]
        public void EveryWorldHasEverythingItNeeds()
        {
            foreach (var b in Kit.Biomes)
            {
                Assert.IsNotNull(b.Palette, b.Id);
                foreach (Role role in Enum.GetValues(typeof(Role))) Assert.IsTrue(b.Skins.ContainsKey(role), b.Id + " " + role);
                Assert.IsNotEmpty(b.Scatter, b.Id);
                Assert.IsNotEmpty(b.Item, b.Id);
                Assert.IsNotEmpty(b.Build, b.Id);
                Assert.IsNotEmpty(b.Souvenir, b.Id);
                Assert.IsNotEmpty(b.TaskLine, b.Id);
                Assert.IsNotEmpty(b.Words, b.Id);
                var r = Recipe.For(b.Id);
                Assert.AreEqual(b.Id, r.Biome);
                Assert.AreEqual(b.Id, Words.BestBiome(b.Words[0], out _), "first word picks its world: " + b.Words[0]);
            }
        }

        [Test]
        public void JsonRoundTrip()
        {
            var r = Recipe.For("city");
            r.Extras.Add(Extras.Aurora);
            var back = Recipe.FromJson(Json.Parse(Json.Write(r.ToJson())) as Dictionary<string, object>);
            Assert.IsTrue(back.SameWorld(r));
        }

        [Test]
        public void HostileJsonIsClamped()
        {
            var junk = Recipe.FromJson(Json.FirstObject("Sure! ```json {\"biome\":\"tropical island\",\"hour\":99,\"fog\":7,\"density\":-5,\"extras\":[\"aurora\",\"dragons\",\"aurora\"],\"title\":\"Fuck island\"} ```"));
            Assert.AreEqual("beach", junk.Biome);
            Assert.AreEqual(3f, junk.Hour, 0.01f);
            Assert.AreEqual(1f, junk.Fog);
            Assert.AreEqual(Recipe.MinDensity, junk.Density);
            CollectionAssert.AreEqual(new[] { Extras.Aurora }, junk.Extras);
            Assert.AreEqual(junk.Def.Name, junk.Title);
            Assert.IsNull(Recipe.FromJson(Json.Parse("{\"biome\":\"qwerty\"}") as Dictionary<string, object>));
        }

        [Test]
        public void UnderwaterOnlyBubblesAndSpaceNoWeather()
        {
            var reef = Recipe.For("reef"); reef.Precip = Precip.Rain; reef.Clamp();
            Assert.AreEqual(Precip.Bubbles, reef.Precip);
            var space = Recipe.For("space"); space.Precip = Precip.Snow; space.Extras.Add(Extras.Fireflies); space.Clamp();
            Assert.AreEqual(Precip.None, space.Precip);
            Assert.IsFalse(space.Extras.Contains(Extras.Fireflies));
        }

        [Test]
        public void TheDailyWorldIsTheSameAllDayAndCoversEveryWorld()
        {
            var day = new DateTime(2026, 11, 11);
            Assert.IsTrue(Recipe.Daily(day.AddHours(1)).SameWorld(Recipe.Daily(day.AddHours(23))));
            var seen = new HashSet<string>();
            var cycleStart = new DateTime(2026, 1, 1).AddDays(Kit.Biomes.Length * 40);
            for (int d = 0; d < Kit.Biomes.Length; d++) seen.Add(Recipe.Daily(cycleStart.AddDays(d)).Biome);
            Assert.AreEqual(Kit.Biomes.Length, seen.Count, "one cycle visits every world once");
        }

        [Test]
        public void ShareCodesRoundTripAndRefuseTypos()
        {
            var rng = new Rng(5);
            int refused = 0, n = 300;
            for (int i = 0; i < n; i++)
            {
                var x = Recipe.For(Kit.Biomes[rng.Range(0, Kit.Biomes.Length)].Id, rng.Range(0, 4096));
                x.Hour = rng.Range(0, 48) / 2f; x.Fog = rng.Next01(); x.Density = rng.Range(0.3f, 1.6f);
                x.Clamp();
                string code = ShareCode.Encode(x);
                Assert.AreEqual(ShareCode.Length + 2, code.Length);
                var y = ShareCode.Decode(code);
                Assert.IsNotNull(y, code);
                Assert.AreEqual(x.Biome, y.Biome);
                Assert.AreEqual(x.Hour, y.Hour, 0.01f);
                Assert.AreEqual(x.Seed, y.Seed);
                Assert.IsNotNull(ShareCode.Decode(code.ToLowerInvariant().Replace("-", " ")), "lower case and spaces read");
                char[] c = code.ToCharArray();
                int at = rng.Range(0, c.Length); if (c[at] == '-') at = 0;
                c[at] = c[at] == 'A' ? 'B' : 'A';
                if (ShareCode.Decode(new string(c)) == null) refused++;
            }
            Assert.Greater(refused, n * 0.95f, "nearly every typo is refused");
            Assert.IsNull(ShareCode.Decode("hello"));
        }

        [Test]
        public void FurnitureRolesFollowWhatItIsFor()
        {
            Assert.AreEqual(Role.Surface, Kit.RoleOf(new[] { "TABLE", "OTHER" }));
            Assert.AreEqual(Role.Seat, Kit.RoleOf(new[] { "COUCH" }));
            Assert.AreEqual(Role.Seat, Kit.RoleOf(new[] { "BED" }));
            Assert.AreEqual(Role.Tall, Kit.RoleOf(new[] { "STORAGE" }));
            Assert.AreEqual(Role.Tall, Kit.RoleOf(new[] { "SCREEN" }));
            Assert.AreEqual(Role.Light, Kit.RoleOf(new[] { "LAMP" }));
            Assert.AreEqual(Role.Door, Kit.RoleOf(new[] { "DOOR_FRAME" }));
            Assert.AreEqual(Role.Clutter, Kit.RoleOf(new[] { "OTHER" }));
            Assert.IsNull(Kit.RoleOf(new[] { "WALL_FACE" }));
            Assert.IsNull(Kit.RoleOf(new[] { "FLOOR" }));
        }
    }

    public class ProgressTests
    {
        [Test]
        public void RoundTripAndOneSouvenirPerWorld()
        {
            var p = new Progress();
            var fire = Recipe.For("campfire");
            p.Save("My happy place", fire, DateTime.UtcNow);
            Assert.IsNotNull(p.Finished(fire, DateTime.UtcNow));
            Assert.IsNull(p.Finished(fire, DateTime.UtcNow), "one souvenir per world");
            p.DailyDone(new DateTime(2026, 11, 10)); p.DailyDone(new DateTime(2026, 11, 11));
            var q = Progress.Parse(p.ToJsonString(), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual(1, q.Programs.Count);
            Assert.AreEqual(1, q.Souvenirs.Count);
            Assert.AreEqual(2, q.DailyStreak);
            Assert.AreEqual(2, q.Built["campfire"]);
        }

        [Test]
        public void StreakBreaksAfterAMissedDay()
        {
            var p = new Progress();
            p.DailyDone(new DateTime(2026, 11, 8)); p.DailyDone(new DateTime(2026, 11, 9)); p.DailyDone(new DateTime(2026, 11, 11));
            Assert.AreEqual(1, p.DailyStreak);
            Assert.AreEqual(2, p.BestDailyStreak);
        }

        [Test]
        public void DamagedFilesStartFresh()
        {
            var p = Progress.Parse("{not json", out bool damaged);
            Assert.IsTrue(damaged);
            Assert.AreEqual(0, p.Programs.Count);
            Progress.Parse("", out bool empty);
            Assert.IsFalse(empty);
        }

        [Test]
        public void ListsAreCapped()
        {
            var p = new Progress();
            for (int i = 0; i < 60; i++) { var r = Recipe.For("beach", i); p.Save("Beach " + i, r, DateTime.UtcNow); }
            Assert.AreEqual(Progress.MaxPrograms, p.Programs.Count);
            var same = Recipe.For("beach", 59);
            p.Save("Renamed", same, DateTime.UtcNow);
            Assert.AreEqual(Progress.MaxPrograms, p.Programs.Count, "the same world saved again is renamed, not added");
        }
    }

    public class SpeechAndAiTests
    {
        [Test]
        public void SpeechNeverLeavesTheHeadset()
        {
            // No cloud speech in the app: the recogniser runs on the headset (Ears), nothing else turns audio into text.
            Assert.IsNull(System.Type.GetType("Worldeck.SpeechToText, Worldeck.Runtime"), "no cloud speech class");
        }

        [Test]
        public void TheProxysAnswerIsReadDefensively()
        {
            var a = new AiAnswer();
            RecipeAi.Read("{\"safety\":\"ok\",\"recipe\":{\"biome\":\"snow\",\"hour\":22,\"extras\":[\"aurora\"],\"title\":\"Castle-town in the snow at night\"},\"clarify\":null,\"choices\":[\"snow\",\"nonsense\"],\"source\":\"model\"}", a);
            Assert.IsNull(a.Error);
            Assert.AreEqual("snow", a.Recipe.Biome);
            Assert.AreEqual("Castle-town in the snow at night", a.Recipe.Title);
            CollectionAssert.AreEqual(new[] { "snow" }, a.Choices);
            var bad = new AiAnswer();
            RecipeAi.Read("<html>502</html>", bad);
            Assert.IsNotNull(bad.Error);
            var rude = new AiAnswer();
            RecipeAi.Read("{\"recipe\":{\"biome\":\"beach\"},\"clarify\":\"what the fuck?\"}", rude);
            Assert.AreEqual("", rude.Clarify);
        }
    }
}

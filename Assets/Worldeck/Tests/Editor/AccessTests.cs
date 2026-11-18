using System.Collections.Generic;
using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Comfort & access settings and records: kept between launches, shown plainly, earned fairly.</summary>
    public class AccessTests
    {
        [Test]
        public void SettingsAndRecordsSurviveSaving()
        {
            var p = new Progress { CaptionsLarge = true, GentleStorm = true, CalmEffects = true, LookToPress = true, SoundLevel = 2, BestBlocked = 9, Landings = 2 };
            p.PlacesSeen.Add("On Mars");
            var back = Progress.Parse(p.ToJsonString(), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.IsTrue(back.CaptionsLarge && back.GentleStorm && back.CalmEffects && back.LookToPress);
            Assert.AreEqual(2, back.SoundLevel);
            Assert.AreEqual(9, back.BestBlocked);
            Assert.AreEqual(2, back.Landings);
            CollectionAssert.AreEqual(new[] { "On Mars" }, back.PlacesSeen);
        }

        [Test]
        public void OldSavesKeepNormalSettings()
        {
            var back = Progress.Parse("{\"v\":1,\"launches\":3,\"onboarded\":true}", out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual(1, back.SoundLevel, "normal sound");
            Assert.IsFalse(back.CaptionsLarge || back.GentleStorm || back.CalmEffects || back.LookToPress);
        }

        [Test]
        public void BadValuesAreClamped()
        {
            var back = Progress.Parse("{\"v\":1,\"sound_level\":9,\"best_blocked\":-4}", out _);
            Assert.AreEqual(2, back.SoundLevel);
            Assert.AreEqual(0, back.BestBlocked);
        }

        [Test]
        public void RecordsReadPlainly()
        {
            var p = new Progress { MissionChapter = 1, Landings = 1, BestBlocked = 7 };
            p.Visits["campfire"] = 2; p.Visits["space"] = 1;
            p.PlacesSeen.Add("On Mars");
            string t = Records.Text(p);
            StringAssert.Contains("chapter 2 of 3", t);
            StringAssert.Contains("1 landing", t);
            StringAssert.Contains("blocked in one storm: 7", t);
            StringAssert.Contains("visited: 3", t);
            foreach (var line in t.Split('\n')) Assert.Less(line.Length, 60, "short enough to read on the palm: " + line);
        }

        [Test]
        public void AchievementsAreEarnedNotGiven()
        {
            Assert.IsEmpty(Records.Earned(new Progress()), "nothing for nothing");
            var p = new Progress { Landings = 1, MissionChapter = Story.Chapters, StormBest = 1 };
            for (int i = 0; i < Records.WorldwalkerCount; i++) p.Visits["w" + i] = 1;
            p.PlacesSeen.Add("On Mars");
            var e = Records.Earned(p);
            CollectionAssert.IsSubsetOf(new[] { "FIRST_LANDING", "SPRING_RETURNS", "UNTOUCHED", "WORLDWALKER", "SAY_A_WORLD" }, e);
            CollectionAssert.AreEqual(e, new HashSet<string>(e), "no repeats");
        }

        [Test]
        public void TheAchievementNamesMatchThePlatformNames()
        {
            var names = new[] { MetaPlatform.FirstLanding, MetaPlatform.StoryDone, MetaPlatform.Untouched, MetaPlatform.AllWonders, MetaPlatform.Worldwalker, MetaPlatform.SayAWorld };
            var p = new Progress { Landings = 1, MissionChapter = Story.Chapters, StormBest = 1 };
            for (int i = 0; i < Records.WorldwalkerCount; i++) p.Visits["w" + i] = 1;
            p.PlacesSeen.Add("x");
            foreach (var n in Records.Earned(p, true, true)) CollectionAssert.Contains(names, n, n + " is one of the platform's names");
        }
    }
}

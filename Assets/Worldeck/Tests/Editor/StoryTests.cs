using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck.Tests
{
    /// <summary>The Last Signal: every chapter has its words, choices mean what they say, and endings follow the storm.</summary>
    public class StoryTests
    {
        [Test]
        public void EveryLineOfHersFitsHerCaptionWithNoWordAlone()
        {
            // Her caption is 34 characters wide; a line that only just spills over left "me." or "see." alone (review 39).
            foreach (var s in Story.EverythingSpoken())
            {
                string fit = Story.Fit(s);
                Assert.AreEqual(Story.VoiceKey(s), Story.VoiceKey(fit), "still her recording: " + s);
                foreach (var l in fit.Split('\n')) Assert.LessOrEqual(l.Length, 34, "fits: " + l);
                foreach (var para in s.Split('\n'))   // a paragraph it had to break: no word alone ("Please." on its own is meant)
                    if (para.Length > 34)
                        foreach (var l in Story.Fit(para).Split('\n')) Assert.IsTrue(l.Contains(" "), "no word alone: " + para);
            }
            Assert.AreEqual("A meteor field took\nout my navigation.", Story.Fit("A meteor field took out my navigation."));
            Assert.AreEqual("Short line.", Story.Fit("Short line."));
        }

        [Test]
        public void TheWelcomeFitsHerCaption()
        {
            // Juniper's caption wraps at 34: a longer line leaves one word alone underneath (review 38).
            foreach (var line in (Story.Welcome + "\n" + Story.LightNotice).Split('\n'))
                Assert.LessOrEqual(line.Length, 34, "fits on one line: " + line);
        }

        [Test]
        public void ComingBackSheHasBeenCalling()
        {
            for (int ch = 1; ch < Story.Chapters; ch++)
            {
                StringAssert.Contains("Mara", Story.WhileAway(ch));
                Assert.Less(Story.WhileAway(ch).Length, 110, "short enough to read at a glance");
            }
        }

        [Test]
        public void EveryChapterHasACallChoicesAndEndings()
        {
            for (int ch = 0; ch <= Story.Chapters; ch++)
            {
                Assert.IsNotEmpty(Story.Title(ch));
                Assert.IsNotEmpty(Story.Call(ch), "call " + ch);
                Assert.IsNotEmpty(Story.Answer(ch, false));
                Assert.AreEqual(2, Story.Paces(ch).Length);
                foreach (Landing l in new[] { Landing.Clean, Landing.Rough, Landing.Lost })
                    Assert.IsNotEmpty(Story.End(ch, l, "sofa", 5), ch + " " + l);
                Assert.AreEqual(2, Story.Farewell(ch).Length);
                foreach (var c in Story.Farewell(ch)) Assert.IsNotEmpty(Story.Reply(ch, c.Id).Text);
            }
        }

        [Test]
        public void EveryLineIsShortCleanAndOriginal()
        {
            var all = Enumerable.Range(0, Story.Chapters + 1).SelectMany(ch =>
                Story.Call(ch).Concat(new[] { Story.AskPace(ch) })
                    .Concat(new[] { Landing.Clean, Landing.Rough, Landing.Lost }.SelectMany(l => Story.End(ch, l, "sofa and shelf", 7)))
                    .Concat(Story.Farewell(ch).Select(c => Story.Reply(ch, c.Id)))).Select(l => l.Text)
                .Concat(Story.Who().Select(l => l.Text)).Concat(new[] { Story.Go("m:fast", true).Text, Story.Go("m:slow", false).Text, Story.LostHint, Story.Back, Story.Faint, Story.FirstBreak("sofa"), Story.FirstBreak("") }).ToList();
            foreach (var s in all)
            {
                foreach (var line in s.Split('\n')) Assert.LessOrEqual(line.Length, 46, "fits a caption line: " + line);
                Assert.IsTrue(Words.IsClean(s), s);
                string low = s.ToLowerInvariant();
                foreach (var borrowed in new[] { "computer", "program", "simulation", "captain", "red alert", "shields" })
                    StringAssert.DoesNotContain(borrowed, low, "no borrowed wording: " + s);
            }
        }

        [Test]
        public void TheStoryHasStakesAQuestionAndAMemory()
        {
            Assert.IsTrue(Story.Call(0).Any(l => l.Text.Contains("Medicine")), "what is at stake, in her first call (not behind a button)");
            StringAssert.Contains("why", Story.Hook(0)[0].Text, "the first chapter ends on a question");
            StringAssert.Contains("You're mine", Story.Hook(2)[1].Text, "the last answers it");
            Assert.IsEmpty(Story.Hook(Story.Chapters), "the daily flights don't");
            StringAssert.Contains("cracked", Story.Call(1, Landing.Rough)[0].Text, "she remembers a rough landing");
            StringAssert.Contains("patched up", Story.Call(1, Landing.Clean)[0].Text);
            Assert.IsTrue(Story.Call(2, Landing.Clean, "shelf").Any(l => l.Text.Contains("your shelf")), "and the piece that held");
            Assert.AreEqual(2, Story.Call(2, Landing.Clean, "furniture").Length, "nothing to remember about plain furniture");
            StringAssert.Contains("slow", Story.AskPace(2, "m:slow").Text, "and how you chose to fly");
            Assert.AreEqual("CHAPTER 2 OF 3\nThe Way Back Up", Story.TitleCard(1));
            foreach (var l in Story.StoryEnd("sofa").Split('\n')) Assert.LessOrEqual(l.Length, 46, l);
            // Every version of every line fits a caption and is recorded in her voice (MaraVoiceTests).
            foreach (var s in Story.EverythingSpoken()) foreach (var l in s.Split('\n')) Assert.LessOrEqual(l.Length, 46, l);
        }

        [Test]
        public void HerLastStormEndsWithABigOne()
        {
            var std = new Sim(4, Pace.Fast);
            foreach (int level in new[] { 0, 1 })
            {
                var s = new Sim(4, Pace.Fast, level);
                Assert.AreEqual(std.Strikes.Count, s.Strikes.Count);
                for (int i = 0; i < std.Strikes.Count; i++) Assert.AreEqual(std.Strikes[i].Time, s.Strikes[i].Time, 1e-5f, "her first two storms are as before");
                Assert.IsTrue(s.Strikes.All(x => x.Kind == Sim.Single));
            }
            var three = new Sim(4, Pace.Slow, 2);
            Assert.AreEqual(Sim.Big, three.Strikes[three.Strikes.Count - 1].Kind, "a big one to finish her third");
            Assert.AreEqual(1, three.Strikes.Count(x => x.Kind == Sim.Big), "just the one");
            Assert.Greater(three.Strikes[three.Strikes.Count - 1].Time - three.Strikes[three.Strikes.Count - 2].Time, 3f, "seen coming");
            foreach (var l in Story.BigWarning.Split('\n')) Assert.LessOrEqual(l.Length, 46, l);
        }

        [Test]
        public void DarkFurnitureIsRememberedUntilLitAgain()
        {
            var p = new Progress();
            p.Darken(new List<string> { "a", "b" }, new List<string> { "a" });
            CollectionAssert.AreEqual(new[] { "a" }, p.DarkPieces);
            var back = Progress.Parse(p.ToJsonString(), out _);
            CollectionAssert.AreEqual(new[] { "a" }, back.DarkPieces, "saved");
            back.Darken(new List<string> { "a", "c" }, new List<string> { "c" });
            CollectionAssert.AreEqual(new[] { "c" }, back.DarkPieces, "a held this time and is lit; c went dark");
            back.Darken(new List<string> { "d" }, new List<string>());
            CollectionAssert.AreEqual(new[] { "c" }, back.DarkPieces, "a piece the storm wasn't aimed at keeps its state");
            back.Darken(null, Enumerable.Range(0, 9).Select(i => "k" + i).ToList());
            Assert.AreEqual(8, back.DarkPieces.Count, "at most eight");
            Assert.AreEqual("k8", back.DarkPieces.Last(), "the newest is kept, the oldest goes");
            foreach (var piece in Story.Pieces.Where(x => x.Length > 0))
                foreach (var l in (Story.StillDark(piece) + "\n" + Story.Relit(piece) + "\n" + Story.RelitLine(piece) + "\n" + Story.RelitLine(piece, false) + "\n" + Story.ChapterDark(piece)).Split('\n'))
                    Assert.LessOrEqual(l.Length, 46, l);
        }

        [Test]
        public void EveryLocalHasAFavourTheHeadsetCanMake()
        {
            foreach (var l in Locals.All)
            {
                var f = Locals.FavourOf(l);
                Assert.NotNull(f, l.Name);
                Assert.IsTrue(MadeKinds.Has(f.Kind), l.Name + " asks for " + f.Kind + ", which the headset can make");
                Assert.IsTrue(Wishes.Read("make me " + f.Name) != null && Wishes.Read("make me " + f.Name).Kind == f.Kind, l.Name + ": \"make me " + f.Name + "\" makes a " + f.Kind);
                foreach (var line in (f.Ask + "\n" + f.Thanks + "\n" + f.Remember).Split('\n')) Assert.LessOrEqual(line.Length, 32, line);
            }
            var pip = Locals.FavourOf(Locals.Traveller("Somewhere"));
            Assert.IsTrue(MadeKinds.Has(pip.Kind), "the traveller asks for something too");
            // What they remember is saved.
            var p = new Progress();
            p.LocalMet("pirate_cove");
            p.LocalMade("pirate_cove", "lion");
            Assert.AreEqual(1, p.LocalVisits("pirate_cove"));
            StringAssert.Contains("lion", Locals.WelcomeBack(Locals.Get("pirate_cove"), false, p.LocalLastMade("pirate_cove")));
            p.LocalFavour("pirate_cove");
            var back = Progress.Parse(p.ToJsonString(), out _);
            Assert.IsTrue(back.FavourDone("pirate_cove"), "the favour is saved");
            Assert.AreEqual("lion", back.LocalLastMade("pirate_cove"));
            StringAssert.Contains("parrot", Locals.WelcomeBack(Locals.Get("pirate_cove"), true, "lion"), "and they remember it");
        }

        [Test]
        public void YourRoomsStoryIsWhatHappened()
        {
            StringAssert.Contains("your sofa went dark", Story.RoomStory("sofa", "sofa", true));
            StringAssert.Contains("Then you lit it again.", Story.RoomStory("sofa", "sofa", true));
            StringAssert.Contains("it held", Story.RoomStory("sofa", "sofa", true));
            StringAssert.DoesNotContain("held", Story.RoomStory("sofa", "sofa", false));
            StringAssert.Contains("lit your shelf again", Story.RoomStory("sofa", "shelf", true));
            Assert.AreEqual("", Story.RoomStory("", "", false), "nothing went dark: nothing to tell");
            StringAssert.Contains("You lit your shelf again.", Story.RoomStory("", "shelf", false), "no \"then\" with nothing before it");
            Assert.AreEqual("", Story.RoomStory("furniture", "", false), "no name for it: nothing to tell");
            foreach (var piece in Story.Pieces)
                foreach (var l in (Story.RoomStory(piece, piece, true) + "\n" + Story.BigAgain(piece.Length > 0 ? piece : "sofa")).Split('\n'))
                    Assert.LessOrEqual(l.Length, 46, l);
            var p = new Progress();
            p.RememberChapter(0, Landing.Rough, "m:slow", "", "sofa", "");
            p.RememberChapter(1, Landing.Clean, "m:fast", "shelf", "", "sofa");
            var back = Progress.Parse(p.ToJsonString(), out _);
            Assert.AreEqual("sofa", back.ChapterNote(0, 3), "what went dark is saved");
            Assert.AreEqual("sofa", back.LastRelit(), "and what was lit again");
            Assert.AreEqual(Landing.Rough, back.ChapterLanding(0, out string pace, out _), "older parts read as before");
            Assert.AreEqual("m:slow", pace);
        }

        [Test]
        public void WhatSheRemembersIsSaved()
        {
            var p = new Progress();
            p.RememberChapter(0, Landing.Rough, "m:slow", "sofa");
            p.RememberChapter(1, Landing.Clean, "m:fast", "sofa");
            var back = Progress.Parse(p.ToJsonString(), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual(Landing.Rough, back.ChapterLanding(0, out string pace, out string held));
            Assert.AreEqual("m:slow", pace);
            Assert.AreEqual("sofa", held);
            Assert.AreEqual(Landing.None, back.ChapterLanding(-1, out _, out _), "before her first call");
            Assert.AreEqual(Landing.None, back.ChapterLanding(4, out _, out _));
            Assert.AreEqual("sofa", back.HeldThroughout(), "the sofa held every time");
            back.StoryEndSeen = true;
            Assert.IsTrue(Progress.Parse(back.ToJsonString(), out _).StoryEndSeen);
            Assert.AreEqual(0, Progress.Parse("{\"mission_chapter\":2}", out _).MaraLog.Count, "an older save simply has no memory");
        }

        [TestCase("let's go slow", "m:slow")]
        [TestCase("take it slow please", "m:slow")]
        [TestCase("come in fast", "m:fast")]
        [TestCase("straight through the middle", "m:fast")]
        [TestCase("fast or slow", null)]
        [TestCase("make it rain", null)]
        public void SpokenChoicesAreUnderstood(string said, string id) => Assert.AreEqual(id, Story.FromWords(said, Story.Paces(0)), said);

        [TestCase("yes I'll guide you", "m:guide")]
        [TestCase("who are you", "m:who")]
        [TestCase("not now", "m:later")]
        [TestCase("call me later", "m:later")]
        public void SpokenAnswersAreUnderstood(string said, string id) => Assert.AreEqual(id, Story.FromWords(said, Story.Answer(0, false)), said);

        [Test]
        public void ThePaceChangesTheStorm()
        {
            var fast = new Sim(4, Pace.Fast);
            var slow = new Sim(4, Pace.Slow);
            Assert.AreEqual(Pace.Fast.Count, fast.Strikes.Count);
            Assert.AreEqual(Pace.Slow.Count, slow.Strikes.Count);
            Assert.Greater(slow.Strikes[2].Time - slow.Strikes[0].Time, fast.Strikes[2].Time - fast.Strikes[0].Time, "the slow approach gives more time between the first meteors");
            // The standard storm is unchanged (the existing tests and saved results depend on it).
            var a = new Sim(4); var b = new Sim(4, Pace.Standard);
            for (int i = 0; i < a.Strikes.Count; i++) Assert.AreEqual(a.Strikes[i].Time, b.Strikes[i].Time, 1e-5f);
        }

        [Test]
        public void EverySurvivedChapterLeavesAKeepsake()
        {
            for (int ch = 0; ch < Story.Chapters; ch++)
            {
                Assert.IsNotEmpty(Story.Keepsake(ch));
                StringAssert.Contains(Story.Keepsake(ch).Split(' ')[0], Story.KeepsakeLine(ch).Text.ToLowerInvariant());
                var mesh = Souvenirs.Keepsake(ch);
                Assert.Greater(mesh.vertexCount, 20);
                Assert.Less(mesh.bounds.size.magnitude, 0.12f, "small enough to stand on a table");
                Assert.GreaterOrEqual(mesh.bounds.min.y, -0.001f, "stands on its base");
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MaraHasARadioVoice()
        {
            var a = RadioVoice.Samples("Hello? Is anyone there?", 1f);
            var b = RadioVoice.Samples("Hello? Is anyone there?", 1f);
            Assert.AreEqual(a.Length, b.Length);
            Assert.AreEqual(a[a.Length / 2], b[a.Length / 2], 1e-6f, "the same line sounds the same");
            Assert.Greater(a.Length, RadioVoice.Rate / 2, "long enough to hear");
            Assert.LessOrEqual(a.Length, (int)(RadioVoice.MaxSeconds * RadioVoice.Rate) + 1, "never longer than a breath");
            float peak = 0f, energy = 0f;
            foreach (var x in a) { peak = Mathf.Max(peak, Mathf.Abs(x)); energy += x * x; }
            Assert.LessOrEqual(peak, 1f);
            Assert.Greater(energy / a.Length, 1e-3f, "not silent");
            var weak = RadioVoice.Samples("Losing you...", 0.1f);
            Assert.Greater(weak.Length, 0);
        }

        [Test]
        public void LandingsFollowTheStorm()
        {
            Assert.AreEqual(Landing.Lost, Story.LandingFor(true, 2));
            Assert.AreEqual(Landing.Clean, Story.LandingFor(false, 0));
            Assert.AreEqual(Landing.Clean, Story.LandingFor(false, 2));
            Assert.AreEqual(Landing.Rough, Story.LandingFor(false, 3));
        }

        [Test]
        public void StaticEatsLettersButNotTheMeaning()
        {
            string s = Story.Hurt("sofa", 0.3f);
            Assert.AreNotEqual("Losing... your sofa... can't see...", s, "garbled when the signal is weak");
            Assert.AreEqual("Your sofa went dark! Losing you a bit...", Story.Hurt("sofa", 0.9f), "clear when the signal is strong");
            Assert.AreEqual(Story.Garble("hello there", 0.8f, 3), Story.Garble("hello there", 0.8f, 3), "the same every time");
        }

        [Test]
        public void MaraFliesEveryDayAndTheRunOfDaysCounts()
        {
            var p = new Progress();
            var d1 = new System.DateTime(2026, 11, 13, 20, 0, 0, System.DateTimeKind.Utc);
            p.FlightDone(d1);
            p.FlightDone(d1.AddHours(2));
            Assert.AreEqual(1, p.FlightStreak, "once a day");
            p.FlightDone(d1.AddDays(1));
            Assert.AreEqual(2, p.FlightStreak, "two days in a row");
            Assert.AreEqual(2, p.FlightStreakOn(d1.AddDays(2)), "still standing the next day");
            Assert.AreEqual(0, p.FlightStreakOn(d1.AddDays(3)), "a missed day ends it");
            p.FlightDone(d1.AddDays(3));
            Assert.AreEqual(1, p.FlightStreak);
            Assert.AreEqual(2, p.BestFlightStreak);
            var back = Progress.Parse(p.ToJsonString(), out _);
            Assert.AreEqual(p.LastFlight, back.LastFlight);
            Assert.AreEqual(1, back.FlightStreak);
            StringAssert.Contains("Mara's flying over the desert", Story.TodaysFlight("the desert", 0, false));
            StringAssert.Contains("3 days in a row", Story.TodaysFlight("the desert", 3, true));
            StringAssert.Contains("carrying a crate of oranges", Story.TodaysFlight("the desert", 0, false, "a crate of oranges"));
            var day = new System.DateTime(2026, 11, 13, 0, 0, 0, System.DateTimeKind.Utc);
            Assert.AreEqual(Story.Cargo(day), Story.Cargo(day.AddHours(20)), "the same all day");
            Assert.AreNotEqual(Story.Cargo(day), Story.Cargo(day.AddDays(1)), "different the next");
        }
    }
}
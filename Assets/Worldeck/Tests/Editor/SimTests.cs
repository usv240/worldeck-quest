using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>The emergency simulation: actions have consequences, the same choices give the same ending, rewind is exact.</summary>
    public class SimTests
    {
        static Sim Run(int seed, System.Action<Sim, float> play, float dt = 1f / 72f)
        {
            var s = new Sim(seed);
            s.Begin();
            float t = 0f;
            while (s.State.Phase < Sim.Aftermath && t < 200f) { play?.Invoke(s, t); s.Step(dt); t += dt; }
            return s;
        }

        [Test]
        public void PowerAlwaysSumsTo100()
        {
            var s = new Sim(3);
            foreach (var to in new[] { "shields", "engines", "lights", "balance" })
            {
                Assert.IsTrue(s.Divert(to), to);
                Assert.AreEqual(100f, s.State.Shields + s.State.Engines + s.State.Lights, 0.001f);
            }
            Assert.IsFalse(s.Divert("coffee"), "unknown targets change nothing");
        }

        [Test]
        public void DoingNothingGetsBattered_ShieldsAndEvadingGetThrough()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                var idle = Run(seed, null);
                Assert.Greater(idle.State.Taken, 0, "seed " + seed + ": ignoring the storm costs hull");
                var shields = Run(seed, (s, t) => { if (t < 0.1f) s.Divert("shields"); });
                Assert.Greater(shields.State.Hull, idle.State.Hull, "seed " + seed + ": shields help");
                Assert.Greater(shields.State.Blocked, idle.State.Blocked);
            }
        }

        [Test]
        public void EveryEndingCanBeReached()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int seed = 1; seed <= 12; seed++)
            {
                seen.Add(Run(seed, null).State.Outcome);
                seen.Add(Run(seed, (s, t) => { if (t < 0.1f) s.Divert("shields"); }).State.Outcome);
                // Shields first, then engines and an evade whenever a strike is close: the careful way.
                seen.Add(Run(seed, (s, t) =>
                {
                    if (t < 0.1f) s.Divert("shields");
                    if (s.State.Phase == Sim.Storm && s.State.ShieldCharge < Sim.BlockCost * 1.5f)
                    {
                        s.Divert("engines");
                        if (s.NextStrikeIn < 1f) s.Evade();
                    }
                    else if (s.State.Phase == Sim.Storm && s.State.T > s.State.EvadeUntil) s.Divert("shields");
                    for (int st = 0; st < Sim.Stations; st++) s.Repair(st);
                }).State.Outcome);
            }
            Assert.IsTrue(seen.Contains(Sim.Untouched), "untouched is possible");
            Assert.IsTrue(seen.Contains(Sim.Scraped), "scraped is possible");
            Assert.IsTrue(seen.Contains(Sim.Battered), "battered is possible");
        }

        [Test]
        public void TheSameChoicesEndTheSameWayAtAnyFrameRate()
        {
            System.Action<Sim, float> play = (s, t) => { if (t < 0.1f) s.Divert("shields"); };
            var a = Run(4, play, 1f / 72f);
            var b = Run(4, play, 1f / 90f);
            var c = Run(4, play, 1f / 30f);
            Assert.AreEqual(a.State.Outcome, b.State.Outcome);
            Assert.AreEqual(a.State.Taken, b.State.Taken);
            Assert.AreEqual(a.State.Taken, c.State.Taken);
            Assert.AreEqual(a.State.Blocked, c.State.Blocked);
        }

        [Test]
        public void RewindGoesBackExactlyAndADifferentChoiceChangesTheEnding()
        {
            var s = new Sim(2);
            s.Begin();
            float dt = 1f / 72f;
            while (s.State.T < 14f) s.Step(dt);
            // The state 10 s ago, as recorded.
            Assert.IsTrue(s.TryPast(10f, out var past));
            Assert.That(past.T, Is.InRange(2.9f, 4.05f));
            int pastHash = past.Hash();
            while (s.State.Phase < Sim.Aftermath) s.Step(dt);
            int firstOutcome = s.State.Outcome, firstTaken = s.State.Taken;
            Assert.Greater(firstTaken, 0, "doing nothing took hits");
            // All the way back to before the storm.
            Assert.IsTrue(s.Rewind(s.State.T - 3.5f));
            Assert.AreEqual(pastHash, s.State.Hash(), "rewind restores the recorded state exactly");
            Assert.AreEqual(100f, s.State.Hull);
            // This time: shields.
            s.Divert("shields");
            while (s.State.Phase < Sim.Aftermath) s.Step(dt);
            Assert.Less(s.State.Taken, firstTaken, "a different choice, a different ending");
            Assert.LessOrEqual(s.State.Outcome, firstOutcome);
        }

        [Test]
        public void RewindThenTheSameChoicesReplaysTheSameStorm()
        {
            var s = new Sim(6);
            s.Begin();
            float dt = 1f / 72f;
            while (s.State.Phase < Sim.Aftermath) s.Step(dt);
            int outcome = s.State.Outcome, taken = s.State.Taken, blocked = s.State.Blocked;
            for (int k = 0; k < 5; k++)
            {
                Assert.IsTrue(s.Rewind(s.State.T));
                Assert.Less(s.State.T, 1.05f);
                while (s.State.Phase < Sim.Aftermath) s.Step(dt);
                Assert.AreEqual(outcome, s.State.Outcome, "replay " + k);
                Assert.AreEqual(taken, s.State.Taken);
                Assert.AreEqual(blocked, s.State.Blocked);
            }
        }

        [Test]
        public void ABrokenPowerStationSlowsTheShieldsUntilRepaired()
        {
            var a = new Sim(1); a.Begin(); a.Divert("shields");
            var b = new Sim(1); b.Begin(); b.Divert("shields"); Assert.IsTrue(b.Damage(0));
            a.Step(5f); b.Step(5f);
            Assert.Greater(a.State.ShieldCharge, b.State.ShieldCharge);
            Assert.IsTrue(b.Repair(0));
            Assert.IsFalse(b.Repair(1), "one repair at a time, and only what is broken");
            b.Step(Sim.RepairSeconds + 0.1f);
            Assert.IsFalse(b.IsBroken(0));
            Assert.AreEqual(-1, b.State.Repairing);
        }

        [Test]
        public void EvadeNeedsEnginePowerAndHasACooldown()
        {
            var s = new Sim(1);
            s.Begin();
            Assert.IsFalse(s.Evade(), "not with the engines at 40%");
            s.Divert("engines");
            Assert.IsTrue(s.Evade());
            Assert.IsFalse(s.Evade(), "cooling down");
            s.Step(Sim.EvadeCooldown + 0.1f);
            Assert.IsTrue(s.Evade());
        }

        [Test]
        public void WithABlockerOnlyWhatIsInTheWayIsBlocked()
        {
            var s = new Sim(3);
            int n = 0;
            s.Blocker = strike => (n++ % 2) == 0;   // every other one
            s.Begin();
            while (s.State.Phase < Sim.Aftermath) s.Step(1f / 72f);
            Assert.AreEqual(s.Strikes.Count, s.State.Blocked + s.State.Taken);
            Assert.AreEqual((s.Strikes.Count + 1) / 2, s.State.Blocked);
            Assert.AreEqual(Sim.Battered, s.State.Outcome);
            Assert.AreEqual(s.State.Taken, System.Convert.ToString(s.State.HitMask, 2).Replace("0", "").Length, "every hit is remembered");
            var all = new Sim(3) { Blocker = _ => true };
            all.Begin();
            while (all.State.Phase < Sim.Aftermath) all.Step(1f / 72f);
            Assert.AreEqual(Sim.Untouched, all.State.Outcome);
        }

        [Test]
        public void BadActionsChangeNothing()
        {
            var s = new Sim(1);
            s.Begin();
            int before = s.State.Hash();
            Assert.IsFalse(s.Repair(-1));
            Assert.IsFalse(s.Repair(99));
            Assert.IsFalse(s.Repair(0), "nothing is broken");
            Assert.IsFalse(s.Damage(7));
            Assert.IsFalse(s.Rewind(0f));
            Assert.IsFalse(s.Begin(), "already begun");
            Assert.AreEqual(before, s.State.Hash());
        }
    }
}

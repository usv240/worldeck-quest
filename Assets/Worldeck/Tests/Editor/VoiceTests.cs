using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>On-device voice: Juniper's name in what the recogniser heard, and the words that follow it.</summary>
    public class VoiceTests
    {
        [TestCase("Juniper, make it rain.", "make it rain")]
        [TestCase("Hey Juniper, darker please", "darker please")]
        [TestCase("hey, juniper... a cabin in the snow at night", "a cabin in the snow at night")]
        [TestCase("Junipers can you play some music?", "play some music")]
        [TestCase("Juniper please freeze", "freeze")]
        [TestCase("Okay Juniper. Take me to the beach.", "take me to the beach")]
        [TestCase("Junipor, take me home.", "take me home")]
        public void TheNameWakesItAndTheRestIsTheCommand(string heard, string command)
        {
            Assert.IsTrue(WakeWord.Find(heard, out string c), heard);
            Assert.AreEqual(command, c);
        }

        [TestCase("Juniper.")]
        [TestCase("Hey Juniper")]
        [TestCase("Juniper, Juniper.")]
        [TestCase("June per")]
        [TestCase("Juni per.")]
        public void TheNameAloneWaitsForTheCommand(string heard)
        {
            Assert.IsTrue(WakeWord.Find(heard, out string c), heard);
            Assert.AreEqual("", c);
        }

        // What woke the old name by mistake, and ordinary talk: none of it is the name.
        [TestCase("The number is seven.")]
        [TestCase("Members.")]
        [TestCase("M. B.")]
        [TestCase("Remember to buy milk")]
        [TestCase("We went to Jupiter")]
        [TestCase("Jupiter.")]
        [TestCase("make it rain")]
        [TestCase("I told my friend about the juniper bush")]   // a mention half-way through doesn't count
        [TestCase("")]
        public void OtherTalkInTheRoomIsIgnored(string heard)
        {
            Assert.IsFalse(WakeWord.Find(heard, out _), heard);
        }

        [TestCase("uh", false)]
        [TestCase("the", false)]
        [TestCase("Thank you.", false)]
        [TestCase("darker", true)]
        [TestCase("rain", true)]
        [TestCase("a beach at sunset", true)]
        public void RoomNoiseIsNotACommand(string heard, bool command) => Assert.AreEqual(command, WakeWord.IsCommand(heard));

        [TestCase("play some music", Act.Music)]
        [TestCase("put on some music", Act.Music)]
        [TestCase("play a song", Act.Music)]
        [TestCase("stop the music", Act.MusicOff)]
        [TestCase("music off", Act.MusicOff)]
        public void MusicIsAControlNotAPlace(string said, Act act)
        {
            var h = Words.Parse(said, Recipe.For("campfire", 1), 3);
            Assert.AreEqual(act, h.Act, said);
        }

        [TestCase("rewind", Act.Rewind, "30")]
        [TestCase("go back twenty seconds", Act.Rewind, "20")]
        [TestCase("try again", Act.Rewind, "30")]
        [TestCase("evade", Act.Evade, "")]
        [TestCase("fix it", Act.Repair, "")]
        [TestCase("start a meteor storm", Act.Storm, "")]
        public void EmergencyWordsAreControls(string said, Act act, string arg)
        {
            var h = Words.Parse(said, Recipe.For("space", 1), 3);
            Assert.AreEqual(act, h.Act, said);
            Assert.AreEqual(arg, h.Arg, said);
        }

        [TestCase("junipor make it rain", true)]
        [TestCase("Jeniper, darker", true)]
        [TestCase("juniperr play some music", true)]
        [TestCase("a member of the club", false)]
        [TestCase("number nine", false)]
        [TestCase("maybe", false)]
        [TestCase("a lamp", false)]
        // The name said alone was heard as "Remember?" (Nov 13 night, just before "Me to India"): alone, it is the name.
        [TestCase("Remember?", true)]
        [TestCase("Do you remember?", false)]
        [TestCase("Juniper add embers", true)]
        public void TheNameIsHeardEvenWhenSlightlyMisheard(string said, bool woke) => Assert.AreEqual(woke, WakeWord.Find(said, out _), said);

        [TestCase("end program")]
        [TestCase("turn my room into a beach")]
        [TestCase("make my room a forest at night")]
        [TestCase("computer, end program")]
        [TestCase("run program evening walk")]
        public void BorrowedCommandsAreNotUnderstood(string said) => Assert.AreNotEqual(Act.End, Words.Parse(said, Recipe.For("space", 1), 3).Act, said);


        [TestCase("go back")]
        [TestCase("never mind")]
        public void PlainGoBackIsStillUndo(string said) => Assert.AreEqual(Act.Undo, Words.Parse(said, Recipe.For("space", 1), 3).Act);

        [TestCase("make it rian", "precip")]
        [TestCase("add fireflys", "extras")]
        [TestCase("a little darkr", "light")]
        [TestCase("make it romantic", "hour")]
        [TestCase("something magical", "extras")]
        public void MisheardWordsAndMoodsAreUnderstood(string said, string changed)
        {
            var start = Recipe.For("campfire", 1);
            start.Extras.Clear();   // so that adding fireflies is a change
            var h = Words.Parse(said, start, 3);
            Assert.AreEqual(Act.Edit, h.Act, said);
            StringAssert.Contains(changed, h.Changed, said);
        }

        [TestCase("rewined", Act.Rewind)]
        [TestCase("take me to the beech", Act.NewWorld)]
        public void MisheardCommandsStillWork(string said, Act act) => Assert.AreEqual(act, Words.Parse(said, Recipe.For("campfire", 1), 3).Act, said);

        [TestCase("i think there is a store")]
        [TestCase("where would that thing be")]
        public void EverydayTalkIsNotTurnedIntoCommands(string said)
        {
            Assert.AreEqual(Words.Normalise(said), Words.Soften(Words.Normalise(said)));
        }

        [Test]
        public void VoiceSettingsAreRemembered()
        {
            var p = new Progress { VoiceOn = true, VoiceOffered = true };
            var q = Progress.Parse(p.ToJsonString(), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.IsTrue(q.VoiceOn);
            Assert.IsTrue(q.VoiceOffered);
        }
    }
}

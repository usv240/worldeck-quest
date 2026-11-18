using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Mara's recorded voice: every line she can say has a recording, and lines split where she'd pause.</summary>
    public class MaraVoiceTests
    {
        [Test]
        public void EveryLineSheCanSayIsRecorded()
        {
            var missing = Story.EverythingSpoken().Where(l => Resources.Load<AudioClip>(RadioVoice.Folder + Story.VoiceKey(l)) == null).ToList();
            Assert.IsEmpty(missing, "not recorded (run Worldeck > Export Mara's lines, then Tools/voice/bake-mara-kokoro.py): " + string.Join(" | ", missing.Take(8)));
        }

        [Test]
        public void ALineWithPiecesSheNamesIsStillSpoken()
        {
            // The safe pieces change every run; the line is said sentence by sentence.
            var end = Story.End(0, Landing.Clean, "sofa and shelf", 6)[0].Text;
            Assert.IsTrue(RadioVoice.HasRecording(end), end);
            Assert.IsTrue(RadioVoice.HasRecording(Story.HurtSpoken("sofa", 0.2f)));
        }

        [TestCase("Touchdown! I'm down, I'm safe!\nYou blocked 6 of them.", 3)]
        [TestCase("Signal... lost...", 1)]
        [TestCase("...anyone... there...?\n...I need... a light...", 1)]
        [TestCase("I made my delivery run... and came back.", 1)]
        [TestCase("Hello? Is anyone there?\nThis is Mara. I'm a courier pilot.", 4)]
        public void SentencesSplitWhereSheStops(string line, int count) => Assert.AreEqual(count, Story.Sentences(line).Count, string.Join(" | ", Story.Sentences(line)));

        [Test]
        public void TheSameWordsHaveTheSameRecording()
        {
            Assert.AreEqual(Story.VoiceKey("Nice block!"), Story.VoiceKey("  nice   BLOCK! "));
            Assert.AreEqual(Story.VoiceKey("A\nB"), Story.VoiceKey("A B"));
            Assert.AreNotEqual(Story.VoiceKey("Nice block!"), Story.VoiceKey("Clean!"));
        }

        [Test]
        public void HerVoiceIsLouderThanStaticAndBreaksUpAsTheSignalFades()
        {
            string line = Story.Cheer(1);
            var clear = RadioVoice.Make(line, 1f);
            var weak = RadioVoice.Make(line, 0.1f);
            Assert.Greater(clear.length, 0.4f, "a real recording, not a blip");
            Assert.AreNotSame(clear, weak);
        }
    }
}

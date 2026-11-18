using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    public class InterviewTests
    {
        [Test]
        public void FourTouchesBuildAPlace()
        {
            var iv = new Interview(5);
            Assert.AreEqual("where", iv.Current.Key);
            iv.Choose(iv.Current.Choices.FindIndex(c => c.Label == "Mountains"));
            Assert.AreEqual("when", iv.Current.Key);
            iv.Choose(iv.Current.Choices.FindIndex(c => c.Label == "Night"));
            Assert.AreEqual("weather", iv.Current.Key);
            iv.Choose(iv.Current.Choices.FindIndex(c => c.Label == "Snow"));
            Assert.AreEqual("special", iv.Current.Key);
            iv.Choose(iv.Current.Choices.FindIndex(c => c.Label == "Northern lights"));
            Assert.IsTrue(iv.Done);
            var r = iv.Finish();
            Assert.AreEqual("snow", r.Biome);
            Assert.IsTrue(r.IsNight);
            Assert.AreEqual(Precip.Snow, r.Precip);
            Assert.IsTrue(r.Extras.Contains(Extras.Aurora));
            StringAssert.Contains("night", r.Title);
        }

        [Test]
        public void OneSentenceAnswersSeveralQuestions()
        {
            var iv = new Interview(5);
            Assert.IsTrue(iv.Say("a beach at sunset in the rain"));
            Assert.AreEqual("special", iv.Current.Key, "place, time and weather were all said");
            Assert.IsTrue(iv.Say("that's it"));
            Assert.IsTrue(iv.Done);
            var r = iv.Finish();
            Assert.AreEqual("beach", r.Biome);
            Assert.AreEqual(Precip.Rain, r.Precip);
            Assert.AreEqual(18.5f, r.Hour, 0.01f);
        }

        [Test]
        public void SpaceAndReefSkipQuestionsThatDontApply()
        {
            var iv = new Interview(5);
            iv.Choose(iv.Current.Choices.FindIndex(c => c.Label == "Space"));
            Assert.AreEqual("special", iv.Current.Key, "no time or weather in orbit");
        }

        [Test]
        public void MumblingKeepsTheQuestion()
        {
            var iv = new Interview(5);
            Assert.IsFalse(iv.Say("uh hmm"));
            Assert.AreEqual("where", iv.Current.Key);
        }

        [TestCase("describe a place", Act.Describe)]
        [TestCase("let me describe", Act.Describe)]
        [TestCase("open my happy place", Act.Run)]
        [TestCase("open my evening walk", Act.Run)]
        [TestCase("take me back to where i was", Act.LastWorld)]
        [TestCase("open the beach", Act.NewWorld)]
        public void RoomPhrases(string text, Act act) => Assert.AreEqual(act, Words.Parse(text, Recipe.For("campfire"), 3).Act, text);

        [Test]
        public void SavedProgramsAreFoundBySpokenName()
        {
            var p = new Progress();
            p.Save("My happy place", Recipe.For("beach"), System.DateTime.UtcNow);
            p.Save("Winter night", Recipe.For("snow", 4), System.DateTime.UtcNow);
            Assert.AreEqual("My happy place", p.Find("my happy place").Name);
            Assert.AreEqual("Winter night", p.Find("winter").Name);
            Assert.IsNull(p.Find("volcano"));
        }
    }
}

namespace Worldeck.Tests
{
    public class ShareTokenTests
    {
        [Test]
        public void AFriendsCodeIsKeptOnceAndSurvivesSaving()
        {
            var p = new Progress();
            string code = ShareCode.Encode(Recipe.For("snow", 42));
            Assert.IsNotNull(p.Receive(code.ToLowerInvariant(), System.DateTime.UtcNow), "codes read in any case");
            Assert.IsNotNull(p.Receive(code, System.DateTime.UtcNow));
            Assert.AreEqual(1, p.Codes.Count, "the same code is kept once");
            Assert.IsNull(p.Receive("HELLO-WORLD", System.DateTime.UtcNow), "a bad code is refused");
            var q = Progress.Parse(p.ToJsonString(), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual(1, q.Codes.Count);
            Assert.AreEqual("snow", ShareCode.Decode(q.Codes[0].Text).Biome);
        }

        [Test]
        public void EveryRoleHasAVerb()
        {
            foreach (Role role in System.Enum.GetValues(typeof(Role))) Assert.IsNotEmpty(RoomVerbs.Verb(role), role.ToString());
        }
    }
}

using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Typing: the top things you might mean, as you type.</summary>
    public class TypingTests
    {
        [TestCase("volc", "a volcano at nightfall")]
        [TestCase("mar", "on Mars")]
        [TestCase("rain", "make it rain")]
        [TestCase("haunt", "a haunted forest")]
        [TestCase("tent", "make me a tent")]
        [TestCase("home", "take me home")]
        public void TheFirstSuggestionIsWhatTheyMean(string typed, string first) => Assert.AreEqual(first, Suggestions.For(typed)[0], typed);

        [Test]
        public void LanternsAreOffered()
        {
            CollectionAssert.Contains(Suggestions.For("lant"), "make me a lantern");
            CollectionAssert.Contains(Suggestions.For("lant"), "add lanterns");
        }

        [Test]
        public void AtMostFourAndNoRepeats()
        {
            foreach (var t in new[] { "", "a", "make", "m", "the", "zz" })
            {
                var s = Suggestions.For(t);
                Assert.LessOrEqual(s.Count, 4, t);
                CollectionAssert.AllItemsAreUnique(s, t);
            }
        }

        [Test]
        public void EnterOnAPartWordOffersWhatTheyMeant()
        {
            var go = new UnityEngine.GameObject("type box");
            var box = go.AddComponent<TypeBox>();
            string sent = null;
            box.Submitted += (t, how) => sent = t;
            box.Show();
            foreach (var k in "volc") box.Key(k.ToString());
            Assert.AreEqual("volc", box.Text, "keys type");
            box.Key("\b");
            Assert.AreEqual("vol", box.Text, "delete deletes");
            box.Key("c");
            box.Done(box.Text);
            Assert.IsNull(sent, "a part-word is not sent as it is");
            Assert.IsTrue(box.Open, "the keyboard stays, offering what they meant");
            Assert.AreEqual("a volcano at nightfall", box.Shown[0]);
            box.Pick(box.Shown[0], "test");
            Assert.AreEqual("a volcano at nightfall", sent);
            box.Show();
            box.Done("make it rain");
            Assert.AreEqual("make it rain", sent, "something understood is done at once");
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void EveryWhatCanISayExampleIsUnderstood()
        {
            foreach (var p in HandPanel.SayExamples)
            {
                if (p == "make me a dinosaur") { Assert.IsNotNull(Wishes.Read(p), "a wish for the relay"); continue; }
                var h = Words.Parse(p, Recipe.For("campfire", 1), 3);
                bool understood = h.Act != Act.Unclear && h.Act != Act.None || (Wishes.Read(p)?.Kind ?? "").Length > 0 || Wishes.Change(p, new[] { "lantern" }) != null;
                Assert.IsTrue(understood, "\"" + p + "\" is not understood offline");
            }
        }

        [Test]
        public void NothingTypedStillOffersSomething() => Assert.AreEqual(4, Suggestions.For("").Count);

        [Test]
        public void SomethingUnknownCanBeSentAsItIs() => CollectionAssert.Contains(Suggestions.For("a giant rubber duck"), "a giant rubber duck");

        [Test]
        public void EverySuggestionIsUnderstoodOffline()
        {
            // Touching a suggestion must do something without the network: a place, a change, a thing or a control.
            foreach (var p in Suggestions.All)
            {
                var h = Words.Parse(p, Recipe.For("campfire", 1), 3);
                bool understood = h.Act != Act.Unclear && h.Act != Act.None || Wishes.Read(p) != null || Wishes.Change(p, new[] { "lantern" }) != null;
                Assert.IsTrue(understood, "\"" + p + "\" is not understood offline");
            }
        }
    }
}

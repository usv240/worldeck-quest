using NUnit.Framework;
using Worldeck.Core;

namespace Worldeck.Tests
{
    /// <summary>Things made from real models, words the recogniser gets wrong, Juniper's sound controls, spin, the tour.</summary>
    public class AliveTests
    {
        [TestCase("make me a lion", "lion")]
        [TestCase("make me a dinosaur", "dinosaur")]
        [TestCase("give me a t rex", "t_rex")]
        [TestCase("make me a bee", "bee")]
        [TestCase("can you make a horse", "horse")]
        [TestCase("make me two penguins", "penguin")]
        [TestCase("make me a robot", "robot")]
        [TestCase("make me a spaceship", "spaceship")]
        [TestCase("make me a dog on the table", "dog")]
        public void RealThingsAreMadeFromModels(string said, string kind)
        {
            var w = Wishes.Read(said);
            Assert.NotNull(w, said);
            Assert.AreEqual(kind, w.Kind, said);
            Assert.IsTrue(MadeKinds.Has(kind), kind + " is a real model");
        }

        [Test]
        public void EveryMadeKindIsInTheModelKit()
        {
            foreach (var m in MadeKinds.All)
            {
                Assert.IsTrue(ModelKit.Has(m.Kind), m.Kind + " has a model");
                Assert.That(m.Size, Is.InRange(ActAnswer.MinSize, ActAnswer.MaxSize), m.Kind);
                Assert.AreEqual(m.Size, Wishes.SizeOf(m.Kind), 0.001f, m.Kind + " is made at its own size");
            }
        }

        // Everything asked for in the Nov 13 session (heard-0858), made from the real model on the headset.
        [TestCase("And generate a dog.", "dog")]
        [TestCase("Uh, generate me a T. Rex.", "t_rex")]
        [TestCase("Generate me a mushroom.", "mushroom")]
        [TestCase("Generate me a person.", "person")]
        [TestCase("Generate a lion.", "lion")]
        // Heard on the headset, Nov 13 afternoon ("spawn" is never heard as spawn).
        [TestCase("Born me a puppy.", "dog")]
        [TestCase("Bonds some deers.", "deer")]
        [TestCase("Lawn may a big dog", "dog")]
        [TestCase("give me a horse. Lord, Fox.", "horse")]
        [TestCase("make me a T. V. X.", "t_rex")]
        [TestCase("generate me a T. Rex", "t_rex")]
        [TestCase("Uh, generate me a T. Rex.", "t_rex")]
        [TestCase("generate a lion", "lion")]
        [TestCase("Generate me an elephant.", "elephant")]
        [TestCase("Pawn another person, please.", "person")]
        [TestCase("you me a lion", "lion")]
        [TestCase("You, me, lion on my table", "lion")]
        [TestCase("you may be a bee", "bee")]
        public void MisheardWishesStillWork(string heard, string kind) => Assert.AreEqual(kind, Wishes.Read(heard)?.Kind, heard);

        [TestCase("I want some piece", "peace")]
        [TestCase("peace and quiet", "peace")]
        [TestCase("mute", "mute")]
        [TestCase("shh", "mute")]
        [TestCase("sound on", "on")]
        [TestCase("turn it down", "down")]
        [TestCase("it's too loud", "down")]
        [TestCase("louder", "up")]
        public void EmberControlsTheSound(string said, string what)
        {
            var h = Words.Parse(said, Recipe.For("campfire", 1), 2);
            Assert.AreEqual(Act.Sound, h.Act, said);
            Assert.AreEqual(what, h.Arg, said);
        }

        [TestCase("make it rain")]
        [TestCase("stop the music")]
        [TestCase("more fog")]
        public void OtherWordsAreNotSound(string said) => Assert.AreNotEqual(Act.Sound, Words.Parse(said, Recipe.For("campfire", 1), 2).Act, said);

        [TestCase("take me on a tour")]
        [TestCase("show me around")]
        public void ATourCanBeAsked(string said) => Assert.AreEqual(Act.Tour, Words.Parse(said, Recipe.For("campfire", 1), 2).Act, said);

        [TestCase("spin it", 1)]
        [TestCase("make it spin", 1)]
        [TestCase("rotate it", 1)]
        [TestCase("stop spinning", -1)]
        public void MadeThingsSpin(string said, int spin)
        {
            var c = Wishes.Change(said, new[] { "lion" });
            Assert.NotNull(c, said);
            Assert.AreEqual(spin, c.Spin, said);
        }

        // ---- Heard on the headset, Nov 12 evening ----

        [TestCase("had tree", "oak")]
        [TestCase("had a tree", "oak")]
        [TestCase("I had one person.", "person")]
        [TestCase("I had person.", "person")]
        [TestCase("Uh, give me puppy.", "dog")]
        [TestCase("you may a dog", "dog")]
        [TestCase("You dog.", "dog")]
        public void AddAndMakeAreHeardThroughTheNoise(string heard, string kind) => Assert.AreEqual(kind, Wishes.Read(heard)?.Kind, heard);

        [TestCase("ah, northern lights", "add northern lights")]
        [TestCase("Ride fireflies.", "add fireflies")]
        [TestCase("ah, make it rain", "make it rain")]
        public void AMisheardAddIsAnAdd(string heard, string meant)
        {
            var fire = Recipe.For("campfire", 1);
            var a = Words.Parse(heard, fire, 2);
            var b = Words.Parse(meant, fire, 2);
            Assert.AreEqual(b.Act, a.Act, heard);
            Assert.AreEqual(b.Result?.Line(), a.Result?.Line(), heard);
        }

        [TestCase("puppy", "dog", 1)]
        [TestCase("a lion", "lion", 1)]
        [TestCase("two penguins", "penguin", 2)]
        public void ARealThingNamedOnItsOwnIsAWish(string said, string kind, int count)
        {
            var w = Wishes.Read(said);
            Assert.NotNull(w, said);
            Assert.AreEqual(kind, w.Kind, said);
            Assert.AreEqual(count, w.Count, said);
            Assert.IsTrue(w.Bare, said);
        }

        [TestCase("a beach")]
        [TestCase("lanterns")]
        [TestCase("rain")]
        [TestCase("darker")]
        public void APlaceOrWeatherOnItsOwnIsNotAThing(string said) => Assert.IsNull(Wishes.Read(said), said);

        [TestCase("You mean for peace?")]
        [TestCase("peace please")]
        [TestCase("leave me in peace")]
        public void PeaceOnItsOwnIsQuiet(string said)
        {
            var h = Words.Parse(said, Recipe.For("campfire", 1), 2);
            Assert.AreEqual(Act.Sound, h.Act, said);
            Assert.AreEqual("peace", h.Arg, said);
        }

        [TestCase("Remove pumpkins.", true)]
        [TestCase("remove the pumpkins", true)]
        [TestCase("Remove all pumpkins", true)]
        [TestCase("remove all the pumpkins", true)]
        [TestCase("remove these pumpkins", true)]
        [TestCase("remove this pumpkin", false)]
        [TestCase("remove the pumpkin", false)]
        [TestCase("get rid of the pumpkin please", false)]
        public void MadeThingsGoByTheirName(string said, bool every)
        {
            var c = Wishes.Change(said, new[] { "pumpkins", "lion" });
            Assert.NotNull(c, said);
            Assert.IsTrue(c.Remove, said);
            Assert.AreEqual("pumpkins", c.Target, said);
            Assert.AreEqual(every, c.Every, said);
        }

        [TestCase("remove fog")]
        [TestCase("remove the rain")]
        [TestCase("clear the sky")]
        public void TheWorldsOwnThingsAreNotMadeThings(string said) => Assert.IsNull(Wishes.Change(said, new[] { "pumpkins", "lion" }), said);

        [Test]
        public void AThingIsNamedByItsLastWord() => Assert.AreEqual("big red dragon", Wishes.Change("remove the dragon", new[] { "big red dragon", "lion" })?.Target);

        // "Number." alone was the name itself, misheard on the headset (Nov 13 night): it wakes; in a sentence it is a word.
        [TestCase("Number.", true)]
        [TestCase("Number three.", false)]
        [TestCase("numbers", false)]
        [TestCase("Members.", false)]
        [TestCase("Juniper.", true)]
        public void NumbersNoLongerWakeIt(string said, bool woke) => Assert.AreEqual(woke, WakeWord.Find(said, out _), said);

        [TestCase("Mara calls: when I'm ready", "Mara calls:\nwhen I'm ready")]
        [TestCase("Go", "Go")]
        [TestCase("a volcano at nightfall", "a volcano at\nnightfall")]
        public void LongLabelsBreakNearTheMiddle(string label, string two) => Assert.AreEqual(two, Worldeck.PokeButton.TwoLines(label));

        // ---- Hold it close to look at it; life size ----

        [Test]
        public void EveryRealModelHasALifeSize()
        {
            foreach (var m in MadeKinds.All) Assert.Greater(LifeSize.Metres(m.Kind), 0f, m.Kind);
        }

        [TestCase("how fast could it run?", true)]
        [TestCase("What does it eat", true)]
        [TestCase("is it heavy", true)]
        [TestCase("make it bigger", false)]
        [TestCase("a volcano", false)]
        public void QuestionsAreKnown(string said, bool question) => Assert.AreEqual(question, Words.IsQuestion(said), said);

        [Test]
        public void EveryAnimalArrivesHomeAmongItsOwnKind()
        {
            foreach (var m in MadeKinds.All)
            {
                string home = Wildlife.HomeOf(m.Kind);
                if (home.Length == 0) continue;
                var herd = Wildlife.For(Places.Get(home).Recipe(1), m.Kind);
                Assert.GreaterOrEqual(System.Array.FindAll(herd, k => k == m.Kind).Length, 2, m.Kind + " among its own kind at " + home);
                Assert.IsTrue(System.Array.TrueForAll(herd, k => k == m.Kind), m.Kind + ": only its own kind");
                Assert.LessOrEqual(herd.Length, Wildlife.MaxAnimals);
            }
        }

        [Test]
        public void TakeMeAnywhereTheAtlas()
        {
            // Every spot is made from a world there is, with a landmark there is.
            foreach (var s in Atlas.All)
            {
                Assert.IsTrue(Places.Get(s.From) != null || Kit.Get(s.From) != null, s.Words[0] + " is made from " + s.From);
                Assert.IsTrue(s.Landmark.Length == 0 || System.Array.IndexOf(Atlas.Landmarks, s.Landmark) >= 0, s.Words[0] + ": " + s.Landmark);
                foreach (var w in s.Words)
                {
                    Assert.AreEqual(w, Words.Normalise(w), "as it would be heard");
                    var p = Atlas.Make(s, w);
                    Assert.NotNull(p, w);
                    var r = p.Recipe(1);
                    Assert.IsTrue(Words.IsClean(r.Look.Name), r.Look.Name);
                    Assert.LessOrEqual(r.Look.Short.Length, 14, r.Look.Short);
                    Assert.AreEqual(s.Landmark, r.Look.Landmark, w + " keeps its landmark through the recipe");
                    Assert.NotNull(Locals.For(r), "someone lives in " + w);
                    Assert.AreNotEqual("Pip", Locals.For(r).Name, w + " has the local of the world it is made from");
                }
            }
            // Asked for, it is a trip there: at once, offline, nothing left for Claude to guess.
            foreach (var (said, name, landmark) in new[]
            {
                ("Take me to India.", "India in the morning", "dome"), ("take me to egypt", "Egypt at golden hour", "pyramids"),
                ("let's go to Paris", "Paris at dusk", "tower"), ("I want to see the pyramids", "The Pyramids at golden hour", "pyramids"),
                ("London", "London in the rain", "clock_tower"), ("the Taj Mahal at night", "Taj Mahal", "dome"),
                ("take me to New York", "New York at night", "spire"), ("Kenya", "Kenya at golden hour", ""),
                ("go to Mount Fuji", "Mount Fuji in blossom", "peak"), ("take me to Turkey", "Turkey at dusk", "mosque"),
            })
            {
                var h = Words.Parse(said, null, 3);
                Assert.AreEqual(Act.NewWorld, h.Act, said);
                Assert.IsFalse(h.AskAi, said + " needs no help");
                Assert.IsFalse(h.Refine, said + " is not sent to be refined");
                Assert.NotNull(h.Result.Look, said);
                Assert.AreEqual(name, h.Result.Look.Name, said);
                Assert.AreEqual(landmark, h.Result.Look.Landmark, said);
            }
            Assert.AreEqual(21f, Words.Parse("the Taj Mahal at night", null, 3).Result.Hour, 2.5f, "and the time you ask for is kept");
            Assert.Less(Words.Parse("take me to Paris", null, 3).Result.Fog, 0.1f, "a clear dusk in Paris: the tower is seen");
            // The right thing on the horizon, or nothing: never another country's.
            Assert.AreEqual("onion_domes", Words.Parse("take me to Moscow", null, 3).Result.Look.Landmark);
            Assert.AreEqual("wall", Words.Parse("take me to the Great Wall of China", null, 3).Result.Look.Landmark);
            Assert.AreEqual("The Great Wall of China in the morning", Words.Parse("take me to the Great Wall of China", null, 3).Result.Look.Name);
            Assert.AreEqual("pagoda", Words.Parse("take me to China", null, 3).Result.Look.Landmark);
            Assert.AreEqual("step_pyramid", Words.Parse("take me to Chichen Itza", null, 3).Result.Look.Landmark);
            Assert.AreEqual("", Words.Parse("take me to Jerusalem", null, 3).Result.Look.Landmark);
            Assert.AreEqual("", Words.Parse("take me to Venice", null, 3).Result.Look.Landmark);
            Assert.AreEqual("", Words.Parse("take me to Ireland", null, 3).Result.Look.Landmark);
            Assert.AreEqual("stone_circle", Words.Parse("take me to Stonehenge", null, 3).Result.Look.Landmark);
            Assert.AreEqual("step_pyramid", Words.Parse("take me to Mexico", null, 3).Result.Look.Landmark);
            Assert.AreEqual("wild_west", Words.Parse("take me to New Mexico", null, 3).Result.Look.Of);
            Assert.AreEqual("savanna", Words.Parse("take me to Ghana", null, 3).Result.Look.Of);
            // The place's own plants, sky and people.
            Assert.IsFalse(System.Array.Exists(Words.Parse("take me to Egypt", null, 3).Result.Look.Scatter, k => k == "cactus"), "no saguaro in Egypt");
            Assert.IsTrue(System.Array.Exists(Places.Get("oasis").Look.Scatter, k => k == "cactus"), "the oasis itself keeps its own");
            Assert.AreEqual("none", Words.Parse("take me to Chichen Itza", null, 3).Result.Look.SkyFrom, "a painted sky, not the pine woods");
            var india = Locals.For(Words.Parse("take me to India", null, 3).Result);
            Assert.AreEqual("Meera", india.Name);
            foreach (var said in new[] { india.Name, india.Hello, india.Who, india.Here, india.Tip }) { Assert.IsTrue(Words.IsClean(said), said); Assert.LessOrEqual(said.Length, 70, said); }
            Assert.AreEqual(Act.Edit, Words.Parse(Locals.TipPhrase(india), Words.Parse("take me to India", null, 3).Result, 2).Act, "her tip does something");
            Assert.AreEqual("Posy", Locals.For(Words.Parse("take me to Rome", null, 3).Result).Name);
            // As the headset heard them on Nov 14.
            var indiaNow = Words.Parse("take me to India", null, 3).Result;
            foreach (var (heard, name, mark) in new[]
            {
                ("Like Meta, Egypt.", "Egypt at golden hour", "pyramids"), ("Make me to humala ya.", "Himalaya in the snow", "peak"),
                ("Take me to north of India.", "North of India in the snow", "peak"), ("take me to the himalayas", "The Himalayas in the snow", "peak"),
                ("take me to Mosco", "Moscow in the snow", "onion_domes"), ("take me to Egipt", "Egypt at golden hour", "pyramids")
            })
            {
                var got = Words.Parse(heard, indiaNow, 3);
                Assert.AreEqual(Act.NewWorld, got.Act, heard);
                Assert.IsFalse(got.AskAi, heard + " opens offline");
                Assert.AreEqual(name, got.Result.Look.Name, heard);
                Assert.AreEqual(mark, got.Result.Look.Landmark, heard);
            }
            // A name that is nothing like a place still goes to Claude; a made thing is still a thing.
            foreach (var said in new[] { "take me to Atlantis", "take me to Narnia", "take me to space", "make me a turkey", "I love Paris in spring" })
                Assert.IsTrue(Words.Parse(said, null, 3).Result == null || Words.Parse(said, null, 3).Result.Look == null || Words.Parse(said, null, 3).Result.Look.Landmark.Length == 0, said);
            // "Make it dark" in India: night, and still India with its landmark.
            var dark = Words.Parse("Make it dark.", indiaNow, 3);
            Assert.AreEqual(Act.Edit, dark.Act);
            Assert.IsFalse(dark.AskAi, "understood here, not sent away");
            Assert.AreEqual(22.5f, dark.Result.Hour, 0.6f);
            Assert.AreEqual("dome", dark.Result.Look.Landmark, "the same place, by night");
            Assert.AreEqual(Act.Edit, Words.Parse("too dark", dark.Result, 3).Act);
            Assert.Greater(Words.Parse("too dark", dark.Result, 3).Result.Light, dark.Result.Light, "too dark is brighter, not night");
            // A thunderstorm, however it was heard, is weather where you are.
            foreach (var heard in new[] { "Hande's storm.", "Hand astrom.", "You may a thunderstorm.", "Hindustrom", "a thunder storm" })
            {
                var storm = Words.Parse(heard, indiaNow, 3);
                Assert.AreEqual(Act.Edit, storm.Act, heard);
                Assert.IsFalse(storm.AskAi, heard);
                Assert.AreEqual(Precip.Rain, storm.Result.Precip, heard);
                Assert.AreEqual("dome", storm.Result.Look.Landmark, heard + " keeps the place");
            }
            // Near misses that are not places, and words that only look like "here".
            foreach (var (said, never) in new[] { ("take me to Liberia", "Siberia"), ("I want to see whales", "Wales"), ("show me a turkey", "Turkey"), ("take me to Mali", "Bali") })
            {
                var got = Words.Parse(said, null, 3);
                Assert.IsTrue(got.Result == null || got.Result.Look == null || !got.Result.Look.Name.Contains(never), said + " is not " + never);
            }
            Assert.AreEqual(Act.NewWorld, Words.Parse("turn this place into a beach", indiaNow, 3).Act, "a trip, not a change to here");
            Assert.AreEqual("beach", Words.Parse("turn this place into a beach", indiaNow, 3).Result.Biome);
            var purple = Words.Parse("make it dark purple", indiaNow, 3);
            Assert.IsTrue(purple.Result == null || System.Math.Abs(purple.Result.Hour - 22.5f) > 0.6f, "dark purple is a colour, not night");
            // "Play calm music" is the tune, not a calmer wind.
            foreach (var said in new[] { "Play calm music.", "play a Hindi song", "put on some relaxing music" }) Assert.AreEqual(Act.Music, Words.Parse(said, indiaNow, 3).Act, said);
            Assert.AreEqual(Act.MusicOff, Words.Parse("stop the music", indiaNow, 3).Act);
            // Everyday words from someone new.
            foreach (var said in new[] { "hello", "Hi!", "what can I do", "how do I move", "I'm stuck" }) Assert.AreEqual(Act.Help, Words.Parse(said, indiaNow, 3).Act, said);
            foreach (var said in new[] { "home", "leave", "I want to leave" }) Assert.AreEqual(Act.End, Words.Parse(said, indiaNow, 3).Act, said);
            var rainy = indiaNow.Copy(); rainy.Precip = Precip.Rain;
            Assert.AreEqual(Precip.None, Words.Parse("turn off the rain", rainy, 3).Result.Precip, "turn off the rain stops it");
            Assert.AreEqual(Act.Undo, Words.Parse("go back", indiaNow, 3).Act, "go back is still undo");
            Assert.AreNotEqual(Act.Help, Words.Parse("what is this place", indiaNow, 3).Act, "a question for whoever lives there, not the help card");
            // "Add a river in this desert" in Egypt is about Egypt, not a trip to the desert.
            var egyptNow = Words.Parse("take me to Egypt", null, 3).Result;
            foreach (var heard in new[] { "Had a river in this desert.", "add a river in this desert", "more palms here" })
                Assert.AreNotEqual(Act.NewWorld, Words.Parse(heard, egyptNow, 3).Act, heard);
            Assert.AreEqual(Precip.None, Words.Parse("take me to Paris", null, 3).Result.Precip);
            // The animals and the local of the world it is made from come too.
            var kenya = Words.Parse("take me to Kenya", null, 3).Result;
            Assert.AreEqual("savanna", Wildlife.PlaceOf(kenya));
            Assert.AreEqual("Amara", Locals.For(kenya).Name);
            // Merely mentioned, a place is not a trip; nor is a word that only sounds like one.
            foreach (var said in new[] { "Paris is lovely in spring", "I went to India last year", "make me a turkey", "a china plate", "nice" })
            {
                var h = Words.Parse(said, Recipe.For("meadow", 1), 3);
                Assert.IsTrue(h.Result == null || h.Result.Look == null || h.Result.Look.Landmark.Length == 0 && h.Result.Look.Of.Length == 0, said + " is not a trip");
            }
            // A composed place keeps its own name over the atlas.
            Assert.AreEqual("On Mars", Words.Parse("take me to Mars", null, 3).Result.Look.Name);
            // The look survives being saved and read back.
            var egypt = Words.Parse("take me to Egypt", null, 3).Result;
            var back = Recipe.FromJson(egypt.ToJson());
            Assert.AreEqual("pyramids", back.Look.Landmark);
            Assert.AreEqual("oasis", back.Look.Of);
        }

        [Test]
        public void TakeMeSomewhereIsATrip()
        {
            Assert.IsTrue(Words.IsTravel("Take me to India."));
            Assert.IsTrue(Words.IsTravel("Mato India."), "as the headset heard it");
            Assert.IsTrue(Words.IsTravel("Me to India."), "with the first word cut off");
            Assert.IsTrue(Words.IsTravel("let's go to Paris"));
            Assert.IsTrue(Words.IsTravel("please take us to the pyramids"));
            Assert.IsFalse(Words.IsTravel("make me a lion"));
            Assert.IsFalse(Words.IsTravel("show me a puppy"), "a thing, not a place");
            Assert.IsFalse(Words.IsTravel("show me around"), "a tour");
            Assert.IsFalse(Words.IsTravel("add lanterns"));
            Assert.IsFalse(Words.IsTravel("take me to"), "nowhere named");
            Assert.AreEqual("give me a tomato", Words.FixHeard(Words.Normalise("Q-Mia, tomato")));
            // The name alone, as it was heard; never inside a sentence.
            Assert.IsTrue(WakeWord.Find("Number.", out _));
            Assert.IsTrue(WakeWord.Find("Remember?", out _));
            Assert.IsFalse(WakeWord.Find("Remember the number.", out _));
            Assert.IsFalse(WakeWord.Find("number three", out _));
        }

        [Test]
        public void AskHimReachesWhoeverLivesThere()
        {
            Assert.AreEqual("if he ever found treasure", Locals.AskedTo("Ask him if he ever found treasure"));
            Assert.AreEqual("what she grows here", Locals.AskedTo("please ask her what she grows here?"));
            Assert.AreEqual("i said hello", Locals.AskedTo("tell them I said hello"));
            Assert.AreEqual("", Locals.AskedTo("ask Juniper"));
            Assert.AreEqual("", Locals.AskedTo("tell me about it"));
            Assert.AreEqual("", Locals.AskedTo("make me a lion"));
            foreach (var l in Locals.All)
                if (l.Kind == "person") Assert.Less(l.Seed, Locals.People, l.Id + " is one of the people in the kit");
        }

        [Test]
        public void SomeoneLivesInEveryWorld()
        {
            foreach (var b in Kit.Biomes) Assert.NotNull(Locals.Get(b.Id), "someone lives in " + b.Id);
            foreach (var p in Places.All) Assert.NotNull(Locals.Get(p.Id), "someone lives in " + p.Id);
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var l in Locals.All)
            {
                Assert.IsTrue(names.Add(l.Name), "one " + l.Name);
                Assert.IsTrue(MadeKinds.Has(l.Kind), l.Id + " is a real model");
                foreach (var line in new[] { l.Name, l.Hello, l.Who, l.Here, l.Tip })
                {
                    Assert.IsTrue(Words.IsClean(line), line);
                    Assert.LessOrEqual(line.Length, 70, line);
                }
                var r = Places.Get(l.Id) != null ? Places.Get(l.Id).Recipe(1) : Recipe.For(l.Id, 1);
                Assert.AreSame(l, Locals.For(r), l.Id);
                // Their tip is something you can really say there, and it does something.
                string tip = Locals.TipPhrase(l);
                var h = Words.Parse(tip, r, 2);
                var w = Wishes.Read(tip);
                bool works = (w != null && w.Kind.Length > 0) || h.Act == Act.Music || (h.Act == Act.Edit && h.Changed.Length > 0);
                Assert.IsTrue(works, l.Name + "'s tip \"" + tip + "\" is understood in " + l.Id + " (" + h.Act + ")");
                Assert.AreEqual(l.Who, Locals.Answer(l, "Who are you?"));
                Assert.AreEqual(l.Here, Locals.Answer(l, "what is this place"));
                Assert.AreEqual(l.Tip, Locals.Answer(l, "what can I do here?"));
                Assert.AreEqual("", Locals.Answer(l, "how deep is the water"), "anything else is for Claude");
                Assert.IsTrue(Locals.Named(l, "hi " + l.Name), l.Name);
            }
            var composed = Recipe.For("desert", 1);
            composed.Look = new Look { Name = "A greenhouse on Mars" };
            Assert.AreEqual("Pip", Locals.For(composed)?.Name, "a traveller in places Claude composed");
        }

        [TestCase("make it snow", "campfire", Act.Edit)]
        [TestCase("add flowers", "campfire", Act.Edit)]
        [TestCase("add shooting stars", "campfire", Act.Edit)]
        [TestCase("snow", "campfire", Act.Edit)]
        [TestCase("a snowy mountain", "campfire", Act.NewWorld)]
        [TestCase("the meadow", "campfire", Act.NewWorld)]
        [TestCase("take me somewhere with snow", "campfire", Act.NewWorld)]
        public void AddingAThingIsNotATrip(string said, string from, Act act) => Assert.AreEqual(act, Words.Parse(said, Recipe.For(from, 1), 2).Act, said);

        [TestCase("add shooting stars", "moon")]
        [TestCase("add flowers", "sakura")]
        [TestCase("add lanterns", "pirate_cove")]
        public void AddingInAComposedPlaceKeepsYouThere(string said, string place)
        {
            var r = Places.Get(place).Recipe(1);
            var h = Words.Parse(said, r, 2);
            Assert.AreEqual(Act.Edit, h.Act, said);
            Assert.AreEqual(r.Look.Name, h.Result.Look?.Name, "still in " + place);
        }

        [Test]
        public void AnimalsHaveAHome()
        {
            Assert.AreEqual("savanna", Wildlife.HomeOf("zebra"));
            Assert.AreEqual("prehistoric", Wildlife.HomeOf("t_rex"));
            Assert.AreEqual("farm", Wildlife.HomeOf("cow"));
            Assert.AreEqual("", Wildlife.HomeOf("sofa"));
            Assert.AreEqual("", Wildlife.HomeOf(""));
            foreach (var m in MadeKinds.All)
            {
                string home = Wildlife.HomeOf(m.Kind);
                if (home.Length > 0) Assert.NotNull(Places.Get(home), m.Kind + " lives in a real place");
            }
        }

        [Test]
        public void ShapesForARealThingBecomeTheRealModel()
        {
            var a = ActAnswer.Read("{\"safety\":\"ok\",\"say\":\"A dog.\",\"actions\":[{\"do\":\"make\",\"name\":\"dog\",\"kind\":null,\"parts\":[\"box 0 0.3 0 0.5 0.3 0.3 0 0 0 #aa8855\"],\"place\":\"floor\",\"count\":1,\"size\":0.6}]}", Wishes.Knows);
            Assert.AreEqual(1, a.Actions.Count);
            Assert.AreEqual("dog", a.Actions[0].Kind, "the real dog, not boxes");
        }

        [Test]
        public void AnAnswerIsReadAndChecked()
        {
            var a = ActAnswer.Read("{\"safety\":\"ok\",\"say\":\"Good question.\",\"answer\":\"About 20 km an hour.\",\"actions\":[]}", k => true);
            Assert.AreEqual("About 20 km an hour.", a.Answer);
            Assert.AreEqual("", ActAnswer.Read("{\"safety\":\"ok\",\"say\":\"Hm.\",\"answer\":\"see http://x.y\",\"actions\":[]}", k => true).Answer);
            Assert.AreEqual("", ActAnswer.Read("{\"safety\":\"ok\",\"say\":\"Hm.\",\"answer\":null,\"actions\":[]}", k => true).Answer);
        }

        [Test]
        public void CardsUseProperNamesAndNoRealPeople()
        {
            StringAssert.StartsWith("T. rex\n", LifeSize.Card("t rex", "t_rex", 0.5f));
            StringAssert.StartsWith("Lion\n", LifeSize.Card("lion", "lion", 0.3f));
            StringAssert.DoesNotContain("Roosevelt", LifeSize.Card("teddy", "teddy", 0.3f));
            Assert.AreEqual(12f, LifeSize.InWorld("t_rex"), 0.01f, "in a world a T. rex is its real size");
            Assert.AreEqual(LifeSize.RoomMax, LifeSize.InRoom("t_rex"), 0.01f, "in your room it is capped");
        }

        [TestCase(12f, "sofa", 2f, "As long as 6 of your sofas.")]
        [TestCase(2.5f, "sofa", 2.2f, "About as long as your sofa.")]
        [TestCase(0.3f, "table", 1.2f, "4 would fit along your table.")]
        [TestCase(0.015f, "table", 1.2f, "")]
        [TestCase(2f, "", 1f, "")]
        public void ThingsAreMeasuredAgainstYourFurniture(float life, string piece, float length, string said) => Assert.AreEqual(said, LifeSize.Compare(life, piece, length));

        [Test]
        public void TheCardSaysWhatItIsAndHowBig()
        {
            string c = LifeSize.Card("lion", "lion", 0.3f);
            StringAssert.StartsWith("Lion\n", c);
            StringAssert.Contains("2.5 m in life", c);
            StringAssert.Contains("1:8", c);
            StringAssert.Contains("roar", c);
            StringAssert.Contains("life size", LifeSize.Card("lion", "lion", 2.5f));
            StringAssert.Contains("13\u00d7 life", LifeSize.Card("bee", "bee", 0.2f));
            StringAssert.StartsWith("Rubber duck\n", LifeSize.Card("rubber duck", "", 0.25f));
            Assert.AreEqual(LifeSize.RoomMax, LifeSize.InRoom("brontosaurus"));
        }

        [Test]
        public void AnimalsLiveWhereTheyBelong()
        {
            foreach (var k in Wildlife.AllKinds())
            {
                Assert.IsTrue(MadeKinds.Has(k), k + " is a real model");
                Assert.Greater(LifeSize.Metres(k), 0f, k + " has a life size");
                Assert.IsTrue(LifeSize.IsCreature(k), k + " is an animal");
            }
            foreach (var id in Wildlife.PlaceIds) Assert.NotNull(Places.Get(id), id + " is a place");
            CollectionAssert.Contains(Wildlife.For(Places.Get("savanna").Recipe(1)), "zebra");
            CollectionAssert.Contains(Wildlife.For(Places.Get("prehistoric").Recipe(1)), "triceratops");
            CollectionAssert.Contains(Wildlife.For(Places.Get("farm").Recipe(1)), "cow");
            CollectionAssert.IsEmpty(Wildlife.For(Recipe.For("space", 1)), "nothing lives in orbit");
            CollectionAssert.IsEmpty(Wildlife.For(Places.Get("moon").Recipe(1)), "or on the Moon");
            CollectionAssert.IsEmpty(Wildlife.For(Places.Get("neon_city").Recipe(1)), "or in the city");
            Assert.LessOrEqual(Wildlife.For(Places.Get("savanna").Recipe(1)).Length, Wildlife.MaxAnimals);
        }

        [TestCase("lion", true)]
        [TestCase("person", true)]
        [TestCase("bee", true)]
        [TestCase("sofa", false)]
        [TestCase("pumpkin", false)]
        [TestCase("", false)]
        public void AnimalsAndPeopleAreAlive(string kind, bool alive) => Assert.AreEqual(alive, LifeSize.IsCreature(kind), kind);

        [TestCase("show me around")]
        [TestCase("take me on a tour")]
        public void ShowMeAroundIsStillATour(string said) => Assert.AreEqual(Act.Tour, Words.Parse(said, Recipe.For("campfire", 1), 2).Act, said);

        [TestCase("add lanterns", Extras.Lanterns)]
        [TestCase("add fireflies", Extras.Fireflies)]
        [TestCase("ride fireflies", Extras.Fireflies)]
        public void AddingManyIsTheWorlds(string said, string extra)
        {
            Assert.IsNull(Wishes.Read(said), said + " is not a thing to make");
            var h = Words.Parse(said, Recipe.For("snow", 1), 2);
            Assert.IsTrue(h.Result != null && h.Result.Extras.Contains(extra), said);
        }

        [TestCase("a peaceful beach at sunset", Act.NewWorld)]
        [TestCase("a tour of Mars", Act.NewWorld)]
        [TestCase("take me on a tour", Act.Tour)]
        [TestCase("peace and quiet", Act.Sound)]
        [TestCase("make it peaceful", Act.Sound)]
        [TestCase("mute", Act.Sound)]
        public void SoundAndTourAreWholeCommands(string said, Act act) => Assert.AreEqual(act, Words.Parse(said, Recipe.For("campfire", 1), 2).Act, said);

        [TestCase("Turn off the lights.")]
        [TestCase("lights off")]
        public void LightsOffIsDarker(string said)
        {
            var fire = Recipe.For("campfire", 1);
            fire.Hour = 12f;
            var h = Words.Parse(said, fire, 2);
            Assert.AreEqual(Act.Edit, h.Act, said);
            Assert.AreEqual(Words.Parse("darker", fire, 2).Result.Line(), h.Result.Line(), said);
        }

        [Test]
        public void NoBorrowedWords()
        {
            foreach (var m in MadeKinds.All) CollectionAssert.DoesNotContain(m.Words, "droid", m.Kind);
            Assert.AreNotEqual(Act.Undo, Words.Parse("belay that", Recipe.For("campfire", 1), 2).Act);
        }

        [Test]
        public void TheAskEmberPageFitsFourRows() => Assert.LessOrEqual((Worldeck.HandPanel.SayExamples.Length + 3 + 3) / 4, Worldeck.HandPanel.MaxRows);

        [TestCase("make it life size")]
        [TestCase("life size please")]
        [TestCase("show me its real size")]
        [TestCase("make the lion life-size")]
        public void LifeSizeIsAChange(string said)
        {
            var c = Wishes.Change(said, new[] { "lion" });
            Assert.NotNull(c, said);
            Assert.IsTrue(c.LifeSize, said);
        }
    }
}

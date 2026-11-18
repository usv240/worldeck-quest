using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// Building a place by conversation, the way a guide would: the room asks a short question, you answer, it asks
    /// for what's still missing, then builds it. Four questions (where, when, weather, something special), each
    /// answered by touching a choice or by saying it. Works offline and with hands only. A spoken answer can
    /// carry several at once ("a snowy mountain at night"), and the questions it already answered are skipped.
    /// </summary>
    public sealed class Interview
    {
        public sealed class Choice
        {
            public string Label;
            public Action<Recipe> Apply;
            public Choice(string label, Action<Recipe> apply) { Label = label; Apply = apply; }
        }

        public sealed class Question
        {
            public string Key, Text;
            public List<Choice> Choices = new List<Choice>();
        }

        public Recipe Draft { get; private set; }
        public int Step { get; private set; }
        public bool Done => Step >= order.Count;
        public Question Current => Done ? null : Build(order[Step]);
        readonly List<string> order = new List<string> { "where", "when", "weather", "special" };
        readonly HashSet<string> answered = new HashSet<string>();
        readonly int seed;

        public Interview(int seed)
        {
            this.seed = seed;
            Draft = Recipe.For(Kit.Biomes[0].Id, seed);
        }

        /// <summary>Answers the current question with choice <paramref name="index"/>.</summary>
        public void Choose(int index)
        {
            var q = Current;
            if (q == null || index < 0 || index >= q.Choices.Count) return;
            q.Choices[index].Apply(Draft);
            answered.Add(q.Key);
            Advance();
        }

        /// <summary>
        /// Answers by speech: the words fill in every part they mention (place, time, weather, extras). Returns
        /// false if they said nothing this interview understands (the question stays).
        /// </summary>
        public bool Say(string text)
        {
            var q = Current;
            if (q == null) return false;
            string biome = Words.BestBiome(text, out _);
            bool any = false;
            if (biome != null && !answered.Contains("where"))
            {
                var fresh = Recipe.For(biome, seed);
                Draft = fresh;
                answered.Add("where");
                any = true;
            }
            var before = Draft.Copy();
            var h = Words.Parse(text, Draft, seed);
            if (h.Result != null && (h.Act == Act.Edit || h.Act == Act.NewWorld))
            {
                var r = h.Result;
                if (h.Act == Act.NewWorld && !answered.Contains("where")) { Draft = r; answered.Add("where"); any = true; }
                // A new place (or the same place named again) also says whatever it said about time and weather.
                {
                    // What the words mentioned counts as answered, even when it matches the default (a beach is
                    // already at sunset; "a beach at sunset" still answered "when").
                    string ch = " " + h.Changed + " ";
                    if (ch.Contains(" hour ")) { Draft.Hour = r.Hour; answered.Add("when"); any = true; }
                    if (ch.Contains(" precip ") || ch.Contains(" fog ")) { Draft.Precip = r.Precip; Draft.Fog = r.Fog; Draft.Wind = r.Wind; answered.Add("weather"); any = true; }
                    if (ch.Contains(" extras ")) { Draft.Extras.Clear(); Draft.Extras.AddRange(r.Extras); answered.Add("special"); any = true; }
                    if (ch.Contains(" light ") || ch.Contains(" warmth ") || ch.Contains(" density ") || ch.Contains(" wind "))
                    { Draft.Light = r.Light; Draft.Warmth = r.Warmth; Draft.Density = r.Density; Draft.Wind = r.Wind; any = true; }
                }
            }
            string t = Words.Normalise(text);
            if (!any && (t == "no" || t.Contains("nothing") || t.Contains("none") || t.Contains("skip") || t.Contains("that's it") || t.Contains("thats it")))
            {
                answered.Add(q.Key);
                any = true;
            }
            Draft.Clamp();
            if (any) Advance();
            return any;
        }

        void Advance()
        {
            while (Step < order.Count && answered.Contains(order[Step])) Step++;
            if (Step < order.Count && !Applies(order[Step])) { answered.Add(order[Step]); Advance(); }
        }

        bool Applies(string key)
        {
            if (key == "when") return Draft.Def.HasTime;
            if (key == "weather") return Draft.Biome != "space" && Draft.Biome != "reef";
            return true;
        }

        /// <summary>The finished place (with a title that says what was asked for).</summary>
        public Recipe Finish()
        {
            Draft.Title = Draft.Describe(false);
            if (Draft.Title.Length > 0) Draft.Title = char.ToUpperInvariant(Draft.Title[0]) + Draft.Title.Substring(1);
            return Draft.Clamp().Copy();
        }

        Question Build(string key)
        {
            var q = new Question { Key = key };
            switch (key)
            {
                case "where":
                    q.Text = "Where is it?";
                    foreach (var b in Kit.Biomes)
                    {
                        var id = b.Id;
                        q.Choices.Add(new Choice(PlaceWord(id), r => Replace(r, Recipe.For(id, seed))));
                    }
                    break;
                case "when":
                    q.Text = "What time is it there?";
                    q.Choices.Add(new Choice("Dawn", r => r.Hour = 6.5f));
                    q.Choices.Add(new Choice("Midday", r => r.Hour = 12.5f));
                    q.Choices.Add(new Choice("Sunset", r => r.Hour = 18.5f));
                    q.Choices.Add(new Choice("Night", r => r.Hour = 22.5f));
                    break;
                case "weather":
                    q.Text = "And the weather?";
                    q.Choices.Add(new Choice("Clear", r => { r.Precip = Precip.None; r.Fog = 0f; }));
                    q.Choices.Add(new Choice("Rain", r => { r.Precip = Precip.Rain; }));
                    q.Choices.Add(new Choice("Snow", r => { r.Precip = Precip.Snow; }));
                    q.Choices.Add(new Choice("Fog", r => { r.Fog = 0.75f; }));
                    break;
                case "special":
                    q.Text = "Anything else?";
                    if (Draft.Biome == "reef") q.Choices.Add(new Choice("Lanterns", r => Add(r, Extras.Lanterns)));
                    else if (Draft.Biome == "space") q.Choices.Add(new Choice("Shooting stars", r => Add(r, Extras.ShootingStars)));
                    else
                    {
                        q.Choices.Add(new Choice("Northern lights", r => { Add(r, Extras.Aurora); if (!r.IsNight && r.Def.HasTime) r.Hour = 22.5f; }));
                        q.Choices.Add(new Choice("Fireflies", r => Add(r, Extras.Fireflies)));
                        q.Choices.Add(new Choice("Lanterns", r => Add(r, Extras.Lanterns)));
                    }
                    q.Choices.Add(new Choice("That's it", r => { }));
                    break;
            }
            return q;
        }

        static void Add(Recipe r, string extra) { if (!r.Extras.Contains(extra)) r.Extras.Add(extra); }

        void Replace(Recipe r, Recipe with)
        {
            r.Biome = with.Biome; r.Hour = with.Hour; r.Precip = with.Precip; r.Fog = with.Fog; r.Wind = with.Wind;
            r.Density = with.Density; r.Warmth = with.Warmth; r.Light = with.Light; r.Title = with.Title;
            r.Extras.Clear(); r.Extras.AddRange(with.Extras);
        }

        static string PlaceWord(string biome) => biome switch
        {
            "campfire" => "Forest", "beach" => "Beach", "desert" => "Desert", "snow" => "Mountains",
            "space" => "Space", "reef" => "Under the sea", "city" => "City at night", "meadow" => "Meadow", _ => biome
        };
    }
}

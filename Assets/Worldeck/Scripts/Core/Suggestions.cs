using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// What to offer while someone types: the top few things they could mean, from everything the app can do offline
    /// (places, worlds, changes, things to make, controls). Tapping one is the same as saying it.
    /// </summary>
    public static class Suggestions
    {
        static List<string> all;

        /// <summary>Every phrase the app can offer, in a sensible order (places and worlds first).</summary>
        public static IReadOnlyList<string> All
        {
            get
            {
                if (all != null) return all;
                var l = new List<string>();
                foreach (var p in Places.All) l.Add(Article(p.Look.Name));
                foreach (var b in Kit.Biomes) l.Add(char.ToLowerInvariant(b.Name[0]) + b.Name.Substring(1));
                l.AddRange(new[]
                {
                    "make it rain", "make it snow", "darker", "brighter", "at sunset", "at night", "at dawn", "foggy", "clear skies",
                    "windy", "calm", "warmer", "colder", "add northern lights", "add fireflies", "add shooting stars", "add lanterns",
                    "add flowers", "add crystals", "add clouds", "add embers", "fuller", "emptier", "a storm",
                    "make me a lantern", "make me a tent", "make me a pumpkin", "make me a snowman",
                    "make me a teddy bear", "make me a candy cane", "make me a present", "make me a rover", "make me a potted plant",
                    "make me a beach umbrella", "make me a canoe", "make me a bookshelf", "make me a floor lamp", "make me three shells",
                    "make me a jukebox", "make me a sandcastle", "make me a cherry tree",
                    "bigger", "smaller", "make it blue", "remove it",
                    "play some music", "stop the music", "freeze", "unfreeze", "undo", "surprise me", "today's world",
                    "start the storm", "rewind", "take me home", "help"
                });
                return all = l;
            }
        }

        static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string lower = char.ToLowerInvariant(name[0]) + name.Substring(1);
            if (lower.StartsWith("on ", StringComparison.Ordinal)) return lower;   // "on Mars"
            return ("aeiou".IndexOf(lower[0]) >= 0 ? "an " : "a ") + lower;
        }

        /// <summary>
        /// Up to <paramref name="max"/> phrases for what has been typed so far: ones that start with it first, then ones
        /// with every typed word starting a word in them, then ones that contain it. Nothing typed: a place, a change,
        /// a thing and a surprise.
        /// </summary>
        public static List<string> For(string typed, int max = 4)
        {
            var result = new List<string>();
            string t = Words.Normalise(typed ?? "");
            if (t.Length == 0)
            {
                result.AddRange(new[] { "a volcano at nightfall", "make it rain", "make me a lantern", "surprise me" });
                return result.GetRange(0, Math.Min(max, result.Count));
            }
            var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(int score, int order, string phrase)>();
            for (int i = 0; i < All.Count; i++)
            {
                string p = All[i];
                string n = Words.Normalise(p);
                int s;
                if (n.StartsWith(t, StringComparison.Ordinal)) s = 0;
                else if (WordsStart(n, words)) s = 1;
                else if (n.Contains(t)) s = 2;
                else continue;
                scored.Add((s, i, p));
            }
            scored.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.phrase.Length != b.phrase.Length ? a.phrase.Length.CompareTo(b.phrase.Length) : a.order.CompareTo(b.order));
            foreach (var x in scored) { if (result.Count >= max) break; if (!result.Contains(x.phrase)) result.Add(x.phrase); }
            // Whatever they typed can always be sent as it is.
            if (result.Count < max && !result.Contains(t) && t.Length > 2) result.Add(t);
            return result;
        }

        static bool WordsStart(string phrase, string[] typed)
        {
            var have = phrase.Split(' ');
            foreach (var w in typed)
            {
                bool found = false;
                foreach (var h in have) if (h.StartsWith(w, StringComparison.Ordinal)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
    }
}

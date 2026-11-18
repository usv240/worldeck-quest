using System;
using System.Collections.Generic;
using System.Text;

namespace Worldeck.Core
{
    /// <summary>
    /// "Juniper" in what the on-device recogniser heard. Everything said is transcribed on the headset; a phrase
    /// is for us only if it calls Juniper by name ("Juniper, make it rain", "hey Juniper… darker"). Whole words only.
    /// The name was chosen for the recogniser: in a test of 14 names against the headset's own speech model (11
    /// voices, 3 speeds, with and without room noise), "Juniper" was heard right 132 times out of 132, where "Ember"
    /// was heard as "number", "members" or "M. B." on the headset.
    /// </summary>
    public static class WakeWord
    {
        public const string Name = "juniper";
        public const string Display = "Juniper";

        static readonly HashSet<string> Names = new HashSet<string> { "juniper", "junipers", "juniper's" };
        static readonly HashSet<string> Greetings = new HashSet<string> { "hey", "hi", "hello", "ok", "okay", "oh", "yo", "um", "uh", "so", "and" };

        /// <summary>Lower case, letters, digits, apostrophes and single spaces only.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool space = true;
            foreach (char c0 in text)
            {
                char c = char.ToLowerInvariant(c0);
                if (char.IsLetterOrDigit(c) || c == '\'') { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// True if <paramref name="text"/> calls Juniper. <paramref name="command"/> is what follows the name
        /// ("" if the name was said alone, so the next phrase is the command). The name must be among the first
        /// four words, so a sentence that merely mentions "juniper" half-way through doesn't count.
        /// </summary>
        public static bool Find(string text, out string command)
        {
            command = "";
            if (SoundsLikeName(text)) return true;   // the name alone, split or slightly misheard ("June per")
            var words = Normalize(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length && i < 4; i++)
            {
                if (!IsName(words[i])) { if (!Greetings.Contains(words[i])) return false; continue; }
                // "Juniper Juniper…" (the name said again, or cut off: "jun"): the name, not part of the command.
                int k = i + 1;
                while (k < words.Length && (IsName(words[k]) || Greetings.Contains(words[k]) || (k == words.Length - 1 && Name.StartsWith(words[k], StringComparison.Ordinal) && words[k].Length <= 3))) k++;
                command = string.Join(" ", words, k, words.Length - k);
                // "Juniper, please…" / "Juniper can you…": politeness isn't part of the command.
                command = Strip(command, "please ", "can you ", "could you ", "would you ", "i want ", "i'd like ", "let's ");
                return true;
            }
            return false;
        }

        /// <summary>
        /// A short phrase that is only the name, run together or split ("June per", "Juni per"). Only for a phrase of
        /// one or two words, so a real sentence never wakes Juniper by accident.
        /// </summary>
        public static bool SoundsLikeName(string text)
        {
            var n = Normalize(text);
            if (n.Length == 0) return false;
            var words = n.Split(' ');
            if (words.Length > 2) return false;
            string c = n.Replace(" ", "").Replace("'", "");
            // Said alone, the name was also heard as "Number." and "Remember?" (Nov 13, on the headset). Alone only:
            // it starts listening and does nothing else, and in a sentence those words are just words.
            if (words.Length == 1 && (c == "number" || c == "remember")) return true;
            return c == Name || c == "junipers" || (c.Length >= 6 && c.Length <= 8 && OneAway(c, Name));
        }

        /// <summary>Words one letter from "juniper" that are words of their own, never the name.</summary>
        static readonly HashSet<string> NotNames = new HashSet<string> { "jupiter" };

        /// <summary>"Juniper", or what the recogniser makes of it: one letter away ("junipor", "jeniper").</summary>
        public static bool IsName(string w)
        {
            if (Names.Contains(w)) return true;
            if (w.Length < 6 || w.Length > 8 || NotNames.Contains(w)) return false;
            return OneAway(w, Name);
        }

        static bool OneAway(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++; else if (a.Length < b.Length) j++; else { i++; j++; }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }

        static string Strip(string s, params string[] prefixes)
        {
            bool again = true;
            while (again)
            {
                again = false;
                foreach (var p in prefixes)
                    if (s.StartsWith(p, StringComparison.Ordinal)) { s = s.Substring(p.Length); again = true; }
            }
            return s.Trim();
        }

        /// <summary>
        /// Worth acting on as a command (heard while Juniper is listening): at least one real word, not just a
        /// filler the recogniser makes of room noise ("uh", "the", "mm").
        /// </summary>
        public static bool IsCommand(string text)
        {
            var n = Normalize(text);
            if (n.Length < 2) return false;
            foreach (var w in n.Split(' '))
                if (w.Length >= 3 && !Fillers.Contains(w)) return true;
            return false;
        }

        static readonly HashSet<string> Fillers = new HashSet<string> { "the", "and", "uhh", "umm", "hmm", "mmm", "huh", "you", "yeah", "okay", "thank", "thanks", "bye" };
    }
}

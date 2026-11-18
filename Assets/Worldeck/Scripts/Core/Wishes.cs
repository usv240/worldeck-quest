using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Worldeck.Core
{
    /// <summary>A sentence that asks for a thing: "make me a lantern", "put three shells on the table".</summary>
    public sealed class Wish
    {
        /// <summary>What was asked for, in the player's words ("rubber duck").</summary>
        public string Object = "";
        /// <summary>The library object it names exactly ("lantern"), or "" when only Juniper's helper could build it.</summary>
        public string Kind = "";
        public int Count = 1;
        public string Place = "front";
        /// <summary>Named on its own ("puppy", "a lion"), with no "make me": a wish only when nothing else is meant.</summary>
        public bool Bare;
    }

    /// <summary>
    /// "Say anything", the part that needs no network: spotting that a sentence asks for a thing, naming the
    /// library object it means, and understanding "bigger", "make it blue" and "remove it" about things already made.
    /// </summary>
    public static class Wishes
    {
        /// <summary>Everything the app can make by itself.</summary>
        public static readonly HashSet<string> Library = new HashSet<string>
        {
            "pine", "snow_pine", "oak", "palm", "bush", "fern", "cactus", "dune_grass", "kelp", "coral", "flower", "flower_patch",
            "rock", "snow_rock", "reef_rock", "shell_rock", "stone", "ice", "crystal_cluster", "log_small", "kindling",
            "lantern", "street_lamp", "light_pylon", "antenna", "panel", "tank", "hydrant", "building", "cloud",
            "shell", "pearl", "snowball", "seed", "sun_shard", "energy_cell", "record",
            "fire", "sandcastle", "sun_crystal", "snowman", "star_map", "anemone_glow", "jukebox", "blossom_tree",
            // Artist-made (Kenney, CC0): only made when the model kit is in the app.
            "mushroom", "stump", "tent", "log_stack", "lantern_hanging", "rover", "crate_space", "console", "bar_stool",
            "lounge_chair", "speaker", "potted_plant", "pumpkin", "present", "candy_cane", "teddy", "radio", "parasol", "canoe",
            "fence", "barrels", "bookcase", "floor_lamp", "cone", "crystal_rock", "fall_tree", "generator"
        };

        public static bool Knows(string kind) => !string.IsNullOrEmpty(kind) && Library.Contains(kind);

        // Real models (MadeKinds, generated from Tools/kit/made.py) join the library, named by their words.
        static Wishes()
        {
            foreach (var m in MadeKinds.All)
            {
                Library.Add(m.Kind);
                foreach (var w in m.Words) if (!Alias.ContainsKey(w)) Alias[w] = m.Kind;
            }
        }

        static readonly HashSet<string> Tall = new HashSet<string> { "pine", "snow_pine", "oak", "palm", "blossom_tree", "street_lamp", "building", "light_pylon", "antenna", "kelp" };
        static readonly HashSet<string> Small = new HashSet<string> { "pearl", "seed", "shell", "snowball", "sun_shard", "energy_cell", "stone", "flower", "record", "kindling" };

        /// <summary>How big (metres, largest side) a library thing is made when nobody says.</summary>
        public static float SizeOf(string kind) => MadeKinds.Has(kind) ? MadeKinds.SizeOf(kind) : Tall.Contains(kind) ? 0.6f : Small.Contains(kind) ? 0.1f : kind == "cloud" ? 0.5f : 0.25f;

        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        {
            { "tree", "oak" }, { "oak tree", "oak" }, { "pine tree", "pine" }, { "fir tree", "pine" }, { "christmas tree", "snow_pine" },
            { "snowy pine", "snow_pine" }, { "palm tree", "palm" }, { "shrub", "bush" }, { "seaweed", "kelp" }, { "flowers", "flower_patch" },
            { "boulder", "rock" }, { "pebble", "stone" }, { "ice shard", "ice" }, { "icicle", "ice" }, { "crystal", "crystal_cluster" },
            { "crystals", "crystal_cluster" }, { "gem", "crystal_cluster" }, { "log", "log_small" }, { "sticks", "kindling" },
            { "lamp", "lantern" }, { "street light", "street_lamp" }, { "streetlight", "street_lamp" }, { "lamp post", "street_lamp" },
            { "solar panel", "panel" }, { "fire hydrant", "hydrant" }, { "house", "building" }, { "tower", "building" },
            { "seashell", "shell" }, { "sea shell", "shell" }, { "snow ball", "snowball" }, { "battery", "energy_cell" },
            { "vinyl", "record" }, { "sand castle", "sandcastle" }, { "castle", "sandcastle" }, { "snow man", "snowman" },
            { "star map", "star_map" }, { "music box", "jukebox" }, { "cherry tree", "blossom_tree" }, { "blossom tree", "blossom_tree" },
            { "grass", "dune_grass" },
            { "toadstool", "mushroom" }, { "tree stump", "stump" }, { "camping tent", "tent" }, { "log pile", "log_stack" },
            { "pile of logs", "log_stack" }, { "firewood", "log_stack" }, { "hanging lantern", "lantern_hanging" },
            { "moon buggy", "rover" }, { "mars rover", "rover" }, { "space buggy", "rover" }, { "crate", "crate_space" },
            { "cargo crate", "crate_space" }, { "container", "crate_space" }, { "computer", "console" }, { "control panel", "console" },
            { "terminal", "console" }, { "bar stool", "bar_stool" }, { "stool", "bar_stool" }, { "chair", "lounge_chair" },
            { "armchair", "lounge_chair" }, { "lounge chair", "lounge_chair" }, { "loudspeaker", "speaker" },
            { "gift", "present" }, { "candy cane", "candy_cane" }, { "teddy bear", "teddy" }, { "bear", "teddy" }, { "umbrella", "parasol" },
            { "beach umbrella", "parasol" }, { "boat", "canoe" }, { "kayak", "canoe" }, { "barrel", "barrels" }, { "bookshelf", "bookcase" },
            { "book shelf", "bookcase" }, { "floor lamp", "floor_lamp" }, { "traffic cone", "cone" }, { "autumn tree", "fall_tree" },
            { "potted plant", "potted_plant" }, { "houseplant", "potted_plant" }, { "house plant", "potted_plant" }, { "plant", "potted_plant" }
        };

        static readonly Regex Ask = new Regex(
            @"^(?:please |hey |okay |ok )?(?:can you |could you |would you |can i |could i |may i |i would like you to |i'd like you to )?" +
            @"(?:i want|i need|i would like|i'd like|id like|make|create|build|generate|produce|render|materialize|materialise|craft|give|get|bring|spawn|conjure|summon|show|put|place|add|drop|have)" +
            @"(?: me| us)?(?: up)? (a few|a couple of|a|an|some|another|one|two|three|four|five|six|[1-6])\b ?(.*)$", RegexOptions.Compiled);

        static readonly (string phrase, string place)[] PlaceWords =
        {
            ("on the table", "table"), ("on my table", "table"), ("on the desk", "table"), ("on my desk", "table"),
            ("on the floor", "floor"), ("on the ground", "floor"), ("on the sofa", "sofa"), ("on the couch", "sofa"), ("on my sofa", "sofa"),
            ("on my couch", "sofa"), ("on the shelf", "shelf"), ("on my shelf", "shelf"), ("on the bed", "bed"), ("on my bed", "bed"),
            ("over there", "there"), ("right there", "there"), ("where i'm pointing", "there"), ("where i am pointing", "there"),
            ("to my left", "left"), ("on my left", "left"), ("on the left", "left"), ("to my right", "right"), ("on my right", "right"),
            ("on the right", "right"), ("in front of me", "front"), ("in my hand", "front"), ("in my hands", "front"), ("right here", "front"),
            ("for me", "front"), ("please", "front")
        };

        /// <summary>The wish in a sentence, or null when it isn't one ("make it rain" and "a beach" are not).</summary>
        public static Wish Read(string text)
        {
            string t = Words.FixHeard(Words.Normalise(text));
            var m = Ask.Match(t);
            if (!m.Success) return BareThing(t);
            var w = new Wish();
            string n = m.Groups[1].Value;
            w.Count = n == "two" || n == "a couple of" ? 2 : n == "three" || n == "some" || n == "a few" ? 3 : n == "four" ? 4 : n == "five" ? 5 : n == "six" ? 6
                    : n.Length == 1 && char.IsDigit(n[0]) ? n[0] - '0' : 1;
            string rest = " " + m.Groups[2].Value + " ";
            foreach (var (phrase, place) in PlaceWords)
            {
                int at = rest.IndexOf(" " + phrase + " ", StringComparison.Ordinal);
                if (at < 0) continue;
                if (phrase != "please" && phrase != "for me") w.Place = place;
                rest = rest.Remove(at, phrase.Length + 1);
            }
            rest = rest.Trim();
            if (rest.StartsWith("of ", StringComparison.Ordinal)) rest = rest.Substring(3);
            if (rest.Length == 0 || rest.Length > 60) return null;
            w.Object = rest;
            w.Kind = KindOf(rest);
            // "A big dog", "a little baby fox": the real model, whatever its size was called.
            if (w.Kind.Length == 0)
            {
                string plain = Regex.Replace(rest, @"^(?:(?:big|large|huge|giant|small|little|tiny|baby|cute|nice) )+", "");
                if (plain != rest && MadeKinds.Has(KindOf(plain))) w.Kind = KindOf(plain);
            }
            // Heard run together ("a horse, lord, fox" for "a horse... look, a fox"): the first thing named is the wish.
            if (w.Kind.Length == 0)
            {
                var parts = rest.Split(' ');
                if (parts.Length >= 3)
                {
                    string first = KindOf(parts[0]);
                    if (MadeKinds.Has(first)) { w.Kind = first; rest = parts[0]; w.Object = rest; }
                }
            }
            if (w.Kind.Length > 0 && n == "some" && (w.Kind == "flower_patch" || w.Kind == "kindling" || w.Kind == "dune_grass")) w.Count = 1;
            // A place we know ("a beach", "some rain") is a world, not a thing.
            if (w.Kind.Length == 0 && Words.UnknownWords(rest) == 0) return null;
            return w;
        }

        static readonly Regex Bare = new Regex(@"^(?:please )?(?:(a|an|one|two|three|four|five|six|[1-6]) )?([a-z][a-z ]{1,30}?)(?: please)?$", RegexOptions.Compiled);

        /// <summary>
        /// "Puppy", "a lion", "two penguins": a real model named on its own (typed, or said to Juniper) is a wish for it.
        /// Only real models: "lanterns" or "rain" on their own are the world's words.
        /// </summary>
        static Wish BareThing(string t)
        {
            var b = Bare.Match(t);
            if (!b.Success) return null;
            string rest = b.Groups[2].Value.Trim();
            if (rest.Split(' ').Length > 3) return null;
            string kind = KindOf(rest);
            if (!MadeKinds.Has(kind)) return null;
            string n = b.Groups[1].Value;
            int count = n == "two" ? 2 : n == "three" ? 3 : n == "four" ? 4 : n == "five" ? 5 : n == "six" ? 6 : n.Length == 1 && char.IsDigit(n[0]) ? n[0] - '0' : 1;
            return new Wish { Object = rest, Kind = kind, Count = count, Bare = true };
        }

        /// <summary>The library object these words name exactly ("lanterns" → lantern), or "".</summary>
        public static string KindOf(string words)
        {
            string s = (words ?? "").Trim();
            if (s.Length == 0) return "";
            foreach (string c in new[] { s, s.EndsWith("es", StringComparison.Ordinal) ? s.Substring(0, s.Length - 2) : s, s.EndsWith("s", StringComparison.Ordinal) ? s.Substring(0, s.Length - 1) : s })
            {
                if (Alias.TryGetValue(c, out string k)) return k;
                string id = c.Replace(' ', '_');
                if (Library.Contains(id)) return id;
            }
            return "";
        }

        static readonly Dictionary<string, string> Colours = new Dictionary<string, string>
        {
            { "red", "#d63a2f" }, { "blue", "#2f6fd6" }, { "green", "#3aa655" }, { "yellow", "#f2c832" }, { "orange", "#ef8a2b" },
            { "purple", "#8a4fd0" }, { "pink", "#f08ab8" }, { "white", "#f4f4f0" }, { "black", "#26262a" }, { "gold", "#e0b040" },
            { "golden", "#e0b040" }, { "brown", "#8a5a36" }, { "silver", "#c0c6cc" }, { "teal", "#2fa6a0" }
        };

        static readonly Regex Paint = new Regex(@"^(?:please )?(?:make|turn|paint|color|colour) (?:it|that|this|them|the .+?|my .+?) ([a-z]+)(?: please)?$", RegexOptions.Compiled);
        static readonly Regex Gone = new Regex(@"^(?:please )?(?:can you |could you )?(?:remove|delete|erase|destroy|get rid of|take away|lose|clear away|clear|throw away) (.+?)(?: please)?$", RegexOptions.Compiled);
        static readonly HashSet<string> GoneAll = new HashSet<string> { "everything", "all of it", "it all", "them", "all of them", "them all", "those", "these", "all", "all that", "the lot", "all of these", "all of those" };
        static readonly HashSet<string> GoneOne = new HashSet<string> { "it", "that", "this", "that one", "this one", "the last one", "the last thing" };

        /// <summary>
        /// The made thing the words are about, by its name or the name's last word, one or many ("the pumpkin" and
        /// "pumpkins" both name "pumpkins"), or "". <paramref name="plural"/>: it was said as more than one.
        /// </summary>
        static string Named(string t, IList<string> names, out bool plural)
        {
            plural = false;
            string s = " " + t + " ";
            string best = "";
            int bestLen = 0;
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                string last = n.Contains(" ") ? n.Substring(n.LastIndexOf(' ') + 1) : "";
                foreach (var root in last.Length >= 3 ? new[] { n, last } : new[] { n })
                {
                    string one = Singular(root);
                    string ies = one.EndsWith("y", StringComparison.Ordinal) ? one.Substring(0, one.Length - 1) + "ies" : one;
                    foreach (var form in new[] { one, one + "s", one + "es", ies, root })
                    {
                        if (form.Length <= bestLen || s.IndexOf(" " + form + " ", StringComparison.Ordinal) < 0) continue;
                        best = n;
                        bestLen = form.Length;
                        plural = form != one;
                    }
                }
            }
            return best;
        }

        static string Singular(string w)
        {
            if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w.Substring(0, w.Length - 3) + "y";
            if (w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal) || w.EndsWith("sses", StringComparison.Ordinal))
                return w.Substring(0, w.Length - 2);
            if (w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal)) return w.Substring(0, w.Length - 1);
            return w;
        }

        /// <summary>
        /// "Bigger", "make it blue", "remove the duck": a change to something already made, or null. Only asked when
        /// there are things; <paramref name="names"/> are their names.
        /// </summary>
        public static ActItem Change(string text, IList<string> names)
        {
            if (names == null || names.Count == 0) return null;
            string t = Words.FixHeard(Words.Normalise(text));
            if (t.Length == 0 || t.Length > 80) return null;
            var it = new ActItem { Do = "change", Target = "last" };
            string named = Named(t, names, out bool plural);
            if (named.Length > 0) it.Target = named;

            var g = Gone.Match(t);
            if (g.Success || t == "make it disappear" || t == "make it go away" || t == "make them disappear" || t == "make them go away")
            {
                string what = g.Success ? g.Groups[1].Value : t.Contains(" them ") ? "them" : "it";
                if (GoneAll.Contains(what)) { it.Target = "all"; it.Remove = true; return it; }
                if (GoneOne.Contains(what)) { it.Remove = true; return it; }
                // "the pumpkins", "all the pumpkins", "this pumpkin", "pumpkins": only about what was made here
                // ("remove the fog" is the world's).
                if (named.Length == 0) return null;
                it.Remove = true;
                it.Every = plural || System.Text.RegularExpressions.Regex.IsMatch(what, @"^(?:all|every|each|both|these|those) ");
                return it;
            }
            if (Regex.IsMatch(t, @"^(?:please )?(?:make (?:it|that|this|them|the \w+) )?(?:spin|rotate|revolve|turn around|turn round|keep turning)(?: (?:it|that|this|them|around|round|the \w+))?(?: please)?$"))
            { it.Spin = 1; return it; }
            if (Regex.IsMatch(t, @"^(?:please )?stop (?:spinning|turning|rotating|it spinning|the spinning)(?: please)?$")) { it.Spin = -1; return it; }
            if (Regex.IsMatch(t, @"\b(?:life size|life sized|lifesize|actual size|real size|full size|true size|as big as (?:it is |they are )?in (?:real )?life|how big (?:it is|they are) really)\b"))
            { it.LifeSize = true; return it; }
            var p = Paint.Match(t);
            if (p.Success && Colours.TryGetValue(p.Groups[1].Value, out string hex)) { it.Color = hex; return it; }
            if (Colours.TryGetValue(t, out hex)) { it.Color = hex; return it; }

            bool about = it.Target != "last" || Regex.IsMatch(t, @"^(?:please )?(?:make (?:it|that|this|them) )?(?:a (?:little|bit|lot) |much |way |even )?(?:bigger|larger|huge|giant|smaller|tinier|tiny|little)(?: please)?$")
                         || Regex.IsMatch(t, @"^(?:shrink|grow|enlarge) (?:it|that|this|them)$");
            if (!about) return null;
            bool lot = t.Contains("much ") || t.Contains("a lot") || t.Contains("way ") || t.Contains("huge") || t.Contains("giant") || t.Contains("tiny");
            if (Regex.IsMatch(t, @"\b(bigger|larger|huge|giant|grow|enlarge)\b")) { it.Scale = lot ? 2.2f : 1.5f; return it; }
            if (Regex.IsMatch(t, @"\b(smaller|tinier|tiny|little|shrink)\b") && !t.Contains("a little bigger")) { it.Scale = lot ? 0.45f : 0.66f; return it; }
            return null;
        }
    }
}

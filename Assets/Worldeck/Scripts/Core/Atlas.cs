using System;
using System.Collections.Generic;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>
    /// "Take me to India." Real places (countries, cities, famous sights), each made from a world the app already
    /// has plus the one thing on the horizon that makes it that place (<see cref="Look.Landmark"/>: the pyramids,
    /// an iron tower, a white dome). They open at once and offline; a place not in here is composed by Claude.
    /// A place is an impression in the app's own style, never a model of the real one.
    /// </summary>
    public static class Atlas
    {
        public sealed class Spot
        {
            public string[] Words;
            /// <summary>What follows the name in the title: "India" + " at sunrise".</summary>
            public string After = "";
            /// <summary>The composed place (Places) or kit world it is made from.</summary>
            public string From = "";
            public string Landmark = "";
            public float Hour = -1f;
            /// <summary>Weather to force (Precip.None clears the base place's rain), or null to keep it.</summary>
            public string Precip;
            /// <summary>Fog to force (0 clear … 1), or -1 to keep the base place's.</summary>
            public float Fog = -1f;
        }

        static Spot A(string words, string after, string from, string landmark = "", float hour = -1f, string precip = null, float fog = -1f) =>
            new Spot { Words = words.Split('|'), After = after, From = from, Landmark = landmark, Hour = hour, Precip = precip, Fog = fog };

        public static readonly Spot[] All =
        {
            // ---- South Asia ----
            A("india|taj mahal|agra|delhi|new delhi|jaipur|varanasi|lucknow", "in the morning", "meadow", "dome", 8.5f, null, 0.06f),
            A("mumbai|bombay|bangalore|bengaluru|hyderabad|chennai|kolkata|calcutta|pune|ahmedabad", "at night", "city", "spire", 21f),
            A("rajasthan|udaipur|jodhpur|jaisalmer", "at golden hour", "oasis", "dome", 17.5f),
            A("goa|maldives|sri lanka beach|andaman", "by the sea", "beach", "", 16f),
            A("kerala|sri lanka|bangladesh|assam", "in the rain", "jungle"),
            A("kashmir|ladakh|pakistan|karakoram", "at dawn", "mountain_lake", "peak"),
            A("nepal|himalayas|the himalayas|himalaya|the himalaya|everest|mount everest|tibet|bhutan|kathmandu|north india|north of india|northern india|" +
              "manali|shimla|himachal|uttarakhand|rishikesh|darjeeling|sikkim", "in the snow", "snow", "peak", 9f),
            // ---- East and South-East Asia ----
            A("japan|kyoto|nara|osaka", "in blossom", "sakura", "pagoda"),
            A("mount fuji|fuji|fujiyama", "in blossom", "sakura", "peak"),
            A("tokyo|seoul|korea|south korea|hong kong|shanghai|singapore|taipei|bangkok city", "in the rain", "neon_city", "spire"),
            A("china|beijing|forbidden city|xian", "in the morning mist", "meadow", "pagoda", 7f),
            A("great wall|the great wall|great wall of china|the great wall of china", "in the morning", "meadow", "wall", 8f),
            A("thailand|bangkok|cambodia|angkor wat|angkor|vietnam|laos|myanmar|burma|borneo|indonesia", "in the jungle", "temple"),
            A("bali|phuket|philippines|fiji|tahiti|bora bora|hawaii|honolulu", "at sunset", "beach", "", 18.5f),
            A("mongolia|gobi|the gobi", "at dusk", "desert", "", 18f),
            // ---- Middle East and Africa ----
            A("egypt|cairo|giza|the pyramids|pyramids|pyramid|sphinx|the nile|luxor", "at golden hour", "oasis", "pyramids", 17f),
            A("dubai|abu dhabi|qatar|doha|riyadh|saudi arabia|kuwait", "at dusk", "oasis", "spire", 18.5f),
            A("istanbul|turkey|hagia sophia|iran|persia|isfahan|samarkand|uzbekistan", "at dusk", "oasis", "mosque", 18.5f),
            A("jerusalem|israel|bethlehem|nazareth", "at golden hour", "oasis", "", 17.5f),
            A("sahara|the sahara|morocco|marrakech|arabia|arabian desert|jordan|petra|algeria|libya|tunisia", "at noon", "desert", "", 13f),
            A("africa|kenya|tanzania|serengeti|the serengeti|safari|south africa|botswana|namibia|uganda|masai mara|nigeria|ghana", "at golden hour", "savanna"),
            A("kilimanjaro|mount kilimanjaro", "at golden hour", "savanna", "peak"),
            A("madagascar|congo|the congo", "in the rain", "jungle"),
            // ---- Europe ----
            A("paris|france|eiffel tower|the eiffel tower", "at dusk", "cafe", "tower", 18.5f, Core.Precip.None, 0.05f),
            A("london|england|britain|great britain|uk|united kingdom|big ben", "in the rain", "cafe", "clock_tower", -1f, null, 0.15f),
            A("scotland|edinburgh|the highlands|scottish highlands|germany|bavaria|neuschwanstein|transylvania|romania|prague|czech republic", "at dusk", "castle"),
            A("stonehenge|salisbury", "in the morning", "meadow", "stone_circle", 9f),
            A("ireland|dublin|galway|wales|cardiff", "in the morning", "castle", "", 9f),
            A("rome|italy|colosseum|the colosseum", "at golden hour", "meadow", "colosseum", 17.5f),
            A("tuscany|provence|florence|spain|andalusia|portugal|lisbon|madrid|seville", "at sunset", "lavender"),
            A("greece|athens|acropolis|the acropolis|parthenon|the parthenon|olympus", "in the afternoon", "meadow", "columns", 15.5f),
            A("santorini|mykonos|sicily|sardinia|ibiza|barcelona|nice|monaco", "by the sea", "beach", "lighthouse", 15f),
            A("venice|amalfi|croatia|dubrovnik", "by the sea", "beach", "", 17f),
            A("netherlands|the netherlands|holland|amsterdam|belgium|denmark|copenhagen", "in spring", "meadow", "windmill", 11f),
            A("switzerland|the alps|alps|austria|matterhorn|the matterhorn|mont blanc|dolomites|the dolomites", "at dawn", "mountain_lake", "peak"),
            A("norway|sweden|finland|lapland|iceland|reykjavik|tromso|alaska|yukon", "under the northern lights", "aurora"),
            A("russia|moscow|saint petersburg|st petersburg|kremlin|the kremlin|red square", "in the snow", "snow", "onion_domes", 16f),
            A("siberia|greenland|antarctica|south pole|the south pole|the arctic|arctic", "on the ice", "ice_cave"),
            A("north pole|the north pole", "at Christmas", "christmas"),
            // ---- The Americas ----
            A("new york|new york city|nyc|manhattan|chicago|toronto|times square", "at night", "city", "spire", 22f),
            A("las vegas|vegas|los angeles night|miami night", "at night", "neon_city", "", -1f, Core.Precip.None),
            A("california|los angeles|hollywood|malibu|miami|florida|san diego|caribbean|the caribbean|bahamas|the bahamas|jamaica|cuba|havana|cancun|rio|rio de janeiro|copacabana", "at sunset", "beach", "", 18.5f),
            A("texas|arizona|grand canyon|the grand canyon|nevada|utah|monument valley|new mexico|wild west", "at sundown", "wild_west"),
            A("canada|banff|the rockies|rockies|rocky mountains|yellowstone|yosemite|montana|colorado|oregon|maine|vermont", "in the morning", "lake_camp"),
            A("peru|machu picchu|the andes|andes|bolivia|ecuador", "in the clouds", "temple", "peak"),
            A("chichen itza|maya|mayan pyramid|guatemala|yucatan|aztec|tikal|mexico|mexico city|teotihuacan", "in the jungle", "temple", "step_pyramid"),
            A("brazil|colombia|costa rica|panama|venezuela|the amazon river", "in the rain", "jungle"),
            A("patagonia|argentina|chile|new zealand|tasmania", "at dawn", "mountain_lake", "peak"),
            // ---- Oceania ----
            A("australia|the outback|outback|uluru|ayers rock", "at sundown", "desert", "", 18f),
            A("sydney|melbourne|gold coast|great barrier reef beach|bondi", "in the afternoon", "beach", "", 15f),
        };

        /// <summary>Names that are also ordinary words ("a turkey", "china plates"): they need "take me to…" before them.</summary>
        static readonly HashSet<string> Ambiguous = new HashSet<string> { "turkey", "china", "chile", "jordan", "nice", "maya", "pyramid", "safari", "olympus", "colorado", "arctic", "alps", "outback" };

        static readonly HashSet<string> Lead = new HashSet<string> { "a", "an", "the", "to", "in", "at", "visit", "see" };
        static readonly HashSet<string> Tail = new HashSet<string>
        {
            "at", "in", "by", "on", "with", "under", "during", "after", "before", "tonight", "today", "now", "please", "night", "sunset",
            "sunrise", "dawn", "dusk", "morning", "evening", "noon", "midnight"
        };

        /// <summary>Every landmark there is (<see cref="Look.Landmark"/>).</summary>
        public static readonly string[] Landmarks =
        {
            "pyramids", "tower", "dome", "pagoda", "clock_tower", "windmill", "arch", "colosseum", "columns", "peak", "stone_circle", "lighthouse", "spire",
            "onion_domes", "mosque", "step_pyramid", "wall"
        };

        /// <summary>The spot whose name is in the words (the longest name wins), and the name as it was said; or null.</summary>
        public static Spot Match(string normalised, out string word)
        {
            word = "";
            string t = " " + (normalised ?? "") + " ";
            Spot best = null;
            foreach (var s in All)
                foreach (var w in s.Words)
                    if (w.Length > word.Length && t.IndexOf(" " + w + " ", StringComparison.Ordinal) >= 0) { best = s; word = w; }
            return best;
        }

        /// <summary>
        /// The real place the words ask for, ready to open, or null. The words must ask to go ("take me to India",
        /// "let's go to Paris") or be the place and little else ("India", "the pyramids at night"): a place merely
        /// mentioned ("Paris is lovely", "a china plate") is not a trip.
        /// </summary>
        public static Places.Place Find(string normalised)
        {
            var spot = Match(normalised, out string word);
            string rest = Words.TravelRest(normalised);
            // Asked to go, and the name is a letter off ("take me to humalaya"): the nearest name there is.
            if (spot == null && rest.Length > 0) spot = Fuzzy(rest, out word);
            if (spot == null) return null;
            if (rest.Length == 0 && (Ambiguous.Contains(word) || !(Bare(normalised, word) || Ends(normalised, word)))) return null;
            var place = Make(spot, word);
            // A time or weather asked for replaces the title's own: "the Taj Mahal at night" is not "… at sunrise".
            if (place != null)
                foreach (var w in normalised.Split(' '))
                    if (TimeWords.Contains(w)) { place.Look.Name = Title(word); break; }
            return place;
        }

        static readonly HashSet<string> TimeWords = new HashSet<string>
        {
            "night", "tonight", "midnight", "sunset", "sunrise", "dawn", "dusk", "morning", "evening", "noon", "midday", "afternoon",
            "rain", "raining", "rainy", "snow", "snowing", "snowy", "storm", "fog", "foggy"
        };

        static readonly HashSet<string> NotTrip = new HashSet<string>
        {
            "make", "give", "add", "spawn", "generate", "get", "bring", "remove", "delete", "no", "not", "with", "from", "of", "love", "loved",
            "hate", "miss", "visited", "was", "were", "is", "about", "than", "and", "or", "in"
        };

        /// <summary>
        /// A short phrase that ends in the place ("like meta egypt" was "take me to Egypt"): at most two words before
        /// the name, none of them about making or describing, and only a time or weather after it.
        /// </summary>
        static bool Ends(string t, string word)
        {
            var w = new List<string>(t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (w.Count > 0 && Tail.Contains(w[w.Count - 1])) w.RemoveAt(w.Count - 1);
            var name = word.Split(' ');
            int before = w.Count - name.Length;
            if (before < 0 || before > 2) return false;
            for (int i = 0; i < name.Length; i++) if (w[before + i] != name[i]) return false;
            for (int i = 0; i < before; i++) if (NotTrip.Contains(w[i])) return false;
            return true;
        }

        /// <summary>The spot whose name is nearest the last words (spaces aside): one letter off, two for a long name. Or null.</summary>
        static Spot Fuzzy(string rest, out string word)
        {
            word = "";
            var w = new List<string>((rest ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (w.Count > 0 && Tail.Contains(w[w.Count - 1])) w.RemoveAt(w.Count - 1);
            Spot best = null;
            int bestD = 99;
            for (int n = 1; n <= 3 && n <= w.Count; n++)
            {
                string said = string.Concat(w.GetRange(w.Count - n, n));
                if (said.Length < 5) continue;
                foreach (var s in All)
                    foreach (var name in s.Words)
                    {
                        if (Ambiguous.Contains(name)) continue;
                        string flat = name.Replace(" ", "");
                        if (flat.Length < 5 || Math.Abs(flat.Length - said.Length) > 2) continue;
                        // The same first letter (Liberia is not Siberia); a short name only by one changed letter (whales is not Wales).
                        if (flat[0] != said[0] || (flat.Length <= 5 && flat.Length != said.Length)) continue;
                        int limit = flat.Length >= 9 ? 2 : 1;
                        int d = Edits(said, flat);
                        if (d <= limit && d < bestD) { bestD = d; best = s; word = name; }
                    }
            }
            return best;
        }

        /// <summary>How many letters must be changed, added or dropped to turn one word into the other.</summary>
        static int Edits(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var swap = prev; prev = cur; cur = swap;
            }
            return prev[b.Length];
        }

        /// <summary>The words are the place's name, with at most "the" before it and a time or weather after ("at night").</summary>
        static bool Bare(string t, string word)
        {
            var w = new List<string>(t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (w.Count > 0 && Lead.Contains(w[0]) && !word.StartsWith(w[0] + " ", StringComparison.Ordinal)) w.RemoveAt(0);
            var name = word.Split(' ');
            if (w.Count < name.Length) return false;
            for (int i = 0; i < name.Length; i++) if (w[i] != name[i]) return false;
            return w.Count == name.Length || Tail.Contains(w[name.Length]);
        }

        /// <summary>"taj mahal" → "Taj Mahal"; "the alps" → "The Alps"; "uk" and "nyc" in capitals.</summary>
        public static string Title(string word)
        {
            if (word == "uk" || word == "nyc") return word.ToUpperInvariant();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word).Replace(" Of ", " of ");
        }

        /// <summary>The spot as a place: the world it is made from, named for what was said, with its landmark.</summary>
        public static Places.Place Make(Spot s, string word)
        {
            var src = Places.Get(s.From);
            var def = Kit.Get(src != null ? src.Base : s.From);
            if (def == null) return null;
            var p = new Places.Place { Id = "atlas_" + s.Words[0].Replace(' ', '_'), Base = def.Id, Words = s.Words };
            if (src != null)
            {
                p.Look = src.Look.Copy();
                p.Hour = src.Hour; p.Fog = src.Fog; p.Wind = src.Wind; p.Density = src.Density; p.Light = src.Light; p.Warmth = src.Warmth;
                p.Precip = src.Precip; p.Extras = src.Extras;
            }
            else
            {
                p.Look = new Look
                {
                    Palette = def.Palette, TerrainHeight = def.TerrainHeight, Horizon = def.Horizon, Scatter = (string[])def.Scatter.Clone(),
                    Ambience = def.Ambience, SkyFrom = def.Id
                }.Copy();
            }
            string name = Title(word);
            p.Look.Name = (name + " " + s.After).Trim();
            p.Look.Short = name.Length <= 14 ? name : Title(s.Words[0]).Length <= 14 ? Title(s.Words[0]) : name.Substring(0, 14).Trim();
            p.Look.Landmark = s.Landmark ?? "";
            p.Look.Of = s.From;
            // An oasis in Egypt or Persia has no saguaro; a temple far from the pine woods is not under a pine-wood sky.
            if (s.From == "oasis")
                for (int i = 0; i < p.Look.Scatter.Length; i++) if (p.Look.Scatter[i] == "cactus") p.Look.Scatter[i] = "dune_grass";
            if (s.From == "temple") p.Look.SkyFrom = "none";
            if (s.Hour >= 0f) p.Hour = s.Hour;
            if (s.Precip != null) p.Precip = s.Precip;
            if (s.Fog >= 0f) p.Fog = s.Fog;
            return p;
        }
    }
}

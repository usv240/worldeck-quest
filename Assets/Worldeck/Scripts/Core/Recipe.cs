using System;
using System.Collections.Generic;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>
    /// Everything that decides one world: which kit world, the time of day, the weather and a few knobs.
    /// Small on purpose. A spoken request, a preset, the AI, a share code and the daily world all produce
    /// one of these, and the world builder only ever reads this. Values are always inside their ranges
    /// (<see cref="Clamp"/>), so a bad input can make a dull world but never a broken one.
    /// </summary>
    public sealed class Recipe
    {
        public string Biome = "campfire";
        /// <summary>Hour of day, 0–24 (0 = midnight).</summary>
        public float Hour = 22f;
        public string Precip = Core.Precip.None;
        /// <summary>0 clear air … 1 thick fog.</summary>
        public float Fog;
        /// <summary>0 still … 1 gale.</summary>
        public float Wind = 0.3f;
        /// <summary>How full the world is: 0.3 sparse … 1.6 lush.</summary>
        public float Density = 1f;
        /// <summary>-1 cold … +1 warm colour shift.</summary>
        public float Warmth;
        /// <summary>-1 darker … +1 brighter.</summary>
        public float Light;
        public readonly List<string> Extras = new List<string>();
        /// <summary>Shown to the player ("Campfire under the stars"). Always from <see cref="Words.CleanTitle"/>.</summary>
        public string Title = "";
        /// <summary>Varies prop placement and the gather spots. Same seed, same world.</summary>
        public int Seed = 1;
        /// <summary>A composed place ("a volcano") laid over the base world, or null for the kit world as it is.</summary>
        public Look Look;
        BiomeDef composed;
        Look composedFrom;

        public const float MinDensity = 0.3f, MaxDensity = 1.6f;
        public const int MaxExtras = 4;

        public BiomeDef Def
        {
            get
            {
                var b = Kit.Get(Biome) ?? Kit.Biomes[0];
                if (Look == null) return b;
                if (composed == null || composedFrom != Look || composed.Id != b.Id) { composed = Look.Compose(b); composedFrom = Look; }
                return composed;
            }
        }

        /// <summary>Whose photographed skies this world uses ("none": a sky painted from its colours).</summary>
        public string SkyBiome => Look != null && !string.IsNullOrEmpty(Look.SkyFrom) ? Look.SkyFrom : Biome;

        public static Recipe For(string biome, int seed = 1)
        {
            var def = Kit.Get(biome) ?? Kit.Biomes[0];
            var r = new Recipe
            {
                Biome = def.Id, Hour = def.DefaultHour, Precip = def.DefaultPrecip, Fog = def.DefaultFog,
                Title = def.Name, Seed = seed
            };
            r.Extras.AddRange(def.DefaultExtras);
            return r.Clamp();
        }

        public Recipe Copy()
        {
            var list = new List<string>(Extras);
            var r = new Recipe
            {
                Biome = Biome, Hour = Hour, Precip = Precip, Fog = Fog, Wind = Wind, Density = Density,
                Warmth = Warmth, Light = Light, Title = Title, Seed = Seed, Look = Look?.Copy()
            };
            r.Extras.AddRange(list);
            return r;
        }

        /// <summary>Forces every field into range and the kit's vocabulary. Returns this.</summary>
        public Recipe Clamp()
        {
            if (Kit.Get(Biome) == null) Biome = Kit.Biomes[0].Id;
            if (float.IsNaN(Hour) || float.IsInfinity(Hour)) Hour = Def.DefaultHour;
            Hour = ((Hour % 24f) + 24f) % 24f;
            if (Array.IndexOf(Core.Precip.All, Precip) < 0) Precip = Core.Precip.None;
            // Underwater the only "weather" is bubbles; above water there are no bubbles.
            if (Biome == "reef") Precip = Core.Precip.Bubbles;
            else if (Precip == Core.Precip.Bubbles) Precip = Core.Precip.None;
            if (Biome == "space" && Precip != Core.Precip.None) Precip = Core.Precip.None;
            Fog = Clamp01(Fog);
            Wind = Clamp01(Wind);
            Density = Num(Density, MinDensity, MaxDensity, 1f);
            Warmth = Num(Warmth, -1f, 1f, 0f);
            Light = Num(Light, -1f, 1f, 0f);
            var seen = new HashSet<string>();
            Extras.RemoveAll(e => Array.IndexOf(Core.Extras.All, e) < 0 || !seen.Add(e) || !Allowed(e));
            if (Extras.Count > MaxExtras) Extras.RemoveRange(MaxExtras, Extras.Count - MaxExtras);
            Look?.Clamp(Kit.Get(Biome) ?? Kit.Biomes[0]);
            Title = Words.CleanTitle(Title);
            if (Title.Length == 0) Title = Def.Name;
            return this;
        }

        bool Allowed(string extra)
        {
            if (Biome == "reef") return extra == Core.Extras.Lanterns || extra == Core.Extras.Crystals || extra == Core.Extras.Flowers;
            if (Biome == "space") return extra == Core.Extras.ShootingStars || extra == Core.Extras.Crystals || extra == Core.Extras.Lanterns;
            return true;
        }

        static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;
        static float Num(float v, float lo, float hi, float fallback)
            => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v < lo ? lo : v > hi ? hi : v;

        // ---------- how it reads ----------

        /// <summary>The sun's height, -1 (midnight) … +1 (noon), from the hour.</summary>
        public float SunHeight => (float)Math.Sin((Hour - 6f) / 12f * Math.PI);
        public bool IsNight => Def.HasTime ? SunHeight < -0.08f : Biome == "space";

        public static string TimeWord(float hour)
        {
            if (hour < 4.5f || hour >= 21.5f) return "night";
            if (hour < 7.5f) return "dawn";
            if (hour < 11f) return "morning";
            if (hour < 15f) return "midday";
            if (hour < 17.5f) return "afternoon";
            if (hour < 19.5f) return "sunset";
            return "evening";
        }

        /// <summary>"Forest · night · rain · fireflies" for the panel and the log.</summary>
        public string Line()
        {
            var parts = new List<string> { Def.Short };
            if (Def.HasTime) parts.Add(TimeWord(Hour));
            if (Precip == Core.Precip.Rain) parts.Add(Wind > 0.7f ? "storm" : "rain");
            else if (Precip == Core.Precip.Snow) parts.Add("snow");
            if (Fog > 0.55f) parts.Add("fog");
            foreach (var e in Extras) parts.Add(Core.Extras.Say(e));
            return string.Join(" · ", parts);
        }

        static readonly Dictionary<string, string[]> Nouns = new Dictionary<string, string[]>
        {
            // with article, without
            { "campfire", new[] { "a campfire in the forest", "campfire in the forest" } },
            { "beach", new[] { "a tropical beach", "tropical beach" } },
            { "desert", new[] { "the desert", "desert dunes" } },
            { "snow", new[] { "a mountain cabin", "mountain cabin" } },
            { "space", new[] { "a space station in orbit", "space station in orbit" } },
            { "reef", new[] { "a coral reef", "coral reef" } },
            { "city", new[] { "a jazz club in the city", "jazz club in the city" } },
            { "meadow", new[] { "a flower meadow", "flower meadow" } },
        };

        /// <summary>"a campfire in the forest at night, in the rain, with northern lights" (or without the article).</summary>
        public string Describe(bool article)
        {
            string noun = Nouns.TryGetValue(Biome, out var n) ? n[article ? 0 : 1] : Def.Name;
            return noun + Tail(this);
        }

        /// <summary>The time, weather and first extra as a phrase: " at night, in the rain, with fireflies".</summary>
        public static string Tail(Recipe r)
        {
            var s = new System.Text.StringBuilder();
            if (r.Def.HasTime)
            {
                string w = TimeWord(r.Hour);
                s.Append(w == "night" ? " at night" : w == "dawn" ? " at dawn" : w == "sunset" ? " at sunset" :
                         w == "midday" ? " at midday" : " in the " + w);
            }
            if (r.Precip == Core.Precip.Rain) s.Append(r.Wind > 0.7f ? ", in a storm" : ", in the rain");
            else if (r.Precip == Core.Precip.Snow && r.Biome != "snow") s.Append(", in the snow");
            if (r.Fog > 0.55f) s.Append(", in the fog");
            foreach (var e in r.Extras)
            {
                if (Array.IndexOf(r.Def.DefaultExtras, e) >= 0) continue;
                s.Append(", with ").Append(Core.Extras.Say(e));
                break;
            }
            return s.ToString();
        }

        // ---------- JSON (saved programs, share, the AI's answer) ----------

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            { "biome", Biome }, { "hour", (double)Hour }, { "precip", Precip }, { "fog", (double)Fog },
            { "wind", (double)Wind }, { "density", (double)Density }, { "warmth", (double)Warmth },
            { "light", (double)Light }, { "extras", new List<object>(Extras) }, { "title", Title }, { "seed", Seed },
            { "look", Look != null ? (object)Look.ToJson() : null }
        };

        /// <summary>
        /// Reads a recipe from untrusted JSON (a saved file or the AI). Unknown biome words are matched
        /// against the kit's words; anything missing takes the world's default. Never throws.
        /// </summary>
        public static Recipe FromJson(Dictionary<string, object> o)
        {
            if (o == null) return null;
            string biomeWord = Json.Str(o, "biome", "").Trim().ToLowerInvariant();
            string biome = Kit.Get(biomeWord) != null ? biomeWord : Words.BestBiome(biomeWord, out _);
            if (biome == null) return null;
            var r = For(biome);
            r.Hour = (float)Json.Num(o, "hour", r.Hour);
            r.Precip = Json.Str(o, "precip", r.Precip).ToLowerInvariant();
            r.Fog = (float)Json.Num(o, "fog", r.Fog);
            r.Wind = (float)Json.Num(o, "wind", r.Wind);
            r.Density = (float)Json.Num(o, "density", r.Density);
            r.Warmth = (float)Json.Num(o, "warmth", r.Warmth);
            r.Light = (float)Json.Num(o, "light", r.Light);
            if (o.ContainsKey("extras"))
            {
                r.Extras.Clear();
                foreach (var e in Json.Strings(o, "extras", 8)) r.Extras.Add(e.Trim().ToLowerInvariant().Replace(' ', '_'));
            }
            if (o.TryGetValue("look", out var look) && look != null) r.Look = Look.FromJson(look, Kit.Get(r.Biome) ?? Kit.Biomes[0]);
            r.Title = Json.Str(o, "title", r.Title);
            double seed = Json.Num(o, "seed", r.Seed);
            r.Seed = seed >= int.MinValue && seed <= int.MaxValue ? (int)seed : r.Seed;
            return r.Clamp();
        }

        public bool SameWorld(Recipe o) =>
            o != null && o.Biome == Biome && Math.Abs(o.Hour - Hour) < 0.01f && o.Precip == Precip &&
            Math.Abs(o.Fog - Fog) < 0.01f && Math.Abs(o.Wind - Wind) < 0.01f && Math.Abs(o.Density - Density) < 0.01f &&
            Math.Abs(o.Warmth - Warmth) < 0.01f && Math.Abs(o.Light - Light) < 0.01f && o.Seed == Seed &&
            string.Join(",", o.Extras) == string.Join(",", Extras) && (o.Look?.Name ?? "") == (Look?.Name ?? "");

        public override string ToString() => Line() + " #" + Seed.ToString(CultureInfo.InvariantCulture);

        // ---------- the daily world ----------

        /// <summary>
        /// Today's world: the same on every headset for a given UTC date, different tomorrow. Cycles through
        /// all the worlds before repeating one, with the time and weather varied by the date.
        /// </summary>
        public static Recipe Daily(DateTime utcDate)
        {
            int day = (int)(utcDate.Date - new DateTime(2026, 1, 1)).TotalDays;
            int n = Kit.Biomes.Length;
            int cycle = day >= 0 ? day / n : (day - n + 1) / n;
            int slot = ((day % n) + n) % n;
            // A fixed shuffle per cycle, so the order changes from one round of worlds to the next.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var rng = new Rng((uint)(cycle * 2654435761u + 17u));
            for (int i = n - 1; i > 0; i--) { int j = rng.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            var def = Kit.Biomes[order[slot]];
            var r = For(def.Id, seed: 1000 + day);
            var v = new Rng((uint)(day * 40503u + 7u));
            if (def.HasTime) r.Hour = (def.DefaultHour + (v.Next01() - 0.5f) * 6f + 24f) % 24f;
            if (v.Next01() < 0.3f && def.Id != "space" && def.Id != "reef") r.Precip = v.Next01() < 0.5f ? Core.Precip.Rain : Core.Precip.Snow;
            if (r.IsNight && def.Id != "space" && def.Id != "reef" && v.Next01() < 0.4f) r.Extras.Add(Core.Extras.Aurora);
            r.Title = "Today: " + def.Name;
            return r.Clamp();
        }
    }

    /// <summary>A tiny seeded random generator (xorshift), identical on every platform.</summary>
    public sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
        public Rng(int seed) : this(unchecked((uint)seed * 2654435761u + 1u)) { }
        public uint NextU() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        /// <summary>[0, 1)</summary>
        public float Next01() => (NextU() >> 8) * (1f / 16777216f);
        public float Range(float lo, float hi) => lo + (hi - lo) * Next01();
        /// <summary>[lo, hi)</summary>
        public int Range(int lo, int hi) => hi <= lo ? lo : lo + (int)(NextU() % (uint)(hi - lo));
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>
    /// A world composed from the kit: any place ("a volcano", "Mars", "a haunted forest") as colours, a sky, a horizon,
    /// what stands around you and what your furniture becomes, all picked from things the app already has. A base world
    /// (<see cref="Recipe.Biome"/>) still gives the structure: the task, the build on your table, the souvenir, and its
    /// rules (no rain in orbit). Every field is checked against the kit (<see cref="Clamp"/>), so a composed world, from
    /// the offline library or from Claude, can look odd but never break.
    /// </summary>
    public sealed class Look
    {
        public string Name = "", Short = "";
        public Palette Palette = new Palette();
        public float TerrainHeight = 6f;
        public string Horizon = "hills";
        public string[] Scatter = Array.Empty<string>();
        public string Ambience = "";
        /// <summary>Whose photographed skies to use ("desert"), or "none" for a sky painted from the palette.</summary>
        public string SkyFrom = "";
        /// <summary>What each role becomes, where it differs from the base world.</summary>
        public Dictionary<Role, string> Skins = new Dictionary<Role, string>();
        /// <summary>How far the props' own colours lean to the palette (0 as made … 1 fully the world's colours).</summary>
        public float Tint;
        /// <summary>The one thing on the horizon that makes it a real place ("pyramids", "tower", "dome"), or "" (Atlas.Landmarks).</summary>
        public string Landmark = "";
        /// <summary>The composed place or kit world this was made from (an Atlas place): its animals and whoever lives there come too. "" otherwise.</summary>
        public string Of = "";

        public static readonly string[] Horizons = { "mountains", "dunes", "skyline", "hills", "reef", "planet", "sea" };

        /// <summary>Prop kinds made only by the artist-made model kit (Kenney, CC0); the rest are in the worlds' own lists.</summary>
        public static readonly string[] KitKinds =
        {
            "mushroom", "stump", "tent", "log_stack", "lantern_hanging", "rover", "crate_space", "console", "bar_stool", "lounge_chair",
            "speaker", "potted_plant", "fall_tree", "crater", "crystal_rock", "meteor_rock", "pumpkin", "corn", "fence", "canoe", "lily",
            "parasol", "cone", "traffic_light", "present", "candy_cane", "bookcase", "floor_lamp", "teddy", "radio", "generator", "barrels"
        };

        static HashSet<string> scatterKinds, ambiences;
        static Dictionary<Role, HashSet<string>> skinIds;

        /// <summary>Every prop kind a composed world may scatter.</summary>
        public static HashSet<string> ScatterKinds
        {
            get
            {
                if (scatterKinds != null) return scatterKinds;
                var s = new HashSet<string>(KitKinds);
                foreach (var b in Kit.Biomes) foreach (var k in b.Scatter) s.Add(k);
                foreach (var k in new[] { "oak", "bush", "flower", "flower_patch", "stone", "log_small", "crystal_cluster", "palm", "cactus", "pine", "snow_pine", "rock", "snow_rock", "fern", "dune_grass", "ice", "coral", "kelp", "reef_rock", "shell_rock", "building", "street_lamp", "hydrant", "antenna", "panel", "tank", "light_pylon", "lantern", "cloud" }) s.Add(k);
                return scatterKinds = s;
            }
        }

        public static HashSet<string> Ambiences
        {
            get
            {
                if (ambiences != null) return ambiences;
                var s = new HashSet<string>();
                foreach (var b in Kit.Biomes) if (!string.IsNullOrEmpty(b.Ambience)) s.Add(b.Ambience);
                return ambiences = s;
            }
        }

        /// <summary>The skins each role may take: whatever any world uses for that role.</summary>
        public static HashSet<string> SkinsFor(Role role)
        {
            if (skinIds == null)
            {
                skinIds = new Dictionary<Role, HashSet<string>>();
                foreach (var b in Kit.Biomes)
                    foreach (var kv in b.Skins)
                    {
                        if (!skinIds.TryGetValue(kv.Key, out var set)) skinIds[kv.Key] = set = new HashSet<string>();
                        set.Add(kv.Value);
                    }
            }
            return skinIds.TryGetValue(role, out var r) ? r : new HashSet<string>();
        }

        /// <summary>Forces every field into the kit. <paramref name="baseDef"/> fills anything missing. Returns this.</summary>
        public Look Clamp(BiomeDef baseDef)
        {
            Name = Words.CleanTitle(Name ?? "");
            if (Name.Length == 0) Name = baseDef.Name;
            Short = Words.CleanTitle(Short ?? "");
            if (Short.Length == 0 || Short.Length > 14) Short = FirstWord(Name);
            Palette = CleanPalette(Palette, baseDef.Palette);
            TerrainHeight = float.IsNaN(TerrainHeight) || float.IsInfinity(TerrainHeight) ? baseDef.TerrainHeight : Math.Max(0f, Math.Min(30f, TerrainHeight));
            if (Array.IndexOf(Horizons, Horizon) < 0) Horizon = baseDef.Horizon;
            var scatter = new List<string>();
            foreach (var k in Scatter ?? Array.Empty<string>()) if (k != null && ScatterKinds.Contains(k) && scatter.Count < 8) scatter.Add(k);
            Scatter = scatter.Count > 0 ? scatter.ToArray() : baseDef.Scatter;
            if (!Ambiences.Contains(Ambience ?? "")) Ambience = baseDef.Ambience;
            if (SkyFrom != "none" && Kit.Get(SkyFrom ?? "") == null) SkyFrom = baseDef.Id;
            var skins = new Dictionary<Role, string>();
            if (Skins != null) foreach (var kv in Skins) if (kv.Value == "none" || SkinsFor(kv.Key).Contains(kv.Value)) skins[kv.Key] = kv.Value;
            Skins = skins;
            Tint = float.IsNaN(Tint) ? 0f : Math.Max(0f, Math.Min(1f, Tint));
            Landmark = (Landmark ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(Atlas.Landmarks, Landmark) < 0) Landmark = "";
            Of = (Of ?? "").Trim().ToLowerInvariant();
            if (Of.Length > 0 && Places.Get(Of) == null && Kit.Get(Of) == null) Of = "";
            return this;
        }

        static string FirstWord(string s)
        {
            var w = (s ?? "").Split(' ');
            foreach (var x in w) if (x.Length > 2 && x != "The") return x.Length > 14 ? x.Substring(0, 14) : x;
            return w.Length > 0 ? w[0] : "";
        }

        static Palette CleanPalette(Palette p, Palette fallback)
        {
            p ??= new Palette();
            string H(string v, string f) => IsHex(v) ? v.ToUpperInvariant() : f;
            return new Palette
            {
                Top = H(p.Top, fallback.Top), Horizon = H(p.Horizon, fallback.Horizon), Fog = H(p.Fog, fallback.Fog),
                Ground = H(p.Ground, fallback.Ground), GroundAccent = H(p.GroundAccent, fallback.GroundAccent), Prop = H(p.Prop, fallback.Prop),
                PropAccent = H(p.PropAccent, fallback.PropAccent), Emissive = H(p.Emissive, fallback.Emissive), Ambient = H(p.Ambient, fallback.Ambient),
                Neon = IsHex(p.Neon) ? p.Neon : fallback.Neon ?? ""
            };
        }

        public static bool IsHex(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length != 7 || v[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (Uri.IsHexDigit(v[i]) == false) return false;
            return true;
        }

        /// <summary>The base world with this look laid over it: what every part of the app reads.</summary>
        public BiomeDef Compose(BiomeDef b)
        {
            var skins = new Dictionary<Role, string>(b.Skins);
            foreach (var kv in Skins) skins[kv.Key] = kv.Value;
            return new BiomeDef
            {
                Id = b.Id, Name = Name, Short = Short, DefaultHour = b.DefaultHour, Sky = b.Sky, HasTime = b.HasTime,
                DefaultPrecip = b.DefaultPrecip, DefaultFog = b.DefaultFog, DefaultExtras = b.DefaultExtras, Palette = Palette,
                TerrainHeight = TerrainHeight, Horizon = Horizon, Scatter = Scatter, Ambience = Ambience, Skins = skins,
                Item = b.Item, ItemPlural = b.ItemPlural, Build = b.Build, TaskLine = b.TaskLine, DoneLine = b.DoneLine,
                Souvenir = b.Souvenir, Words = b.Words, ModelTint = Tint
            };
        }

        public Look Copy() => new Look
        {
            Name = Name, Short = Short, Palette = CleanPalette(Palette, Palette), TerrainHeight = TerrainHeight, Horizon = Horizon,
            Scatter = (string[])Scatter.Clone(), Ambience = Ambience, SkyFrom = SkyFrom, Skins = new Dictionary<Role, string>(Skins), Tint = Tint,
            Landmark = Landmark ?? "", Of = Of ?? ""
        };

        // ---------- JSON ----------

        public Dictionary<string, object> ToJson()
        {
            var skins = new Dictionary<string, object>();
            foreach (var kv in Skins) skins[kv.Key.ToString().ToLowerInvariant()] = kv.Value;
            return new Dictionary<string, object>
            {
                { "name", Name }, { "short", Short }, { "terrain", (double)TerrainHeight }, { "horizon", Horizon },
                { "scatter", new List<object>(Scatter) }, { "ambience", Ambience }, { "sky", SkyFrom }, { "tint", (double)Tint },
                { "landmark", Landmark ?? "" }, { "of", Of ?? "" },
                { "skins", skins },
                { "palette", new Dictionary<string, object>
                    {
                        { "top", Palette.Top }, { "horizon", Palette.Horizon }, { "fog", Palette.Fog }, { "ground", Palette.Ground },
                        { "ground_accent", Palette.GroundAccent }, { "prop", Palette.Prop }, { "prop_accent", Palette.PropAccent },
                        { "emissive", Palette.Emissive }, { "ambient", Palette.Ambient }
                    } }
            };
        }

        /// <summary>Reads a look from untrusted JSON (the relay, a saved world). Null if there is none. Never throws.</summary>
        public static Look FromJson(object o, BiomeDef baseDef)
        {
            if (!(o is Dictionary<string, object> d)) return null;
            var l = new Look
            {
                Name = Json.Str(d, "name", ""), Short = Json.Str(d, "short", ""),
                TerrainHeight = (float)Json.Num(d, "terrain", baseDef.TerrainHeight), Horizon = Json.Str(d, "horizon", "").ToLowerInvariant(),
                Ambience = Json.Str(d, "ambience", ""), SkyFrom = Json.Str(d, "sky", "").ToLowerInvariant(), Tint = (float)Json.Num(d, "tint", 0),
                Landmark = Json.Str(d, "landmark", "").Trim().ToLowerInvariant(), Of = Json.Str(d, "of", "").Trim().ToLowerInvariant()
            };
            if (Array.IndexOf(Atlas.Landmarks, l.Landmark) < 0) l.Landmark = "";
            var scatter = new List<string>();
            foreach (var s in Json.Strings(d, "scatter", 12)) scatter.Add(s.Trim().ToLowerInvariant().Replace(' ', '_'));
            l.Scatter = scatter.ToArray();
            if (d.TryGetValue("palette", out var po) && po is Dictionary<string, object> p)
                l.Palette = new Palette
                {
                    Top = Json.Str(p, "top", ""), Horizon = Json.Str(p, "horizon", ""), Fog = Json.Str(p, "fog", ""),
                    Ground = Json.Str(p, "ground", ""), GroundAccent = Json.Str(p, "ground_accent", ""), Prop = Json.Str(p, "prop", ""),
                    PropAccent = Json.Str(p, "prop_accent", ""), Emissive = Json.Str(p, "emissive", ""), Ambient = Json.Str(p, "ambient", "")
                };
            if (d.TryGetValue("skins", out var so) && so is Dictionary<string, object> sk)
                foreach (var kv in sk)
                    if (Enum.TryParse(kv.Key, true, out Role role) && kv.Value is string id) l.Skins[role] = id;
            return l.Clamp(baseDef);
        }
    }
}

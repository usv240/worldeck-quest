using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>What a piece of real furniture is for, which decides what it becomes in every world.</summary>
    public enum Role
    {
        /// <summary>A table or desk: the world's workbench, where gathered pieces build the result.</summary>
        Surface,
        /// <summary>A sofa, bed or chair: something to sit on.</summary>
        Seat,
        /// <summary>A shelf, cupboard, screen or anything tall: a tree, a pillar, a machine.</summary>
        Tall,
        /// <summary>A lamp: a light in the world too.</summary>
        Light,
        /// <summary>A plant: a plant in the world too.</summary>
        Plant,
        /// <summary>Unlabelled scanned clutter: a rock, a crate.</summary>
        Clutter,
        /// <summary>A door frame: an arch or gate the world keeps.</summary>
        Door,
        /// <summary>A window frame: an opening the world keeps.</summary>
        Window
    }

    /// <summary>
    /// The colours of one world at its own time of day (from the art direction research, D-art-ux):
    /// sky top and horizon, fog (= horizon), ground and its accent, props and their accent, emissive, ambient.
    /// Hex strings ("#RRGGBB") so the engine-free core can hold them; the runtime converts.
    /// </summary>
    public sealed class Palette
    {
        public string Top, Horizon, Fog, Ground, GroundAccent, Prop, PropAccent, Emissive, Ambient;
        public string Neon = "";
    }

    /// <summary>One world in the kit: its look, its sound, what furniture becomes, and its task.</summary>
    public sealed class BiomeDef
    {
        public string Id;            // "campfire"
        public string Name;          // "Campfire under the stars"
        public string Short;         // "Forest"
        public float DefaultHour;    // 22 = night
        public bool Sky = true;      // false underwater (the sky is the surface)
        public bool HasTime = true;  // space and reef ignore the clock for colour
        public string DefaultPrecip = Precip.None;
        public float DefaultFog;
        public string[] DefaultExtras = Array.Empty<string>();
        public Palette Palette;
        public float TerrainHeight;  // metres of relief far out (play area stays flat)
        public string Horizon;       // "mountains" | "dunes" | "skyline" | "hills" | "reef" | "planet" | "sea"
        public string[] Scatter;     // prop kinds placed outside the room
        public string Ambience;      // audio bed clip id
        public Dictionary<Role, string> Skins;   // what each role becomes here
        public string Item;          // what you gather ("kindling")
        public string ItemPlural;
        public string Build;         // what the table builds ("fire")
        public string TaskLine;      // the one-line ask ("The fire's hungry.")
        public string DoneLine;      // "The fire's roaring."
        public string Souvenir;      // what stays in your real room ("lantern")
        public string[] Words;       // words that pick this world
        public float ModelTint;      // composed worlds: how far props lean to the palette
    }

    public static class Precip
    {
        public const string None = "none", Rain = "rain", Snow = "snow", Bubbles = "bubbles";
        public static readonly string[] All = { None, Rain, Snow, Bubbles };
    }

    public static class Extras
    {
        public const string Aurora = "aurora", Fireflies = "fireflies", ShootingStars = "shooting_stars",
            Lanterns = "lanterns", Flowers = "flowers", Crystals = "crystals", Clouds = "clouds", Embers = "embers";
        public static readonly string[] All = { Aurora, Fireflies, ShootingStars, Lanterns, Flowers, Crystals, Clouds, Embers };

        public static string Say(string e) => e switch
        {
            Aurora => "northern lights",
            Fireflies => "fireflies",
            ShootingStars => "shooting stars",
            Lanterns => "lanterns",
            Flowers => "flowers",
            Crystals => "crystals",
            Clouds => "clouds",
            Embers => "embers",
            _ => e
        };
    }

    /// <summary>
    /// The whole kit: every world the app can build. The AI only ever chooses from this list (and the
    /// knobs in <see cref="Recipe"/>), so nothing can appear that we did not make.
    /// </summary>
    public static class Kit
    {
        public const int Version = 1;

        static Dictionary<Role, string> Skins(string surface, string seat, string tall, string light, string plant,
                                              string clutter, string door, string window) =>
            new Dictionary<Role, string>
            {
                { Role.Surface, surface }, { Role.Seat, seat }, { Role.Tall, tall }, { Role.Light, light },
                { Role.Plant, plant }, { Role.Clutter, clutter }, { Role.Door, door }, { Role.Window, window }
            };

        public static readonly BiomeDef[] Biomes =
        {
            new BiomeDef
            {
                Id = "campfire", Name = "Campfire under the stars", Short = "Forest", DefaultHour = 22f,
                DefaultExtras = new[] { Extras.Fireflies },
                Palette = new Palette { Top = "#0B1026", Horizon = "#2A3A66", Fog = "#1E2A4A", Ground = "#2B3A2E", GroundAccent = "#4A5A3A",
                                        Prop = "#1F3B33", PropAccent = "#6B4A35", Emissive = "#FF9A3C", Ambient = "#3A4570" },
                TerrainHeight = 8f, Horizon = "mountains", Scatter = new[] { "pine", "pine", "rock", "fern", "mushroom", "stump", "log_stack" },
                Ambience = "amb_forest_night",
                Skins = Skins("fire_ring", "log_bench", "pine_trunk", "lantern_post", "fern", "rock", "tree_arch", "none"),
                Item = "kindling", ItemPlural = "kindling", Build = "fire", Souvenir = "lantern",
                TaskLine = "The fire's hungry.", DoneLine = "The fire's roaring.",
                Words = new[] { "campfire", "camp", "camping", "forest", "woods", "wood", "woodland", "trees", "tree", "pine", "cabin in the woods", "fire", "bonfire", "wilderness", "jungle" }
            },
            new BiomeDef
            {
                Id = "beach", Name = "Tropical beach at sunset", Short = "Beach", DefaultHour = 18.5f,
                DefaultExtras = new[] { Extras.Clouds },
                Palette = new Palette { Top = "#3B2C6B", Horizon = "#FF9E6B", Fog = "#F2A27E", Ground = "#F2CF9B", GroundAccent = "#E0A877",
                                        Prop = "#2F7A5E", PropAccent = "#8A5A3C", Emissive = "#FFD27A", Ambient = "#B86A8A" },
                TerrainHeight = 2f, Horizon = "sea", Scatter = new[] { "palm", "palm", "rock", "shell_rock", "log_small", "dune_grass" },
                Ambience = "amb_beach",
                Skins = Skins("driftwood_table", "lounger", "palm_trunk", "tiki_torch", "palm_bush", "rock", "palm_arch", "none"),
                Item = "shell", ItemPlural = "shells", Build = "sandcastle", Souvenir = "sandcastle",
                TaskLine = "Bring shells for the castle.", DoneLine = "The castle's finished.",
                Words = new[] { "beach", "tropical", "island", "ocean", "sea", "seaside", "shore", "coast", "sand", "palm", "palms", "hawaii", "caribbean", "surf", "lagoon", "bay", "waves" }
            },
            new BiomeDef
            {
                Id = "desert", Name = "Desert dunes at dawn", Short = "Desert", DefaultHour = 6.5f,
                Palette = new Palette { Top = "#6E8FCF", Horizon = "#FFC9A0", Fog = "#F3C6A5", Ground = "#E2A66B", GroundAccent = "#C27A4E",
                                        Prop = "#5E8C5A", PropAccent = "#A0522D", Emissive = "#FFE3A3", Ambient = "#9A86B8" },
                TerrainHeight = 6f, Horizon = "dunes", Scatter = new[] { "cactus", "cactus", "rock", "rock", "dune_grass", "stone" },
                Ambience = "amb_desert",
                Skins = Skins("stone_altar", "sandstone_block", "rock_pillar", "brazier", "cactus_small", "rock", "stone_arch", "none"),
                Item = "sun shard", ItemPlural = "sun shards", Build = "sun_crystal", Souvenir = "sun_crystal",
                TaskLine = "Gather the sun shards.", DoneLine = "The crystal's awake.",
                Words = new[] { "desert", "dune", "dunes", "sahara", "sand dunes", "oasis", "egypt", "pyramid", "pyramids", "canyon", "mars", "arizona", "mesa", "cactus" }
            },
            new BiomeDef
            {
                Id = "snow", Name = "Snowy mountain cabin", Short = "Snow", DefaultHour = 15f, DefaultPrecip = Precip.Snow,
                Palette = new Palette { Top = "#2E4A7A", Horizon = "#A9C4E0", Fog = "#C9D8E8", Ground = "#EEF3F8", GroundAccent = "#B7C7DA",
                                        Prop = "#2F4F4A", PropAccent = "#7A4B2E", Emissive = "#FFB65C", Ambient = "#7F95C0" },
                TerrainHeight = 25f, Horizon = "mountains", Scatter = new[] { "snow_pine", "snow_pine", "snow_rock", "ice", "stump", "log_stack" },
                Ambience = "amb_snow",
                Skins = Skins("cabin_bench", "snowy_log", "snow_pine_trunk", "oil_lamp", "snow_bush", "snow_rock", "cabin_door", "frost_window"),
                Item = "snowball", ItemPlural = "snowballs", Build = "snowman", Souvenir = "snowman",
                TaskLine = "Roll snowballs for a snowman.", DoneLine = "Meet your snowman.",
                Words = new[] { "snow", "snowy", "winter", "mountain", "mountains", "alps", "arctic", "ski", "skiing", "cabin", "igloo", "ice", "frozen", "glacier", "christmas", "north pole", "tundra" }
            },
            new BiomeDef
            {
                Id = "space", Name = "Space station in orbit", Short = "Space", DefaultHour = 0f, Sky = true, HasTime = false,
                DefaultExtras = new[] { Extras.ShootingStars },
                Palette = new Palette { Top = "#05060F", Horizon = "#1A1440", Fog = "#120F2E", Ground = "#2A2F3D", GroundAccent = "#3C4456",
                                        Prop = "#C9CED8", PropAccent = "#E07A3F", Emissive = "#4FE3FF", Ambient = "#3A3F6B" },
                TerrainHeight = 7f, Horizon = "planet", Scatter = new[] { "rock", "rock", "rock", "antenna", "panel", "tank", "light_pylon", "rover", "crate_space", "console" },
                Ambience = "amb_space",
                Skins = Skins("console", "pilot_seat", "machine_tower", "light_pylon", "hydroponic", "crate", "airlock", "porthole"),
                Item = "energy cell", ItemPlural = "energy cells", Build = "star_map", Souvenir = "star_globe",
                TaskLine = "Power the console.", DoneLine = "Star map online.",
                Words = new[] { "space", "station", "spaceship", "starship", "ship", "orbit", "moon", "planet", "galaxy", "rocket", "cosmos", "stars", "astronaut", "nebula", "spacecraft", "sci-fi", "scifi", "future" }
            },
            new BiomeDef
            {
                Id = "reef", Name = "Coral reef underwater", Short = "Reef", DefaultHour = 12f, Sky = false, HasTime = false,
                DefaultPrecip = Precip.Bubbles,
                Palette = new Palette { Top = "#7FE0E8", Horizon = "#0E5C7A", Fog = "#0B4F6C", Ground = "#D9C49A", GroundAccent = "#8FB39A",
                                        Prop = "#FF7A6B", PropAccent = "#9B6BD6", Emissive = "#A8FFF0", Ambient = "#2E7F9A" },
                TerrainHeight = 4f, Horizon = "reef", Scatter = new[] { "coral", "coral", "kelp", "reef_rock" },
                Ambience = "amb_reef",
                Skins = Skins("giant_clam", "coral_rock", "kelp_column", "jelly_lamp", "anemone", "reef_rock", "coral_arch", "none"),
                Item = "pearl", ItemPlural = "pearls", Build = "anemone_glow", Souvenir = "pearl_shell",
                TaskLine = "Find the pearls.", DoneLine = "The reef is glowing.",
                Words = new[] { "underwater", "under water", "reef", "coral", "aquarium", "submarine", "ocean floor", "deep sea", "fish", "atlantis", "mermaid", "scuba", "diving", "sea floor" }
            },
            new BiomeDef
            {
                Id = "city", Name = "Jazz club at 2 a.m.", Short = "City", DefaultHour = 2f, DefaultPrecip = Precip.Rain,
                DefaultExtras = new[] { Extras.Lanterns },
                Palette = new Palette { Top = "#120E24", Horizon = "#3A2547", Fog = "#2A1E3A", Ground = "#2B2024", GroundAccent = "#5A3A2E",
                                        Prop = "#3B2F4A", PropAccent = "#B0703A", Emissive = "#FFC36B", Ambient = "#4A3A6A", Neon = "#FF5FA2" },
                TerrainHeight = 0f, Horizon = "skyline", Scatter = new[] { "building", "building", "street_lamp", "hydrant" },
                Ambience = "amb_city_jazz",
                Skins = Skins("bar_table", "booth", "brick_pillar", "street_lamp", "potted_palm", "speaker", "club_door", "neon_window"),
                Item = "record", ItemPlural = "records", Build = "jukebox", Souvenir = "jukebox",
                TaskLine = "Bring records for the jukebox.", DoneLine = "The band's playing.",
                Words = new[] { "city", "jazz", "bar", "club", "nightclub", "rooftop", "downtown", "new york", "tokyo", "paris", "london", "neon", "street", "lounge", "speakeasy", "cafe", "restaurant", "music", "blues", "rain city" }
            },
            new BiomeDef
            {
                Id = "meadow", Name = "Flower meadow in spring", Short = "Meadow", DefaultHour = 10f,
                DefaultExtras = new[] { Extras.Flowers, Extras.Clouds },
                Palette = new Palette { Top = "#4F8FE0", Horizon = "#CFE6F5", Fog = "#DCEBF2", Ground = "#5E9442", GroundAccent = "#7FAE52",
                                        Prop = "#3E7A45", PropAccent = "#8A6A45", Emissive = "#FFE27A", Ambient = "#8FB0D8" },
                TerrainHeight = 10f, Horizon = "hills", Scatter = new[] { "oak", "oak", "bush", "flower_patch", "flower_patch", "flower", "stump" },
                Ambience = "amb_meadow",
                Skins = Skins("picnic_table", "mossy_log", "oak_trunk", "lantern_post", "flower_bush", "mossy_rock", "hedge_arch", "none"),
                Item = "seed", ItemPlural = "seeds", Build = "blossom_tree", Souvenir = "bonsai",
                TaskLine = "Plant the seeds.", DoneLine = "Your tree's in bloom.",
                Words = new[] { "meadow", "garden", "park", "field", "fields", "flowers", "flower", "picnic", "countryside", "spring", "farm", "grass", "valley", "hills", "zen", "japan", "cherry blossom", "nature" }
            },
        };

        public static BiomeDef Get(string id)
        {
            foreach (var b in Biomes) if (b.Id == id) return b;
            return null;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Biomes.Length; i++) if (Biomes[i].Id == id) return i;
            return -1;
        }

        public static string[] Ids
        {
            get
            {
                var ids = new string[Biomes.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = Biomes[i].Id;
                return ids;
            }
        }

        /// <summary>
        /// The role of a scanned piece of furniture, from its scene labels (MRUK label names, upper case).
        /// Function first: a table is a surface even if it is also labelled "other".
        /// </summary>
        public static Role? RoleOf(IEnumerable<string> labels)
        {
            bool has(string l) { foreach (var x in labels) if (x == l) return true; return false; }
            if (has("TABLE")) return Role.Surface;
            if (has("COUCH") || has("BED") || has("CHAIR")) return Role.Seat;
            if (has("LAMP")) return Role.Light;
            if (has("PLANT")) return Role.Plant;
            if (has("STORAGE") || has("SCREEN") || has("SHELF")) return Role.Tall;
            if (has("DOOR_FRAME")) return Role.Door;
            if (has("WINDOW_FRAME")) return Role.Window;
            if (has("OTHER")) return Role.Clutter;
            return null;   // walls, floor, ceiling, wall art, global mesh: not furniture
        }

        /// <summary>A piece taller than this (m) with no clearer label reads as tall, not clutter.</summary>
        public const float TallClutter = 1.1f;
    }
}

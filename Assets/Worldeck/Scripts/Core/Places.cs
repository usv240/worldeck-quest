using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// Places composed ahead of time, so the ones people ask for most work offline and instantly: each is a base world
    /// plus a <see cref="Look"/> and the time, weather and extras that make it. Anything not here goes to Claude, which
    /// composes a look from the same kit.
    /// </summary>
    public static class Places
    {
        public sealed class Place
        {
            public string Id, Base;
            public string[] Words;
            public Look Look;
            public float Hour = -1f, Fog = -1f, Wind = -1f, Density = -1f, Light = 0f, Warmth = 0f;
            public string Precip;
            public string[] Extras;

            /// <summary>The place as a recipe (its look, time and weather), ready to open.</summary>
            public Recipe Recipe(int seed)
            {
                var r = Core.Recipe.For(Base, seed);
                r.Look = Look.Copy();
                if (Hour >= 0f) r.Hour = Hour;
                if (Fog >= 0f) r.Fog = Fog;
                if (Wind >= 0f) r.Wind = Wind;
                if (Density >= 0f) r.Density = Density;
                r.Light = Light; r.Warmth = Warmth;
                if (Precip != null) r.Precip = Precip;
                if (Extras != null) { r.Extras.Clear(); r.Extras.AddRange(Extras); }
                r.Title = Look.Name;
                return r.Clamp();
            }
        }

        static Palette P(string top, string horizon, string fog, string ground, string groundAccent, string prop, string propAccent, string emissive, string ambient) =>
            new Palette { Top = top, Horizon = horizon, Fog = fog, Ground = ground, GroundAccent = groundAccent, Prop = prop, PropAccent = propAccent, Emissive = emissive, Ambient = ambient };

        static Dictionary<Role, string> S(params (Role, string)[] s)
        {
            var d = new Dictionary<Role, string>();
            foreach (var (r, id) in s) d[r] = id;
            return d;
        }

        public static readonly Place[] All =
        {
            new Place
            {
                Id = "volcano", Base = "desert", Words = new[] { "volcano", "volcanic", "lava", "magma", "eruption", "erupting" },
                Hour = 20.5f, Fog = 0.45f, Wind = 0.4f, Extras = new[] { Core.Extras.Embers },
                Look = new Look
                {
                    Name = "Volcano at nightfall", Short = "Volcano", Horizon = "mountains", TerrainHeight = 24f, SkyFrom = "none", Ambience = "amb_desert", Tint = 0.55f,
                    Palette = P("#140A0C", "#6A2414", "#3A1812", "#2A2321", "#4C2A1E", "#3B3330", "#8C3A18", "#FF5A1E", "#4A2A2A"),
                    Scatter = new[] { "rock", "rock", "meteor_rock", "crystal_rock", "stone", "stump" },
                    Skins = S((Role.Surface, "stone_altar"), (Role.Seat, "sandstone_block"), (Role.Light, "brazier"), (Role.Tall, "rock_pillar"))
                }
            },
            new Place
            {
                Id = "mars", Base = "desert", Words = new[] { "mars", "red planet", "martian" },
                Hour = 15f, Fog = 0.3f, Wind = 0.5f, Extras = new string[0],
                Look = new Look
                {
                    Name = "On Mars", Short = "Mars", Horizon = "dunes", TerrainHeight = 9f, SkyFrom = "none", Ambience = "amb_desert", Tint = 0.65f,
                    Palette = P("#4A2A22", "#C77A52", "#B0684A", "#A2492C", "#C4683F", "#8A4630", "#D9A07A", "#FFB070", "#7A4A3A"),
                    Scatter = new[] { "rock", "rock", "crater", "meteor_rock", "rover", "antenna" },
                    Skins = S((Role.Surface, "console"), (Role.Seat, "pilot_seat"), (Role.Tall, "machine_tower"), (Role.Light, "light_pylon"))
                }
            },
            new Place
            {
                Id = "moon", Base = "space", Words = new[] { "moon", "the moon", "lunar", "moon surface" },
                Extras = new[] { Core.Extras.ShootingStars },
                Look = new Look
                {
                    Name = "On the Moon", Short = "Moon", Horizon = "planet", TerrainHeight = 6f, SkyFrom = "space", Ambience = "amb_space", Tint = 0.7f,
                    Palette = P("#000002", "#0A0C14", "#06070C", "#8C8C90", "#A8A8AC", "#77787C", "#B8B8BC", "#CFE3FF", "#2A2C34"),
                    Scatter = new[] { "crater", "crater", "rock", "meteor_rock", "rover", "antenna" }
                }
            },
            new Place
            {
                Id = "moon_base", Base = "space", Words = new[] { "moon base", "space base", "colony", "space colony", "mars base", "outpost" },
                Look = new Look
                {
                    Name = "Moon base", Short = "Base", Horizon = "planet", TerrainHeight = 5f, SkyFrom = "space", Ambience = "amb_space", Tint = 0.3f,
                    Palette = P("#000002", "#0A0C18", "#06070C", "#7A7C82", "#9A9CA2", "#6A6E78", "#C8CDD6", "#7FD8FF", "#2A2E3A"),
                    Scatter = new[] { "crate_space", "console", "generator", "barrels", "rover", "antenna", "light_pylon", "crater" }
                }
            },
            new Place
            {
                Id = "alien", Base = "space", Words = new[] { "alien planet", "alien world", "another planet", "exoplanet" },
                Extras = new[] { Core.Extras.Crystals },
                Look = new Look
                {
                    Name = "Alien world", Short = "Alien", Horizon = "planet", TerrainHeight = 12f, SkyFrom = "none", Ambience = "amb_space", Tint = 0.6f,
                    Palette = P("#0A0418", "#3A1A5A", "#2A1440", "#1E3A44", "#2E5A5A", "#5A2A7A", "#3AE0C0", "#7AFFD8", "#3A2A5A"),
                    Scatter = new[] { "crystal_rock", "crystal_rock", "mushroom", "mushroom", "rock", "crystal_cluster" }
                }
            },
            new Place
            {
                Id = "jungle", Base = "campfire", Words = new[] { "jungle", "rainforest", "rain forest", "amazon", "tropical forest" },
                Hour = 14f, Fog = 0.45f, Precip = Core.Precip.Rain, Density = 1.5f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Rainforest in the rain", Short = "Jungle", Horizon = "hills", TerrainHeight = 14f, SkyFrom = "campfire", Ambience = "amb_forest_night", Tint = 0.25f,
                    Palette = P("#3A5A4A", "#7A9A7A", "#5A7A62", "#2A4A22", "#3E6A2A", "#1E5A2A", "#6A4A2A", "#FFD27A", "#3A5A4A"),
                    Scatter = new[] { "palm", "oak", "fern", "fern", "bush", "mushroom" },
                    Skins = S((Role.Tall, "palm_trunk"), (Role.Plant, "palm_bush"))
                }
            },
            new Place
            {
                Id = "haunted", Base = "campfire", Words = new[] { "haunted forest", "haunted woods", "haunted", "spooky forest", "ghost forest" },
                Hour = 23.5f, Fog = 0.85f, Wind = 0.4f, Light = -0.2f, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Haunted forest", Short = "Haunted", Horizon = "hills", TerrainHeight = 10f, SkyFrom = "none", Ambience = "amb_forest_night", Tint = 0.5f,
                    Palette = P("#05040C", "#2A2440", "#1C1830", "#1E1E22", "#2E2A36", "#2A2630", "#5A4A6A", "#9AFFB0", "#2A2840"),
                    Scatter = new[] { "stump", "stump", "pine", "mushroom", "rock", "pumpkin" },
                    Skins = S((Role.Light, "oil_lamp"), (Role.Surface, "stone_altar"))
                }
            },
            new Place
            {
                Id = "autumn", Base = "meadow", Words = new[] { "autumn", "fall forest", "autumn forest", "autumn woods", "fall leaves", "in the fall" },
                Hour = 16.5f, Wind = 0.5f, Warmth = 0.4f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Autumn woods", Short = "Autumn", Horizon = "hills", TerrainHeight = 10f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.1f,
                    Palette = P("#6A8AB0", "#E8C090", "#D8B090", "#6A5A2A", "#9A6A2A", "#B05A1E", "#7A4A2A", "#FFC060", "#8A6A4A"),
                    Scatter = new[] { "fall_tree", "fall_tree", "fall_tree", "stump", "mushroom", "log_small" },
                    Skins = S((Role.Tall, "oak_trunk"), (Role.Seat, "mossy_log"))
                }
            },
            new Place
            {
                Id = "pumpkin_patch", Base = "meadow", Words = new[] { "pumpkin patch", "halloween", "pumpkins", "harvest" },
                Hour = 18.8f, Fog = 0.3f, Extras = new[] { Core.Extras.Lanterns, Core.Extras.Fireflies },
                Look = new Look
                {
                    Name = "Pumpkin patch at dusk", Short = "Pumpkins", Horizon = "hills", TerrainHeight = 7f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.1f,
                    Palette = P("#2A2A4A", "#E07A3A", "#8A5A5A", "#4A3A1E", "#6A5A2A", "#5A4A2A", "#E0702A", "#FF9A3A", "#5A4A5A"),
                    Scatter = new[] { "pumpkin", "pumpkin", "corn", "fence", "fall_tree", "stump" },
                    Skins = S((Role.Seat, "log_bench"), (Role.Light, "lantern_post"))
                }
            },
            new Place
            {
                Id = "farm", Base = "meadow", Words = new[] { "farm", "countryside", "barn", "ranch", "fields" },
                Hour = 11f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Farm in the morning", Short = "Farm", Horizon = "hills", TerrainHeight = 8f, SkyFrom = "meadow", Ambience = "amb_meadow",
                    Palette = P("#5A9AE0", "#CDE6F5", "#C8DEEA", "#5A8A2E", "#8AAA3A", "#3A6A2A", "#C8A050", "#FFE08A", "#7A9AAA"),
                    Scatter = new[] { "corn", "corn", "fence", "oak", "pumpkin", "bush" },
                    Skins = S((Role.Surface, "picnic_table"))
                }
            },
            new Place
            {
                Id = "sakura", Base = "meadow", Words = new[] { "cherry blossom", "cherry blossoms", "sakura", "japanese garden", "zen garden" },
                Hour = 16f, Wind = 0.35f, Extras = new[] { Core.Extras.Flowers, Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Cherry blossom garden", Short = "Sakura", Horizon = "hills", TerrainHeight = 9f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.35f,
                    Palette = P("#7AA0D0", "#F5D0D8", "#F0D8DE", "#6A8A4A", "#9AAA6A", "#E89AB0", "#7A5A4A", "#FFC0D0", "#9A8AAA"),
                    Scatter = new[] { "oak", "oak", "flower_patch", "stone", "lantern_hanging", "lily" },
                    Skins = S((Role.Light, "oil_lamp"), (Role.Plant, "flower_bush"))
                }
            },
            new Place
            {
                Id = "pirate_cove", Base = "beach", Words = new[] { "pirate", "pirates", "pirate cove", "treasure island", "cove", "shipwreck" },
                Hour = 19f, Fog = 0.25f, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Pirate cove at sunset", Short = "Cove", Horizon = "sea", TerrainHeight = 4f, SkyFrom = "beach", Ambience = "amb_beach",
                    Palette = P("#2A3A6A", "#F0905A", "#D88060", "#D8B880", "#C8A070", "#2A6A3A", "#6A4A2A", "#FFB050", "#7A6A7A"),
                    Scatter = new[] { "palm", "palm", "rock", "barrels", "log_small", "canoe" },
                    Skins = S((Role.Light, "tiki_torch"))
                }
            },
            new Place
            {
                Id = "beach_party", Base = "beach", Words = new[] { "beach party", "resort", "pool party", "beach club" },
                Hour = 17f, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Beach party", Short = "Party", Horizon = "sea", TerrainHeight = 2f, SkyFrom = "beach", Ambience = "amb_beach",
                    Palette = P("#3A6AB0", "#F8C090", "#F0B898", "#E8D0A0", "#D8B888", "#2A8A5A", "#E05A7A", "#FFD060", "#8A8AAA"),
                    Scatter = new[] { "palm", "parasol", "parasol", "lounge_chair", "speaker", "dune_grass" },
                    Skins = S((Role.Seat, "lounger"), (Role.Light, "tiki_torch"))
                }
            },
            new Place
            {
                Id = "oasis", Base = "desert", Words = new[] { "oasis", "desert oasis" },
                Hour = 17.5f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Desert oasis", Short = "Oasis", Horizon = "dunes", TerrainHeight = 6f, SkyFrom = "desert", Ambience = "amb_desert",
                    Palette = P("#4A7AC0", "#F5C890", "#E8C8A0", "#D8B070", "#C89A5A", "#3A7A3A", "#8A6A3A", "#FFD080", "#9A8A7A"),
                    Scatter = new[] { "palm", "palm", "cactus", "dune_grass", "lily", "rock" },
                    Skins = S((Role.Tall, "palm_trunk"), (Role.Plant, "palm_bush"))
                }
            },
            new Place
            {
                Id = "wild_west", Base = "desert", Words = new[] { "wild west", "western", "cowboy", "cowboys", "old west", "saloon" },
                Hour = 18.5f, Wind = 0.5f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Wild west sundown", Short = "West", Horizon = "dunes", TerrainHeight = 10f, SkyFrom = "desert", Ambience = "amb_desert",
                    Palette = P("#3A4A8A", "#F09050", "#D89070", "#C08850", "#A87040", "#4A6A3A", "#7A5030", "#FFB060", "#8A6A5A"),
                    Scatter = new[] { "cactus", "cactus", "rock", "fence", "log_stack", "barrels" },
                    Skins = S((Role.Surface, "picnic_table"), (Role.Seat, "log_bench"))
                }
            },
            new Place
            {
                Id = "savanna", Base = "desert", Words = new[] { "savanna", "savannah", "safari", "africa", "serengeti" },
                Hour = 18f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Savanna at golden hour", Short = "Savanna", Horizon = "hills", TerrainHeight = 6f, SkyFrom = "desert", Ambience = "amb_desert", Tint = 0.2f,
                    Palette = P("#4A6AA0", "#F5B870", "#E8B888", "#B89A4A", "#C8A85A", "#5A6A2A", "#7A5A30", "#FFC870", "#8A7A5A"),
                    Scatter = new[] { "oak", "dune_grass", "dune_grass", "rock", "bush", "stone" },
                    Skins = S((Role.Tall, "oak_trunk"), (Role.Seat, "log_bench"), (Role.Surface, "driftwood_table"))
                }
            },
            new Place
            {
                Id = "lake_camp", Base = "campfire", Words = new[] { "lake", "lakeside", "camping by the lake", "by the lake", "campsite", "camping trip" },
                Hour = 20f, Extras = new[] { Core.Extras.Fireflies },
                Look = new Look
                {
                    Name = "Lakeside camp", Short = "Lake", Horizon = "sea", TerrainHeight = 6f, SkyFrom = "campfire", Ambience = "amb_forest_night",
                    Palette = P("#0E1630", "#3A4A7A", "#2A3658", "#2B3A2E", "#4A5A3A", "#1F3B33", "#6B4A35", "#FF9A3C", "#3A4570"),
                    Scatter = new[] { "pine", "pine", "tent", "canoe", "rock", "lily" }
                }
            },
            new Place
            {
                Id = "christmas", Base = "snow", Words = new[] { "christmas village", "christmas", "north pole", "santa", "winter village" },
                Hour = 18.5f, Precip = Core.Precip.Snow, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Christmas village", Short = "Christmas", Horizon = "mountains", TerrainHeight = 18f, SkyFrom = "snow", Ambience = "amb_snow",
                    Palette = P("#1A2A5A", "#6A7AB0", "#8A9AC0", "#E8EEF5", "#C8D4E5", "#2A5A3A", "#C83A3A", "#FFD27A", "#5A6A9A"),
                    Scatter = new[] { "snow_pine", "snow_pine", "present", "candy_cane", "lantern_hanging", "snow_rock" },
                    Skins = S((Role.Light, "lantern_post"))
                }
            },
            new Place
            {
                Id = "ice_cave", Base = "snow", Words = new[] { "ice cave", "glacier", "frozen lake", "ice palace", "ice kingdom" },
                Hour = 13f, Fog = 0.4f, Precip = Core.Precip.None, Extras = new[] { Core.Extras.Crystals },
                Look = new Look
                {
                    Name = "Blue glacier", Short = "Glacier", Horizon = "mountains", TerrainHeight = 22f, SkyFrom = "snow", Ambience = "amb_snow", Tint = 0.6f,
                    Palette = P("#4A7AB0", "#B0D8F0", "#C0E0F0", "#C8E4F5", "#9AC8E8", "#7AB8E0", "#E0F4FF", "#BFF4FF", "#6A9AC0"),
                    Scatter = new[] { "ice", "ice", "crystal_rock", "snow_rock", "crystal_cluster", "stone" }
                }
            },
            new Place
            {
                Id = "crystal_cave", Base = "desert", Words = new[] { "crystal cave", "cave", "cavern", "crystal cavern", "underground" },
                Hour = 23f, Fog = 0.7f, Light = -0.2f, Extras = new[] { Core.Extras.Crystals },
                Look = new Look
                {
                    Name = "Crystal cavern", Short = "Cavern", Horizon = "mountains", TerrainHeight = 26f, SkyFrom = "none", Ambience = "amb_reef", Tint = 0.5f,
                    Palette = P("#06040C", "#1A1030", "#140C24", "#2A2430", "#3A3044", "#3A3448", "#9A6AE0", "#C89AFF", "#2A2440"),
                    Scatter = new[] { "crystal_rock", "crystal_rock", "crystal_cluster", "rock", "mushroom", "stone" },
                    Skins = S((Role.Light, "jelly_lamp"), (Role.Tall, "rock_pillar"))
                }
            },
            new Place
            {
                Id = "neon_city", Base = "city", Words = new[] { "tokyo", "neon city", "cyberpunk", "night city", "downtown" },
                Hour = 23f, Precip = Core.Precip.Rain, Extras = new string[0],
                Look = new Look
                {
                    Name = "Neon city in the rain", Short = "Neon", Horizon = "skyline", TerrainHeight = 0f, SkyFrom = "city", Ambience = "amb_city_jazz", Tint = 0.35f,
                    Palette = P("#05030C", "#2A1040", "#1A0C2A", "#1E1E26", "#2A2A36", "#2A2240", "#FF3AA0", "#3AE8FF", "#2A2440"),
                    Scatter = new[] { "building", "building", "street_lamp", "traffic_light", "cone", "hydrant" }
                }
            },
            new Place
            {
                Id = "cafe", Base = "city", Words = new[] { "cafe", "café", "coffee shop", "library", "reading room", "bookshop", "bookstore" },
                Hour = 21f, Precip = Core.Precip.Rain, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Rainy-night café", Short = "Café", Horizon = "skyline", TerrainHeight = 0f, SkyFrom = "city", Ambience = "amb_city_jazz",
                    Palette = P("#0A0A18", "#2A2440", "#1E1A2E", "#3A2A20", "#5A3A2A", "#4A3020", "#C08A50", "#FFC070", "#3A3040"),
                    Scatter = new[] { "bookcase", "floor_lamp", "potted_plant", "lounge_chair", "street_lamp", "building" },
                    Skins = S((Role.Surface, "bar_table"), (Role.Seat, "booth"))
                }
            },
            new Place
            {
                Id = "mountain_lake", Base = "snow", Words = new[] { "mountain lake", "alpine lake", "alps lake", "lake in the mountains" },
                Hour = 6.8f, Precip = Core.Precip.None, Fog = 0.35f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Mountain lake at dawn", Short = "Alpine", Horizon = "mountains", TerrainHeight = 26f, SkyFrom = "snow", Ambience = "amb_meadow",
                    Palette = P("#4A6AA0", "#F5C8B0", "#E0D0D8", "#4A6A3A", "#7A8A6A", "#2A4A3A", "#8A7A6A", "#FFD8A0", "#7A8AAA"),
                    Scatter = new[] { "pine", "pine", "rock", "rock", "lily", "canoe" },
                    Skins = S((Role.Tall, "pine_trunk"), (Role.Seat, "log_bench"))
                }
            },
            new Place
            {
                Id = "toy_room", Base = "meadow", Words = new[] { "toy land", "toyland", "toy world", "playroom", "candy land", "candyland", "sweet hills", "candy world" },
                Hour = 12f, Extras = new[] { Core.Extras.Flowers },
                Look = new Look
                {
                    Name = "Sweet hills", Short = "Sweets", Horizon = "hills", TerrainHeight = 8f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.15f,
                    Palette = P("#7AB8F0", "#F8E0F0", "#F0D8EA", "#F0A8C8", "#F8C8DA", "#9AE0C0", "#FFD060", "#FFE080", "#C8A8D8"),
                    Scatter = new[] { "candy_cane", "candy_cane", "present", "teddy", "flower_patch", "mushroom" }
                }
            },
            new Place
            {
                Id = "castle", Base = "meadow", Words = new[] { "castle", "old castle", "medieval castle", "castle on a hill", "palace", "royal palace", "fairy tale castle", "fairytale castle", "castle grounds" },
                Hour = 19f, Fog = 0.15f, Extras = new[] { Core.Extras.Lanterns, Core.Extras.Clouds },
                Look = new Look
                {
                    Name = "Castle grounds at dusk", Short = "Castle", Horizon = "hills", TerrainHeight = 14f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.2f,
                    Palette = P("#2A3A7A", "#F0A070", "#D8A088", "#5A7A3A", "#8A8A5A", "#8A8478", "#B0A090", "#FFC870", "#7A6A7A"),
                    Scatter = new[] { "oak", "oak", "fence", "flower_patch", "lantern_hanging", "stone" },
                    Skins = S((Role.Surface, "stone_altar"), (Role.Tall, "brick_pillar"), (Role.Light, "lantern_post"), (Role.Door, "stone_arch"))
                }
            },
            new Place
            {
                Id = "aurora", Base = "snow", Words = new[] { "northern lights", "aurora borealis", "lapland", "arctic night", "polar night" },
                Hour = 23f, Precip = Core.Precip.None, Fog = 0.05f, Extras = new[] { Core.Extras.Aurora, Core.Extras.ShootingStars },
                Look = new Look
                {
                    Name = "Northern lights over the snow", Short = "Aurora", Horizon = "mountains", TerrainHeight = 20f, SkyFrom = "snow", Ambience = "amb_snow", Tint = 0.2f,
                    Palette = P("#04101C", "#123A4A", "#0E2A3A", "#C8D8E8", "#9AB4CC", "#1E3A3A", "#5A8A9A", "#7AFFC8", "#2A4A5A"),
                    Scatter = new[] { "snow_pine", "snow_pine", "snow_rock", "ice", "log_stack" }
                }
            },
            new Place
            {
                Id = "ski_lodge", Base = "snow", Words = new[] { "ski lodge", "ski resort", "ski chalet", "chalet", "apres ski" },
                Hour = 17.5f, Precip = Core.Precip.Snow, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Ski lodge at dusk", Short = "Lodge", Horizon = "mountains", TerrainHeight = 26f, SkyFrom = "snow", Ambience = "amb_snow",
                    Palette = P("#3A4A8A", "#F0B090", "#D8C8D0", "#E8EEF5", "#C0CCDD", "#4A3A2A", "#A0603A", "#FFC070", "#6A7090"),
                    Scatter = new[] { "snow_pine", "snow_pine", "fence", "lantern_hanging", "log_stack", "snow_rock" },
                    Skins = S((Role.Surface, "cabin_bench"), (Role.Light, "oil_lamp"))
                }
            },
            new Place
            {
                Id = "aquarium", Base = "reef", Words = new[] { "aquarium", "aquarium tunnel", "fish tank", "giant fish tank", "big aquarium" },
                Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Glowing aquarium", Short = "Aquarium", Horizon = "reef", TerrainHeight = 4f, SkyFrom = "none", Ambience = "amb_reef", Tint = 0.25f,
                    Palette = P("#021A3A", "#0A4A7A", "#0A3A5A", "#2A5A6A", "#3A7A7A", "#1A6A6A", "#FF8A5A", "#7AE8FF", "#1A4A6A"),
                    Scatter = new[] { "coral", "coral", "kelp", "kelp", "reef_rock", "crystal_rock" }
                }
            },
            new Place
            {
                Id = "sunken_city", Base = "reef", Words = new[] { "underwater city", "sunken city", "city under the sea", "underwater palace", "lost city under the sea" },
                Extras = new[] { Core.Extras.Crystals, Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Sunken city under the sea", Short = "Sunken", Horizon = "reef", TerrainHeight = 6f, SkyFrom = "none", Ambience = "amb_reef", Tint = 0.65f,
                    Palette = P("#01101E", "#0A3A5A", "#08283E", "#1E4A4A", "#2A5A5A", "#2A6A6A", "#8AE0D0", "#5AFFE0", "#123A4A"),
                    Scatter = new[] { "building", "building", "coral", "kelp", "reef_rock", "crystal_rock" },
                    Skins = S((Role.Tall, "brick_pillar"), (Role.Light, "jelly_lamp"))
                }
            },
            new Place
            {
                Id = "dragon_lair", Base = "desert", Words = new[] { "dragon's lair", "dragons lair", "dragon lair", "dragon cave", "dragon's cave", "dragons cave" },
                Hour = 22f, Fog = 0.5f, Wind = 0.3f, Light = -0.1f, Extras = new[] { Core.Extras.Embers },
                Look = new Look
                {
                    Name = "Fire canyon at night", Short = "Canyon", Horizon = "mountains", TerrainHeight = 28f, SkyFrom = "none", Ambience = "amb_desert", Tint = 0.55f,
                    Palette = P("#0C0606", "#4A1A10", "#2A100C", "#2A201C", "#4A2E1E", "#3A2C26", "#C8902A", "#FFB030", "#3A2420"),
                    Scatter = new[] { "rock", "rock", "crystal_rock", "meteor_rock", "barrels", "stone" },
                    Skins = S((Role.Surface, "stone_altar"), (Role.Light, "brazier"), (Role.Tall, "rock_pillar"), (Role.Seat, "sandstone_block"))
                }
            },
            new Place
            {
                Id = "enchanted", Base = "campfire", Words = new[] { "enchanted forest", "fairy forest", "magic forest", "magical forest", "fairy woods", "elf forest", "mushroom forest" },
                Hour = 21f, Fog = 0.35f, Extras = new[] { Core.Extras.Fireflies, Core.Extras.Flowers },
                Look = new Look
                {
                    Name = "Enchanted forest", Short = "Enchanted", Horizon = "hills", TerrainHeight = 10f, SkyFrom = "none", Ambience = "amb_forest_night", Tint = 0.4f,
                    Palette = P("#06081A", "#2A1E4A", "#1E1838", "#1E2A22", "#2E3A2A", "#3A2A6A", "#E05AA0", "#7AFFE0", "#2A2A4A"),
                    Scatter = new[] { "oak", "mushroom", "mushroom", "fern", "flower_patch", "crystal_rock" },
                    Skins = S((Role.Seat, "mossy_log"), (Role.Light, "jelly_lamp"), (Role.Tall, "oak_trunk"))
                }
            },
            new Place
            {
                Id = "temple", Base = "campfire", Words = new[] { "jungle temple", "lost temple", "ancient temple", "temple ruins", "lost city", "ancient ruins" },
                Hour = 10f, Fog = 0.4f, Density = 1.4f, Extras = new string[0],
                Look = new Look
                {
                    Name = "Lost temple in the jungle", Short = "Temple", Horizon = "hills", TerrainHeight = 16f, SkyFrom = "campfire", Ambience = "amb_forest_night", Tint = 0.2f,
                    Palette = P("#5A8AA0", "#B0C8A0", "#9AB09A", "#3A5A2A", "#5A6A3A", "#6A6A5A", "#8A7A5A", "#FFD27A", "#5A6A5A"),
                    Scatter = new[] { "palm", "fern", "fern", "rock", "stone", "bush" },
                    Skins = S((Role.Surface, "stone_altar"), (Role.Tall, "rock_pillar"), (Role.Door, "stone_arch"), (Role.Light, "brazier"))
                }
            },
            new Place
            {
                Id = "sky_islands", Base = "meadow", Words = new[] { "above the clouds", "floating island", "floating islands", "sky castle", "castle in the sky", "cloud kingdom", "in the clouds" },
                Hour = 16f, Fog = 0.3f, Extras = new[] { Core.Extras.Clouds, Core.Extras.Flowers },
                Look = new Look
                {
                    Name = "Sky gardens", Short = "Sky", Horizon = "hills", TerrainHeight = 3f, SkyFrom = "none", Ambience = "amb_meadow", Tint = 0.15f,
                    Palette = P("#5A9AE8", "#F0F4FF", "#F4F6FF", "#6A9A4A", "#9ABA6A", "#4A8A3A", "#E8E8F0", "#FFF0B0", "#9AAAD0"),
                    Scatter = new[] { "cloud", "cloud", "oak", "flower_patch", "stone", "bush" },
                    Skins = S((Role.Surface, "stone_altar"), (Role.Door, "stone_arch"))
                }
            },
            new Place
            {
                Id = "lavender", Base = "meadow", Words = new[] { "lavender fields", "lavender field", "provence", "field of lavender" },
                Hour = 18.5f, Extras = new[] { Core.Extras.Flowers },
                Look = new Look
                {
                    Name = "Lavender fields at sunset", Short = "Lavender", Horizon = "hills", TerrainHeight = 8f, SkyFrom = "meadow", Ambience = "amb_meadow", Tint = 0.3f,
                    Palette = P("#4A4A9A", "#F0A0A0", "#D8A8C0", "#6A5A8A", "#8A6AAA", "#7A5AB0", "#B08AD0", "#FFC890", "#7A6A9A"),
                    Scatter = new[] { "flower_patch", "flower_patch", "flower_patch", "oak", "fence", "stone" },
                    Skins = S((Role.Surface, "picnic_table"), (Role.Plant, "flower_bush"))
                }
            },
            new Place
            {
                Id = "prehistoric", Base = "campfire", Words = new[] { "prehistoric jungle", "prehistoric", "primeval forest", "ancient jungle" },
                Hour = 13f, Fog = 0.25f, Density = 1.5f, Extras = new[] { Core.Extras.Clouds },
                Look = new Look
                {
                    Name = "Prehistoric jungle", Short = "Prehistoric", Horizon = "mountains", TerrainHeight = 22f, SkyFrom = "none", Ambience = "amb_forest_night", Tint = 0.2f,
                    Palette = P("#6A9AB0", "#D0D8B0", "#B8C8A8", "#4A5A2A", "#6A6A3A", "#2A5A2A", "#7A5A3A", "#FFB060", "#5A6A4A"),
                    Scatter = new[] { "palm", "palm", "fern", "fern", "rock", "crater" },
                    Skins = S((Role.Tall, "palm_trunk"), (Role.Plant, "palm_bush"), (Role.Light, "tiki_torch"), (Role.Seat, "mossy_log"))
                }
            },
            new Place
            {
                Id = "hot_springs", Base = "snow", Words = new[] { "hot springs", "hot spring", "onsen", "thermal springs" },
                Hour = 19.5f, Precip = Core.Precip.Snow, Fog = 0.6f, Extras = new[] { Core.Extras.Lanterns },
                Look = new Look
                {
                    Name = "Hot springs in the snow", Short = "Springs", Horizon = "mountains", TerrainHeight = 20f, SkyFrom = "snow", Ambience = "amb_snow",
                    Palette = P("#2A3A6A", "#8A8AB0", "#B8C0D0", "#E0E8F0", "#B8C4D4", "#3A4A3A", "#8A6A4A", "#FFC880", "#6A7090"),
                    Scatter = new[] { "snow_pine", "snow_pine", "snow_rock", "rock", "lantern_hanging", "stone" },
                    Skins = S((Role.Light, "oil_lamp"), (Role.Surface, "stone_altar"))
                }
            },
        };

        /// <summary>The place the words name (longest matching word wins), or null. Whole words only.</summary>
        public static Place Find(string text)
        {
            string t = " " + Words.Normalise(text) + " ";
            Place best = null;
            int bestLen = 0;
            foreach (var p in All)
                foreach (var w in p.Words)
                    if (w.Length > bestLen && t.IndexOf(" " + w + " ", StringComparison.Ordinal) >= 0) { best = p; bestLen = w.Length; }
            return best;
        }

        static readonly string[] AddWords = { "add", "more", "some", "with", "a bit of", "a little", "less", "remove", "without", "bigger", "smaller" };

        /// <summary>
        /// True when the place's word is something to add or change ("add lava", "make it more volcanic", "with some
        /// pumpkins"), not where to go ("a volcano", "take me to the moon").
        /// </summary>
        public static bool IsAddition(string text, Place p)
        {
            string t = " " + Words.Normalise(text) + " ";
            foreach (var w in p.Words)
            {
                int i = t.IndexOf(" " + w + " ", StringComparison.Ordinal);
                if (i < 0) continue;
                string before = t.Substring(0, i + 1);
                foreach (var a in AddWords)
                    if (before.Contains(" " + a + " ")) return true;
                // "no lava" removes; "no, the moon" (an article after "no") corrects where to go.
                if (before.EndsWith(" no ", StringComparison.Ordinal) && !w.StartsWith("the ", StringComparison.Ordinal) && !w.StartsWith("a ", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static Place Get(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }
}

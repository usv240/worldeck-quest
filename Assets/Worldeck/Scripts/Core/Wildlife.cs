using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// Who lives in a world: real animals at their real size out in the middle distance (a herd of zebras on the
    /// savanna, a farm's cows, the dinosaurs of a prehistoric jungle). Only the places where they belong; never in
    /// space, under the sea or in a city. Every kind is a real model (MadeKinds) with a known life size (LifeSize), and
    /// only the smooth, realistic ones (Quaternius): the blocky toy animals are for asking, not for the wild.
    /// </summary>
    public static class Wildlife
    {
        public const int MaxAnimals = 6;

        static readonly Dictionary<string, string[]> ByPlace = new Dictionary<string, string[]>
        {
            { "savanna", new[] { "zebra", "zebra", "zebra", "zebra" } },   // a herd
            { "farm", new[] { "cow", "sheep", "pig", "horse" } },
            { "prehistoric", new[] { "brontosaurus", "triceratops", "stegosaurus", "parasaurolophus" } },
            { "ice_cave", new[] { "wolf", "stag" } },
            { "aurora", new[] { "stag", "wolf" } },
            { "mountain_lake", new[] { "deer", "stag" } },
            { "wild_west", new[] { "horse", "bull" } },
            { "lake_camp", new[] { "deer", "fox" } },
            { "autumn", new[] { "deer", "fox" } },
            { "christmas", new[] { "stag" } },
            { "sakura", new[] { "deer" } },
            { "lavender", new[] { "sheep" } },
        };

        // The kit's own worlds: a little life where it fits (deer at the forest's edge, horses in the meadow).
        static readonly Dictionary<string, string[]> ByBiome = new Dictionary<string, string[]>
        {
            { "campfire", new[] { "deer" } },
            { "meadow", new[] { "horse", "sheep" } },
            { "snow", new[] { "stag" } },
        };

        /// <summary>The place id of a composed world, by its look's name, or "".</summary>
        public static string PlaceOf(Recipe r)
        {
            if (r?.Look == null || string.IsNullOrEmpty(r.Look.Name)) return "";
            foreach (var p in Places.All) if (p.Look != null && p.Look.Name == r.Look.Name) return p.Id;
            // A real place from the atlas ("Kenya") is the composed place it was made from (the savanna).
            if (!string.IsNullOrEmpty(r.Look.Of) && Places.Get(r.Look.Of) != null) return r.Look.Of;
            return "";
        }

        /// <summary>The animals that live in this world (kinds, repeated as needed), or none.</summary>
        public static string[] For(Recipe r)
        {
            if (r == null) return Array.Empty<string>();
            string place = PlaceOf(r);
            string[] kinds;
            if (place.Length > 0) { if (!ByPlace.TryGetValue(place, out kinds)) return Array.Empty<string>(); }
            else if (r.Look != null || !ByBiome.TryGetValue(r.Biome ?? "", out kinds)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var k in kinds) if (MadeKinds.Has(k) && LifeSize.Metres(k) > 0f) list.Add(k);
            if (list.Count == 0) return Array.Empty<string>();
            int n = Math.Min(MaxAnimals, list.Count <= 1 ? 2 : list.Count + 2);
            var all = new string[n];
            for (int i = 0; i < n; i++) all[i] = list[i % list.Count];
            return all;
        }

        static readonly Dictionary<string, string> Home = new Dictionary<string, string>
        {
            { "zebra", "savanna" }, { "lion", "savanna" }, { "giraffe", "savanna" }, { "elephant", "savanna" },
            { "tiger", "jungle" }, { "monkey", "jungle" }, { "parrot", "jungle" },
            { "dinosaur", "prehistoric" }, { "t_rex", "prehistoric" }, { "triceratops", "prehistoric" }, { "stegosaurus", "prehistoric" },
            { "velociraptor", "prehistoric" }, { "parasaurolophus", "prehistoric" }, { "brontosaurus", "prehistoric" },
            { "cow", "farm" }, { "bull", "farm" }, { "sheep", "farm" }, { "pig", "farm" }, { "horse", "farm" }, { "donkey", "farm" }, { "chick", "farm" },
            { "polar_bear", "ice_cave" }, { "penguin", "ice_cave" }, { "wolf", "aurora" }, { "stag", "aurora" },
            { "deer", "autumn" }, { "fox", "autumn" }, { "alpaca", "mountain_lake" }, { "llama", "mountain_lake" },
            { "fish", "aquarium" }, { "crab", "beach_party" },
        };

        /// <summary>The place an animal lives ("savanna" for a zebra), or "" for things that don't live anywhere.</summary>
        public static string HomeOf(string kind) => !string.IsNullOrEmpty(kind) && Home.TryGetValue(kind, out var p) && Places.Get(p) != null ? p : "";

        /// <summary>The animals of a world, led by three of <paramref name="herd"/> (the animal you brought home).</summary>
        public static string[] For(Recipe r, string herd)
        {
            var own = For(r);
            if (string.IsNullOrEmpty(herd) || !MadeKinds.Has(herd) || LifeSize.Metres(herd) <= 0f) return own;
            // Only its own kind: a penguin's home has penguins, not the wolves that also live on the ice.
            return new[] { herd, herd, herd, herd };
        }

        /// <summary>Every kind any world may hold (for tests).</summary>
        public static IEnumerable<string> AllKinds()
        {
            foreach (var v in ByPlace.Values) foreach (var k in v) yield return k;
            foreach (var v in ByBiome.Values) foreach (var k in v) yield return k;
        }

        /// <summary>Every place id named here (for tests).</summary>
        public static IEnumerable<string> PlaceIds => ByPlace.Keys;
    }
}

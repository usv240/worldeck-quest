using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// How big a made thing is in life (metres, its longest side) and one thing worth knowing about it: what the
    /// card beside a thing you hold up to look at says, and how big "make it life size" makes it. Our own words.
    /// </summary>
    public static class LifeSize
    {
        /// <summary>Life size is capped here (metres): a whole brontosaurus doesn't fit in a living room.</summary>
        public const float RoomMax = 3f;
        /// <summary>In a world (the walls gone) life size is real size, up to this.</summary>
        public const float WorldMax = 30f;

        /// <summary>The size "make it life size" makes it in a world: real size, up to <see cref="WorldMax"/>.</summary>
        public static float InWorld(string kind) => Math.Min(Metres(kind), WorldMax);

        /// <summary>The name a thing goes by on its card ("T. rex"), from what it was called.</summary>
        public static string Title(string name, string kind)
        {
            string title = string.IsNullOrEmpty(name) ? (kind ?? "").Replace('_', ' ') : name;
            string plain = title.Replace("-", " ").Replace(".", "").Trim().ToLowerInvariant();
            if (kind == "t_rex" && (plain == "t rex" || plain == "trex" || plain == "t_rex")) return "T. rex";
            return title.Length > 0 ? char.ToUpperInvariant(title[0]) + title.Substring(1) : title;
        }

        static readonly Dictionary<string, (float Metres, string Fact)> All = new Dictionary<string, (float, string)>
        {
            { "dinosaur", (6f, "Dinosaurs lived on Earth for over 160 million years.") },
            { "t_rex", (12f, "Its bite was one of the strongest of any land animal, ever.") },
            { "triceratops", (9f, "Its name means \"three-horned face\".") },
            { "stegosaurus", (9f, "The plates on its back may have helped it warm up and cool down.") },
            { "velociraptor", (2f, "About the size of a turkey, and covered in feathers.") },
            { "parasaurolophus", (9.5f, "Its long hollow crest may have worked like a trumpet.") },
            { "brontosaurus", (22f, "One of the longest animals ever to walk the Earth.") },
            { "fox", (1f, "A fox curls its bushy tail round itself to keep warm while it sleeps.") },
            { "wolf", (1.8f, "Wolves live and hunt in family groups called packs.") },
            { "deer", (1.8f, "Male deer grow a new set of antlers every year.") },
            { "stag", (2f, "Each spring a stag sheds its antlers, and grows new ones by summer.") },
            { "horse", (2.4f, "Horses can doze standing up: their legs lock in place.") },
            { "cow", (2.4f, "A cow's stomach has four parts.") },
            { "bull", (2.5f, "It's the cape's movement a bull reacts to, not its red colour.") },
            { "alpaca", (1.5f, "Alpacas hum to each other.") },
            { "llama", (1.8f, "Llamas have carried loads in the Andes for thousands of years.") },
            { "donkey", (1.8f, "A donkey's loud call is called a bray.") },
            { "dog", (0.8f, "A dog's sense of smell is thousands of times sharper than ours.") },
            { "sheep", (1.3f, "Sheep can tell faces apart, other sheep's and people's.") },
            { "pig", (1.5f, "Pigs are among the cleverest animals on a farm.") },
            { "zebra", (2.5f, "No two zebras have the same stripes.") },
            { "lion", (2.5f, "A lion's roar can be heard about 8 km away.") },
            { "tiger", (3f, "The largest of the big cats, and a strong swimmer.") },
            { "bee", (0.015f, "A honeybee can visit up to a hundred flowers on one trip.") },
            { "cat", (0.7f, "Cats sleep for around two thirds of the day.") },
            { "bunny", (0.45f, "A rabbit's teeth never stop growing.") },
            { "elephant", (6f, "The largest land animal alive today.") },
            { "giraffe", (5.5f, "The tallest animal alive.") },
            { "monkey", (0.6f, "Some monkeys can hang by their tails.") },
            { "panda", (1.5f, "A giant panda spends about half of every day eating bamboo.") },
            { "polar_bear", (2.5f, "Under its white fur, a polar bear's skin is black.") },
            { "koala", (0.75f, "Koalas sleep up to 20 hours a day.") },
            { "penguin", (0.8f, "Penguins can't fly in the air, but they fly underwater.") },
            { "parrot", (0.5f, "Some parrots can learn to say hundreds of words.") },
            { "chick", (0.08f, "A chick can peep to its mother from inside the egg.") },
            { "crab", (0.2f, "Most crabs walk sideways.") },
            { "fish", (0.3f, "Fish breathe through their gills, taking oxygen from the water.") },
            { "beaver", (1.2f, "Beavers build dams out of branches and mud.") },
            { "caterpillar", (0.05f, "Every butterfly and moth starts life as a caterpillar.") },
            { "person", (1.75f, "People are a little taller in the morning than at night.") },
            { "knight", (1.8f, "A full suit of armour weighed about 20 to 25 kg.") },
            { "robot", (1.5f, "\"Robot\" comes from a Czech word for hard work.") },
            { "alien", (1.5f, "Nobody has met one yet.") },
            { "zombie", (1.75f, "Only in stories.") },
            { "astronaut", (1.9f, "A spacesuit is a tiny spaceship: air, water and warmth.") },
            { "sofa", (2.1f, "\"Sofa\" comes from an Arabic word for a raised bench.") },
            { "bed", (2f, "We spend about a third of our lives asleep.") },
            { "tv", (1.2f, "") },
            { "laptop", (0.35f, "") },
            { "table_lamp", (0.5f, "") },
            { "fridge", (1.8f, "") },
            { "train", (20f, "The first public steam railway opened in England in 1825.") },
            { "christmas_tree", (2f, "") },
            { "gingerbread", (0.15f, "") },
            { "nutcracker", (0.4f, "") },
            { "sled", (1.2f, "") },
            { "spaceship", (30f, "") },
            { "carrot", (0.2f, "The first carrots were purple or yellow, not orange.") },
            { "melon", (0.3f, "A watermelon is about nine tenths water.") },
            { "sword", (1f, "") },
            { "shield", (0.8f, "") },
            { "suitcase", (0.7f, "") },
            { "pumpkin", (0.4f, "A pumpkin is a fruit: it grows from a flower and holds seeds.") },
            { "oak", (20f, "An oak can live for a thousand years.") },
            { "pine", (25f, "") },
            { "palm", (15f, "") },
            { "snowman", (1.6f, "") },
            { "tent", (2.2f, "") },
            { "lantern", (0.3f, "") },
            { "teddy", (0.4f, "The first teddy bears were made in 1902.") },
        };

        public static bool Has(string kind) => !string.IsNullOrEmpty(kind) && All.ContainsKey(kind);

        static readonly HashSet<string> Objects = new HashSet<string>
        {
            "sofa", "bed", "tv", "laptop", "table_lamp", "fridge", "train", "christmas_tree", "gingerbread", "nutcracker", "sled",
            "spaceship", "carrot", "melon", "sword", "shield", "suitcase", "pumpkin", "oak", "pine", "palm", "snowman", "tent",
            "lantern", "teddy"
        };

        /// <summary>An animal or a person (it breathes and notices you), not an object.</summary>
        public static bool IsCreature(string kind) => Has(kind) && !Objects.Contains(kind);

        /// <summary>Life size (metres, longest side), or 0 when we don't know it.</summary>
        public static float Metres(string kind) => Has(kind) ? All[kind].Metres : 0f;

        public static string Fact(string kind) => Has(kind) ? All[kind].Fact : "";

        /// <summary>The size "make it life size" makes it: its life size, no bigger than the room allows.</summary>
        public static float InRoom(string kind) => Math.Min(Metres(kind), RoomMax);

        /// <summary>"2.5 m", "80 cm", "1.5 cm".</summary>
        public static string Length(float metres)
        {
            if (metres >= 1f) return (metres >= 10f ? Math.Round(metres).ToString("0") : metres.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + " m";
            float cm = metres * 100f;
            return (cm >= 10f ? Math.Round(cm).ToString("0") : cm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + " cm";
        }

        static string Round(float x) => x >= 3f ? Math.Round(x).ToString("0") : x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The card beside a thing held up to look at: its name; how big it is in life and how big it is here; the
        /// fact. Shown size is the thing's longest side now.
        /// </summary>
        /// <summary>
        /// How big it is against a piece of your own furniture: "As long as 5 of your sofas.", "About as long as your
        /// table.", "4 would fit along your table." <paramref name="piece"/> is "sofa" or "table"; "" when there's none.
        /// </summary>
        public static string Compare(float lifeMetres, string piece, float pieceMetres)
        {
            if (lifeMetres <= 0f || string.IsNullOrEmpty(piece) || pieceMetres < 0.3f) return "";
            float r = lifeMetres / pieceMetres;
            if (r >= 1.5f)
            {
                int n = (int)Math.Round(r);
                return "As long as " + n + " of your " + (piece == "sofa" ? "sofas" : piece + "s") + ".";
            }
            if (r >= 0.7f) return "About as long as your " + piece + ".";
            int fit = (int)Math.Floor(1f / r);
            if (fit < 2 || fit > 40) return "";
            return fit + " would fit along your " + piece + ".";
        }

        public static string Card(string name, string kind, float shownMetres, string compare = "")
        {
            string title = Title(name, kind);
            float life = Metres(kind);
            if (life <= 0f) return title + "\n" + Length(shownMetres) + " here";
            string scale;
            float r = life / Math.Max(0.001f, shownMetres);
            if (r > 1.15f) scale = "1:" + Round(r);
            else if (r < 0.87f) scale = Round(1f / r) + "× life";
            else scale = "life size";
            string card = title + "\n" + Length(life) + " in life · here " + scale;
            if (!string.IsNullOrEmpty(compare)) card += "\n" + compare;
            string fact = Fact(kind);
            return fact.Length > 0 ? card + "\n" + fact : card;
        }
    }
}

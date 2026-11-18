using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>Someone who lives in a world: who they are and what they say. All of it works with no network.</summary>
    public sealed class LocalDef
    {
        /// <summary>The place or kit world they live in ("pirate_cove", "campfire"), or "" for the traveller.</summary>
        public string Id = "";
        /// <summary>The model they are made from (a MadeKinds kind: person, knight, robot, alien, zombie, astronaut).</summary>
        public string Kind = "person";
        /// <summary>For "person": which of the people they are (0 to Locals.People - 1; the order of PERSONS in Tools/kit/made.py).</summary>
        public int Seed = 1;
        public string Name = "", Role = "";
        /// <summary>What they say when you first look at them; who they are; about this place; something to try here.</summary>
        public string Hello = "", Who = "", Here = "", Tip = "";
    }

    /// <summary>
    /// "Say a world. Someone's in it.": every world has a local. They greet you when you look at them, answer three
    /// questions from their own lines (instantly, offline), and their tip is always something you can say there.
    /// Anything else you ask goes to Claude, who answers as them. Our own characters and words.
    /// </summary>
    public static class Locals
    {
        static LocalDef L(string id, string kind, int seed, string name, string role, string hello, string who, string here, string tip) =>
            new LocalDef { Id = id, Kind = kind, Seed = seed, Name = name, Role = role, Hello = hello, Who = who, Here = here, Tip = tip };

        public static readonly LocalDef[] All =
        {
            // ---- the eight kit worlds ----
            L("campfire", "person", 14, "Wren", "a forest ranger", "Evening! Mind if I share your fire?", "I'm Wren. I walk these woods and count the owls.", "Oldest pines for miles. The deer come close if you sit still.", "Say \"add fireflies\" and watch the clearing wake up."),
            L("beach", "person", 11, "Kai", "a beachcomber", "Hey! The tide's on its way out.", "Kai. I comb this beach for whatever the sea leaves.", "Best sunsets anywhere. The palms were here before the sand.", "Say \"add lanterns\" and the beach glows till morning."),
            L("desert", "person", 0, "Sable", "a caravan guide", "You found the shade. Good.", "Sable. I guide caravans across the dunes.", "The dunes move a little every night. The stars don't.", "Say \"at night\" for the stars. They're worth it."),
            L("snow", "person", 5, "Tove", "a cabin keeper", "Come in from the cold!", "I'm Tove. I keep the lamps lit up here.", "Snowed in half the year. The quiet is the best part.", "Say \"add northern lights\". Trust me."),
            L("space", "astronaut", 1, "Pilar", "the station's engineer", "Welcome aboard. Watch your head.", "Pilar. I keep this station breathing.", "That's home down there. One orbit every ninety minutes.", "Say \"add crystals\". They float in zero gravity."),
            L("reef", "robot", 1, "Bubbles", "a reef-tending robot", "Blub! A visitor! Hello!", "I am Bubbles. I count the fish. All of them.", "Four thousand and six fish live here. I checked twice.", "Say \"add crystals\" and the reef will glow."),
            L("city", "person", 9, "Marlow", "a piano player", "Late night, huh? Pull up a seat.", "Marlow. I play piano here till the rain stops.", "Best little club in the city. It never quite closes.", "Say \"play some music\". I take requests."),
            L("meadow", "person", 7, "Posy", "a gardener", "Lovely day for it, isn't it?", "I'm Posy. I planted most of what you see.", "Spring comes early here. The bees keep a schedule.", "Say \"add flowers\". You can never have too many."),
            // ---- the composed places ----
            L("volcano", "person", 13, "Cinder", "a volcano watcher", "Careful. She's grumbling today.", "Cinder. I measure the mountain's temper.", "That glow is the crater. It's never fully dark here.", "Say \"add embers\" if you want to see her breathe."),
            L("mars", "astronaut", 2, "Dex", "a surveyor", "First visitor in two hundred days!", "Dex. I map this dust, one crater at a time.", "Rust-red rock as far as the rover goes. And farther.", "Say \"make it windy\". The dust storms are something."),
            L("moon", "astronaut", 3, "Luz", "a moonwalker", "One small hop! Mind the craters.", "Luz. I plant flags and collect rocks.", "No wind, no sound. Footprints here last forever.", "Say \"add shooting stars\" and look up."),
            L("moon_base", "robot", 2, "Bolt", "the base's caretaker", "Airlock sealed. Welcome to the base.", "I am Bolt. I fix what the dust breaks.", "Six domes, one greenhouse, and me.", "Say \"make me a rover\". I'll park it."),
            L("alien", "alien", 1, "Zib", "someone who lives here", "Oh! A tall one! Greetings!", "Zib. I live under the third moon.", "Our sky has three colours. Yours has one, I hear.", "Say \"add crystals\". They sing here."),
            L("jungle", "person", 4, "Tamsin", "an explorer", "Shh. Hear that? Parrots.", "Tamsin. I draw maps of places with no roads.", "Everything here is growing, even at night.", "Say \"make it rain\". The jungle loves it."),
            L("haunted", "zombie", 1, "Mort", "the groundskeeper", "Oh, don't mind me. I only look scary.", "Mort. I rake the leaves. For a very long time now.", "The fog never leaves. Neither do I, really.", "Say \"more fog\" if you're feeling brave."),
            L("autumn", "person", 8, "Rowan", "a woodcutter", "Mind the leaves, they're slippery!", "Rowan. I stack the firewood for winter.", "Every tree turns a different red. I've counted nine.", "Say \"make it windy\" and the leaves will dance."),
            L("pumpkin_patch", "person", 7, "Hattie", "a pumpkin farmer", "Pick a good one! They're all good ones.", "Hattie. I grow the biggest pumpkins in the valley.", "Planted in spring, orange by autumn. Magic, really.", "Say \"add three pumpkins\". There's room."),
            L("farm", "person", 1, "Clem", "a farmer", "Morning! The cows said you'd come.", "Clem. I run this farm, or it runs me.", "Sheep there, cows there, and the pig goes where it likes.", "Say \"make me a chick\". They're very small."),
            L("sakura", "person", 0, "Mei", "a gardener", "You came at blossom time. Lucky.", "Mei. I look after these trees.", "The petals fall for one week. Then we wait a year.", "Say \"add flowers\" and the garden fills."),
            L("pirate_cove", "person", 12, "Captain Brine", "a retired pirate captain", "Ahoy! Friend or seagull?", "Captain Brine. Retired. Mostly.", "My ship's just behind that rock. Don't look for it.", "Say \"add lanterns\" and the cove lights up."),
            L("beach_party", "person", 4, "Bea", "the party's host", "You made it! Grab a seat!", "Bea. I throw the best beach parties on this coast.", "Sand, sunset and a speaker. That's the whole recipe.", "Say \"play some music\" and it's a party."),
            L("oasis", "person", 0, "Zahra", "a well keeper", "The water's cool. Sit a while.", "Zahra. I keep this well and its stories.", "The only green for a hundred dunes. Travellers find it by the palms.", "Say \"at night\" and count the stars with me."),
            L("wild_west", "person", 8, "Dusty", "a ranch hand", "Howdy. Long ride?", "Dusty. I mind the horses out here.", "Nothing but rock, sky and a bull who dislikes me.", "Say \"at sunset\". The canyon turns to gold."),
            L("savanna", "person", 14, "Amara", "a wildlife guide", "Quiet now. The herd is close.", "Amara. I know every zebra here by its stripes.", "They walk to the water at dusk. Every day.", "Say \"make me a zebra\" and hold it up close."),
            L("lake_camp", "person", 11, "Finn", "a camper", "The water's like glass this morning.", "Finn. I paddle out before the birds wake up.", "The lake's so still it holds the whole sky.", "Say \"make me a canoe\". There's room on the shore."),
            L("christmas", "person", 5, "Nell", "a toymaker", "Merry everything! Mind the snow.", "Nell. I build toys all year for one night.", "Every window has a candle. That's the rule here.", "Say \"make me a present\". Go on."),
            L("ice_cave", "person", 8, "Isak", "an ice climber", "Watch your step. It's all ice.", "Isak. I climb what's frozen.", "This ice is older than any city. Listen: it creaks.", "Say \"add crystals\". The ice lights up."),
            L("crystal_cave", "person", 13, "Opal", "a crystal miner", "Mind your eyes. They're bright today.", "Opal. I dig for crystals and keep the small ones.", "Every one grew a hair's width a year.", "Say \"darker\". They glow best in the dark."),
            L("neon_city", "robot", 3, "Blink", "a street-stall robot", "Welcome! Noodles? I'm out of noodles.", "I am Blink. I sell snacks I no longer have.", "Ten thousand signs, and it rains every night.", "Say \"make it rain\". The neon doubles."),
            L("cafe", "person", 2, "Odette", "a barista", "Table by the window? It's yours.", "Odette. I make the coffee and guess the orders.", "Warm inside, whatever the sky is doing. People stay for hours.", "Say \"play some music\". Something slow."),
            L("mountain_lake", "person", 5, "Greta", "a hiker", "You made the climb! Worth it?", "Greta. I walk up here every summer.", "Snow on the peaks, and the water never warms.", "Say \"at dawn\". The peaks turn pink."),
            L("toy_room", "robot", 4, "Tock", "a wind-up robot", "Play time! Is it play time?", "I am Tock. Wind me up and I walk.", "Everything here is a toy. Even the hills, I think.", "Say \"make me a teddy bear\". He's nice."),
            L("castle", "knight", 1, "Sir Alder", "a knight", "Halt! Oh. You're a guest. Welcome.", "Sir Alder. I guard this castle and its garden.", "No dragon in years. I keep the armour on anyway.", "Say \"make me a sword\". A small one."),
            L("aurora", "person", 0, "Aila", "a sky watcher", "Look up. They're starting.", "Aila. I wait all winter for these lights.", "Green first, then violet if you're lucky.", "Say \"add shooting stars\" for the full show."),
            L("ski_lodge", "person", 11, "Lars", "a ski instructor", "Fresh snow! Best day of the year.", "Lars. I teach people to fall down nicely.", "The lodge has a fire and the slope has no queue.", "Say \"add northern lights\". The slope turns green."),
            L("aquarium", "person", 6, "Marina", "the aquarium's keeper", "Shh. It's feeding time.", "Marina. I feed four hundred fish by name.", "The glass is as thick as your arm is long.", "Say \"make me a fish\" and hold it up."),
            L("sunken_city", "robot", 5, "Kelp", "a salvage robot", "Glub. Rare to see visitors down here.", "I am Kelp. I catalogue what the sea kept.", "A whole city, asleep under the water.", "Say \"add lanterns\" and the streets light up."),
            L("dragon_lair", "knight", 2, "Dame Thorn", "a dragon-watcher", "Quiet. It's asleep. I think.", "Dame Thorn. I watch the lair so the village sleeps.", "It snores smoke. That's the warm air you feel.", "Say \"add embers\". Quietly."),
            L("enchanted", "person", 7, "Fern", "a forest keeper", "You can see me? Most can't.", "Fern. I look after what glows in here.", "The trees whisper. Mostly about the weather.", "Say \"add fireflies\" and they'll come to you."),
            L("temple", "person", 10, "Tavi", "an archaeologist", "Don't touch the... ah, it's fine.", "Tavi. I read old stones for a living.", "A thousand steps, and someone carved every one.", "Say \"at sunset\" and the stone turns gold."),
            L("sky_islands", "person", 4, "Skye", "a kite flyer", "Hold on to your hat up here!", "Skye. I fly kites between the islands.", "The clouds are below us. Mind the edge.", "Say \"add clouds\" and we'll float through them."),
            L("lavender", "person", 7, "Elodie", "a lavender farmer", "Breathe in. Go on.", "Elodie. I cut the lavender at sunset.", "Purple to the hills, and the bees never stop.", "Say \"at sunset\". It's the best hour."),
            L("prehistoric", "person", 10, "Dr. Flint", "a fossil hunter", "Stay low. The big ones can't see well.", "Dr. Flint. I study them while they're still around.", "Sixty-six million years before your sofa.", "Say \"make me a t rex\". A small one!"),
            L("hot_springs", "person", 2, "Yuki", "a bathkeeper", "The water's perfect. It always is.", "Yuki. I keep the springs and the lanterns.", "Snow on your head, warm to your chin.", "Say \"add shooting stars\" and make a wish."),
        };

        /// <summary>For a place Claude composed (no one was written for it): a traveller who goes wherever you name.</summary>
        public static LocalDef Traveller(string placeName) => L("", "person", 1, "Pip", "a traveller",
            "Oh! I didn't expect company here.", "Pip. Just a traveller. I go wherever someone names.",
            string.IsNullOrEmpty(placeName) ? "I've never seen a place quite like this." : "I've never seen a place like this: " + placeName.ToLowerInvariant() + ".",
            "Say \"a volcano\", or any place. I'll meet you there.");

        /// <summary>Who lives in this world: the place's own local, the kit world's, or the traveller for a composed place.</summary>
        /// <summary>The atlas's India (the meadow with the white dome) has its own host.</summary>
        public static readonly LocalDef India = L("india", "person", 0, "Meera", "a palace gardener", "Namaste! Welcome to the gardens.",
            "I'm Meera. I keep the palace gardens.", "The marble turns pink at dawn and gold by evening.", "Say \"add flowers\". The gardens are never finished.");

        public static LocalDef For(Recipe r)
        {
            if (r == null) return null;
            if (r.Look != null && r.Look.Of == "meadow" && r.Look.Landmark == "dome") return India;
            string place = Wildlife.PlaceOf(r);
            if (place.Length > 0) return Get(place) ?? Traveller(r.Look.Name);
            if (r.Look != null) return (string.IsNullOrEmpty(r.Look.Of) ? null : Get(r.Look.Of)) ?? Traveller(r.Look.Name);
            return Get(r.Biome ?? "");
        }

        /// <summary>A favour: something to make (a kind the headset makes offline), how they ask, how they thank you, and
        /// what they say about it next time.</summary>
        public sealed class Favour { public string Kind, Name, Ask, Thanks, Remember; }

        static Favour F(string kind, string name, string ask, string thanks, string remember) =>
            new Favour { Kind = kind, Name = name, Ask = ask, Thanks = thanks, Remember = remember };

        static readonly Dictionary<string, Favour> Favours = new Dictionary<string, Favour>
        {
            { "campfire", F("fox", "a fox", "I keep hearing a fox\nand never see it. Make me one?", "There it is! I knew\nI'd heard a fox.", "Your fox still visits\nmy fire at night.") },
            { "beach", F("crab", "a crab", "The crabs are hiding today.\nCould you make me one?", "Ha! Look at it go sideways.", "Your crab dug a castle.\nWell, a hole.") },
            { "desert", F("horse", "a horse", "My horse wandered off at dawn.\nCould you make me another?", "A fine horse. We ride at dusk.", "Your horse knows the dunes\nbetter than I do now.") },
            { "snow", F("stag", "a reindeer", "The reindeer are late.\nCould you make me one?", "There's my reindeer!\nThe others will follow.", "Your reindeer came back\nwith friends.") },
            { "space", F("spaceship", "a spaceship", "My shuttle's in for repairs.\nCould you make me a ship?", "Perfect. I'll fly it gently.", "Your ship's docked out back.\nIt flies beautifully.") },
            { "reef", F("fish", "a fish", "Blub! One fish is missing.\nCould you make me one?", "Four thousand and seven!\nThe count is right!", "Your fish has made\nmany friends. Blub!") },
            { "city", F("cat", "a cat", "My cat's out in the rain again.\nCould you make me one?", "A cat for the piano. Perfect.", "Your cat sleeps on the piano.\nEvery night.") },
            { "meadow", F("bee", "a bee", "The bees are late today.\nCould you make me one?", "Busy already! Thank you.", "Your bee brought\nthe whole hive back.") },
            { "savanna", F("zebra", "a zebra", "One zebra is missing\nfrom my count. Make me one?", "Every stripe in place.\nThank you!", "Your zebra leads\nthe herd now.") },
            { "farm", F("chick", "a chick", "One of my chicks wandered off.\nCould you make me one?", "There's my little one!", "Your chick is a hen now.\nShe rules the yard.") },
            { "pirate_cove", F("parrot", "a parrot", "My parrot flew off with my hat.\nMake me a new one?", "Ahoy, matey! Hat or no hat.", "Your parrot still won't\nreturn my hat.") },
            { "christmas", F("nutcracker", "a nutcracker", "I'm one nutcracker short.\nCould you make me one?", "Just in time for tonight!", "Your nutcracker stands\nin my window.") },
            { "jungle", F("monkey", "a monkey", "My monkey friend is missing.\nCould you make me one?", "There you are, troublemaker!", "Your monkey stole\nmy map again.") },
            { "haunted", F("cat", "a cat", "Every groundskeeper needs\na cat. Make me one?", "A cat! I'll call him Shadow.", "Shadow sends his regards.\nFrom the rooftops.") },
            { "mars", F("robot", "a robot", "I need help with the samples.\nCould you make me a robot?", "Two of us now. Faster mapping!", "Your robot mapped\nthree new craters.") },
            { "moon", F("robot", "a robot", "I need a hand with the rocks.\nCould you make me a robot?", "Finally, a helper!", "Your robot collects\nthe best rocks.") },
            { "moon_base", F("spaceship", "a spaceship", "The rover's broken again.\nCould you make me a ship?", "Parking it by the airlock.", "Your ship flies the supply run\nevery week.") },
            { "alien", F("spaceship", "a flying saucer", "Mine is in for repairs.\nCould you make me a saucer?", "Zib's new saucer! Zoom!", "I flew your saucer\nto the third moon.") },
            { "ice_cave", F("penguin", "a penguin", "One penguin is missing.\nCould you make me one?", "Back with the colony!", "Your penguin is the best\nslider on the ice.") },
            { "wild_west", F("horse", "a horse", "The bull chased off my horse.\nCould you make me another?", "Easy now. Good horse.", "Your horse outruns\nthat bull every time.") },
            { "lake_camp", F("chick", "a duckling", "One duckling is lost.\nCould you make me one?", "Back to its mother, quick!", "Your duckling swims\nat the front now.") },
            { "prehistoric", F("triceratops", "a triceratops", "The herd left one behind.\nCould you make me one?", "Now the herd is whole.", "Your triceratops guards\nthe nest.") },
        };

        static readonly string[] AnyKinds = { "dog", "cat", "bunny", "parrot", "horse" };

        /// <summary>The favour this local asks (every local has one; the ones without their own ask for a pet).</summary>
        public static Favour FavourOf(LocalDef l)
        {
            if (l == null) return null;
            if (Favours.TryGetValue(l.Id ?? "", out var f)) return f;
            string key = MemoryKey(l);
            int h = 17;
            foreach (char ch in key) h = unchecked(h * 31 + ch);
            string kind = AnyKinds[(h & 0x7fffffff) % AnyKinds.Length];
            string name = "a " + kind;
            return F(kind, name, "It gets lonely out here.\nCould you make me " + name + "?", "Oh, thank you! What a friend.", "Your " + kind + " never leaves\nmy side now.");
        }

        /// <summary>What a local is remembered by (their world, or the traveller's name).</summary>
        public static string MemoryKey(LocalDef l) => l == null ? "" : !string.IsNullOrEmpty(l.Id) ? l.Id : "traveller-" + (l.Name ?? "").ToLowerInvariant();

        /// <summary>Their hello when you come back: what they remember (the favour, or the last thing you made there).</summary>
        public static string WelcomeBack(LocalDef l, bool favourDone, string lastMade)
        {
            string head = "You're back!";
            if (favourDone) return head + "\n" + FavourOf(l).Remember;
            if (!string.IsNullOrEmpty(lastMade)) return head + "\nHow's that " + lastMade + " doing?";
            return head + "\nGood to see you again.";
        }

        public static LocalDef Get(string id)
        {
            foreach (var l in All) if (l.Id == id) return l;
            return null;
        }

        /// <summary>The thing to say inside a tip ("add fireflies"), or "".</summary>
        public static string TipPhrase(LocalDef l)
        {
            if (l == null) return "";
            int a = l.Tip.IndexOf('"'), b = a >= 0 ? l.Tip.IndexOf('"', a + 1) : -1;
            return b > a ? l.Tip.Substring(a + 1, b - a - 1) : "";
        }

        /// <summary>
        /// Their own answer to what was said: a greeting, who they are, about this place, what to do here. "" when it
        /// is something else (then Claude answers as them, or they fall back to <see cref="LocalDef.Here"/>).
        /// </summary>
        public static string Answer(LocalDef l, string said)
        {
            if (l == null) return "";
            string t = " " + Words.Normalise(said) + " ";
            if (Has(t, " who are you ", " your name ", " who is that ", " who is this ", " who's that ", " who's this ", " what are you ", " what do you do ", " introduce ")) return l.Who;
            if (Has(t, " what can i do ", " what should i do ", " what can i say ", " what now ", " what next ", " any tips ", " any ideas ", " what can we do ")) return l.Tip;
            if (Has(t, " where am i ", " what is this place ", " what's this place ", " where is this ", " what place ", " tell me about this place ", " about here ", " where are we ")) return l.Here;
            if (Has(t, " hi ", " hello ", " hey ", " ahoy ", " howdy ", " good morning ", " good evening ", " greetings ")) return l.Hello;
            return "";
        }

        static bool Has(string t, params string[] any)
        {
            foreach (var a in any) if (t.Contains(a)) return true;
            return false;
        }

        /// <summary>True when the words name this local ("hi, Captain Brine", "Wren, what's that?").</summary>
        public static bool Named(LocalDef l, string said)
        {
            if (l == null) return false;
            string t = " " + Words.Normalise(said) + " ";
            foreach (var part in Words.Normalise(l.Name).Split(' '))
                if (part.Length >= 3 && part != "captain" && part != "sir" && part != "dame" && t.Contains(" " + part + " ")) return true;
            return false;
        }

        /// <summary>Who Claude answers as: "captain brine, a retired pirate captain, at pirate cove at dusk".</summary>
        public static string Persona(LocalDef l, string placeName) =>
            l == null ? "" : l.Name + ", " + l.Role + (string.IsNullOrEmpty(placeName) ? "" : ", at " + placeName.ToLowerInvariant());

        /// <summary>How tall they stand (metres).</summary>
        /// <summary>
        /// How tall they stand. The people are small, round storybook figures (big heads): at a grown-up's 1.7 m
        /// they loomed over the bench like giant toddlers, so they stand as small folk, eye to eye with you seated.
        /// </summary>
        public static float Height(LocalDef l) => l == null ? 1.3f : l.Kind == "robot" || l.Kind == "alien" ? 1.15f : l.Kind == "astronaut" ? 1.45f : 1.3f;

        /// <summary>How many people a "person" can be (the PERSONS list in Tools/kit/made.py): which of the people they are is LocalDef.Seed.</summary>
        public const int People = 15;

        /// <summary>
        /// "Ask him if he ever found treasure", "tell her I said hello": words for whoever lives here, said to the
        /// guide. Returns what to ask them ("" when the words are not that).
        /// </summary>
        public static string AskedTo(string text)
        {
            string t = Words.Normalise(text ?? "");
            foreach (var lead in new[] { "please ", "can you ", "could you " })
                if (t.StartsWith(lead, StringComparison.Ordinal)) t = t.Substring(lead.Length);
            foreach (var verb in new[] { "ask ", "tell " })
            {
                if (!t.StartsWith(verb, StringComparison.Ordinal)) continue;
                string rest = t.Substring(verb.Length);
                foreach (var who in new[] { "him ", "her ", "them " })
                    if (rest.StartsWith(who, StringComparison.Ordinal) && rest.Length > who.Length + 2) return rest.Substring(who.Length);
            }
            return "";
        }
    }
}

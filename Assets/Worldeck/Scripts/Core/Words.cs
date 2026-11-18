using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Worldeck.Core
{
    public enum Act { None, NewWorld, Edit, End, Freeze, Resume, Save, Undo, Help, Surprise, Daily, Unclear, Describe, Run, LastWorld, EnterCode, Music, MusicOff, Storm, Rewind, Divert, Evade, Repair, Sound, Tour }

    /// <summary>What a sentence meant, and what to do and say about it.</summary>
    public sealed class Heard
    {
        public Act Act = Act.None;
        /// <summary>The world after this sentence (NewWorld, Edit, Surprise, Daily). Null otherwise.</summary>
        public Recipe Result;
        /// <summary>The short reply the room gives. Always written by us, never by the AI.</summary>
        public string Reply = "";
        /// <summary>A world was found, but some words weren't understood: worth asking the AI to refine it.</summary>
        public bool Refine;
        /// <summary>One question when the sentence wasn't enough, with up to three world choices.</summary>
        public string Question = "";
        public string[] Choices = Array.Empty<string>();
        /// <summary>The sentence described a place this parser doesn't know: ask the AI if there is one.</summary>
        public bool AskAi;
        /// <summary>The name to save under ("save this as my happy place").</summary>
        public string SaveName = "";
        /// <summary>What an emergency command is about ("shields", or seconds to rewind).</summary>
        public string Arg = "";
        /// <summary>What changed, for the log ("biome hour precip").</summary>
        public string Changed = "";
    }

    /// <summary>
    /// Understands what people say to the room, with no network and no AI: worlds by their words
    /// ("a snowy forest at night"), edits ("darker", "rain", "more trees", "no fireflies"), and the
    /// controls ("take me home", "freeze", "save this", "undo"). The AI is only asked about places this
    /// doesn't know, and its answer is read back through the same vocabulary.
    /// </summary>
    public static class Words
    {
        // ---------- entry point ----------

        public static Heard Parse(string text, Recipe current, int seed = 1)
        {
            var h = new Heard();
            string t = Soften(FixHeard(Normalise(text)));
            if (t.Length == 0) { h.Act = Act.None; return h; }

            // Controls first: "take me home" must never be read as a place.
            if (t == "my room" || t == "go to my room" || t == "home" || t == "leave" || t == "go home now" || t == "close" || t == "close this" ||
                t == "i want to leave" || t == "i want to go home" || t == "let me out" || t == "stop this" || t == "my house" || Any(t, "exit", "go home", "take me home", "back to my room", "end this", "end the world", "leave this world",
                    "back to the room", "quit", "i'm done", "im done", "back to my room", "get me out of here", "bring me home", "get me home", "send me home"))
            { h.Act = Act.End; h.Reply = "Back to your room."; return h; }
            // The emergency: time, power, repairs.
            var rw = Regex.Match(t, @"\b(rewind|go back|back up)\b(?: (\d+|ten|twenty|thirty|sixty) seconds?)?");
            if (rw.Success && (t.StartsWith("rewind", StringComparison.Ordinal) || rw.Groups[2].Success || Any(t, "rewind")))
            {
                string n = rw.Groups[2].Value;
                h.Act = Act.Rewind;
                h.Arg = n == "ten" ? "10" : n == "twenty" ? "20" : n == "sixty" ? "60" : n.Length > 0 && char.IsDigit(n[0]) ? n : "30";
                h.Reply = "Rewinding.";
                return h;
            }
            if (Any(t, "try again", "try that again", "another way", "do over"))
            { h.Act = Act.Rewind; h.Arg = "30"; h.Reply = "Rewinding."; return h; }
            // (No "shields up" / "power to engines": borrowed phrases. The storm is played with your hands.)
            if (Any(t, "power to lights", "power to the lights", "divert power to lights", "divert power to the lights", "lights up"))
            { h.Act = Act.Divert; h.Arg = "lights"; h.Reply = "Power to lights."; return h; }
            if (Any(t, "evade", "dodge", "evasive"))
            { h.Act = Act.Evade; h.Reply = "Evading."; return h; }
            if (Any(t, "fix it", "repair it", "fix the", "repair the", "fix that"))
            { h.Act = Act.Repair; h.Reply = "On it."; return h; }
            if (Any(t, "meteor storm", "start a storm", "start the storm", "storm drill", "start an emergency", "simulate an emergency"))
            { h.Act = Act.Storm; h.Reply = "Storm incoming."; return h; }
            // Sound and the tour are whole commands: "a peaceful beach at sunset" and "a tour of Mars" are places.
            bool aPlace = Places.Find(t) != null || (BestBiome(t, out float placeScore) != null && placeScore >= 1f);
            // Sound: peace and quiet, mute, louder, softer.
            if (!aPlace && Any(t, "peace and quiet", "some peace", "i want peace", "i need peace", "i need some quiet", "some quiet", "i need a break", "calm it down", "calm everything down", "make it peaceful"))
            { h.Act = Act.Sound; h.Arg = "peace"; h.Reply = "Peace and quiet."; return h; }
            // "You mean for peace?", "peace please", "leave me in peace": the word itself, in a short sentence.
            if (!aPlace && t.Split(' ').Length <= 5 && (" " + t + " ").Contains(" peace "))
            { h.Act = Act.Sound; h.Arg = "peace"; h.Reply = "Peace and quiet."; return h; }
            if (!aPlace && Any(t, "unmute", "sound on", "turn the sound on", "turn on the sound", "sound back", "bring the sound back"))
            { h.Act = Act.Sound; h.Arg = "on"; h.Reply = "Sound's back."; return h; }
            if (!aPlace && Any(t, "mute", "mute it", "mute everything", "no sound", "sound off", "turn off the sound", "turn the sound off", "silence", "shh", "shush", "shhh", "be quiet", "hush"))
            { h.Act = Act.Sound; h.Arg = "mute"; h.Reply = "Muted. Say \"sound on\" to bring it back."; return h; }
            if (!aPlace && Any(t, "quieter", "turn it down", "too loud", "volume down", "lower the volume", "less noise", "not so loud", "softer please", "quiet please", "a bit quieter"))
            { h.Act = Act.Sound; h.Arg = "down"; h.Reply = "Quieter."; return h; }
            if (!aPlace && Any(t, "louder", "turn it up", "volume up", "too quiet", "raise the volume", "i can't hear", "i cant hear", "a bit louder"))
            { h.Act = Act.Sound; h.Arg = "up"; h.Reply = "Louder."; return h; }
            // A tour: Juniper takes you round the world's wonders while you sit.
            if (!aPlace && Any(t, "take me on a tour", "give me a tour", "show me around", "show me everything", "guided tour", "a tour", "tour please", "look around"))
            { h.Act = Act.Tour; h.Reply = "Let's look around."; return h; }
            // Music: our own tracks (no songs from outside, so no names or brands).
            if (Any(t, "stop the music", "music off", "turn off the music", "no music", "stop music", "turn the music off"))
            { h.Act = Act.MusicOff; h.Reply = "Music off."; return h; }
            if (Any(t, "play some music", "play music", "music please", "put on some music", "put some music on", "play a song", "play me a song",
                    "play something", "some music", "play a tune", "play some jazz", "play jazz", "sing") ||
                Regex.IsMatch(" " + t + " ", @" (?:play|put on|start|i want) (?:[a-z]+ ){0,3}(?:music|song|songs|tune|melody) "))
            { h.Act = Act.Music; h.Reply = "Here's a tune."; return h; }
            if (Any(t, "unfreeze", "resume", "continue", "carry on", "go on", "unpause"))
            { h.Act = Act.Resume; h.Reply = "Resuming."; return h; }
            if (Any(t, "freeze", "pause", "hold it"))
            { h.Act = Act.Freeze; h.Reply = "Frozen."; return h; }
            if (Any(t, "undo", "go back", "never mind", "nevermind", "cancel that", "take that back"))
            { h.Act = Act.Undo; h.Reply = "Undone."; return h; }
            var save = Regex.Match(t, @"\b(save|remember)\b( (this|it|that|this world|the world))?( as| called| named)? ?(.*)$");
            if (save.Success && (t.StartsWith(save.Groups[1].Value, StringComparison.Ordinal) || t.StartsWith("please " + save.Groups[1].Value, StringComparison.Ordinal)))
            {
                h.Act = Act.Save;
                h.SaveName = CleanTitle(Cap(save.Groups[5].Value.Trim()));
                h.Reply = "Saved.";
                return h;
            }
            if (Any(t, "help", "what can i say", "what can you do", "how does this work", "what can i do", "what do i do", "how do i play", "how do i move",
                    "i'm stuck", "im stuck", "i am stuck", "i don't know what to do", "i dont know what to do") ||
                t == "hello" || t == "hi" || t == "hey" || t == "hello juniper" || t == "hi juniper" || t == "hey juniper" || t == "is anyone there" || t == "what now")
            {
                h.Act = Act.Help;
                h.Reply = "Name a place, like \"a volcano\". Change it: \"rain\", \"at sunset\". Make things: \"make me a lion\". Menu, Ask Juniper, shows more.";
                return h;
            }
            if (Any(t, "surprise me", "anywhere", "somewhere new", "somewhere else", "you choose", "you pick", "random", "dealer's choice"))
            {
                var rng = new Rng(seed * 7919 + 13);
                string pick;
                do pick = Featured.Ids[rng.Range(0, Featured.Ids.Length)]; while (current != null && pick == current.Biome && Featured.Ids.Length > 1);
                h.Act = Act.Surprise;
                h.Result = Recipe.For(pick, seed);
                h.Reply = "How about " + Lower(h.Result.Def.Name) + "?";
                h.Changed = "biome";
                return h;
            }
            if (Any(t, "describe a place", "build a place", "let me describe", "i'll describe", "make a place", "create a place",
                    "new world please", "build a world", "make a world", "create a world", "design a place", "design a world"))
            { h.Act = Act.Describe; h.Reply = "Let's build it."; return h; }
            if (Any(t, "enter a code", "friend's code", "friends code", "share code", "type a code", "i have a code", "enter code"))
            { h.Act = Act.EnterCode; h.Reply = "Type it in."; return h; }
            if (Any(t, "last world", "where i was", "pick up where", "same as last time", "last time"))
            { h.Act = Act.LastWorld; h.Reply = "Back where you were."; return h; }
            string runName = RunName(t);
            if (runName.Length > 0 && BestBiome(runName, out _) == null && !Any(runName, "world", "new place", "a place"))
            { h.Act = Act.Run; h.SaveName = runName; h.Reply = "Running."; return h; }
            if (Any(t, "today's world", "todays world", "daily world", "world of the day", "today's place", "the daily"))
            {
                h.Act = Act.Daily;
                h.Reply = "Today's world.";
                h.Changed = "biome";
                return h;   // the caller fills Result with Recipe.Daily(today)
            }

            // A place, edits on top of it, or edits on the current world.
            // A composed place ("a volcano", "Mars") first: it is more particular than the kit world its words also name.
            var place = Places.Find(t);
            if (place != null && Places.IsAddition(t, place)) place = null;
            // A real place ("take me to India", "the pyramids at night"): from the atlas, at once and offline.
            if (place == null) place = Atlas.Find(t);
            float score = 0f;
            string biome = place != null ? place.Base : BestBiome(t, out score);
            // "Add flowers", "more snow", "make it snow": a change to where you are, not a trip to the kit world those
            // words also name. Only naming a place ("the meadow", "a snowy mountain", "take me somewhere with snow") moves you.
            if (place == null && biome != null && current != null && !Any(t, "take me", "go to", "let's go", "lets go", "somewhere", "new world", "a place", "i want to go"))
            {
                // "Add a river in this desert", "more trees here": about where you are, whatever place the words also name.
                bool addition = IsAdditionOf(t, biome) || Regex.IsMatch(" " + t + " ", @" (?:in|to|on|at|around) this | here $| this (?:place|world) ") && !Any(t, "into", "turn");
                if (addition || score < 1f)
                {
                    var probe = current.Copy();
                    var probeChanges = new List<string>();
                    ApplyEdits(t, probe, probeChanges, new List<string>());
                    // "Make it snow" where it already snows is still about here, not a trip.
                    bool phrased = t.StartsWith("make it ", StringComparison.Ordinal) || t.StartsWith("let it ", StringComparison.Ordinal);
                    if (addition || probeChanges.Count > 0 || phrased) biome = null;
                }
            }
            var changes = new List<string>();
            Recipe r;
            bool samePlace = place != null && current != null && current.Look != null && current.Look.Name == place.Look.Name;
            bool newWorld = place != null
                ? !samePlace || Any(t, "take me to", "go to", "let's go", "lets go", "new world", "i want")
                : biome != null && (current == null || biome != current.Biome || current.Look != null || Any(t, "take me to", "go to", "let's go", "lets go", "new world", "i want"));
            if (newWorld)
            {
                r = place != null ? place.Recipe(seed) : Recipe.For(biome, seed);
                changes.Add("biome");
            }
            else r = current != null ? current.Copy() : Recipe.For(Kit.Biomes[0].Id, seed);

            var said = new List<string>();
            ApplyEdits(t, r, changes, said);
            // Words we don't know with no place we know: "a medieval castle", "hogwarts at night". The AI
            // may know them; the edits we did understand stand if it can't be asked.
            h.AskAi = biome == null && UnknownWords(t) >= 1;
            // A world we know, with words we don't ("a sunken pirate ship"): open it now, ask for the rest.
            h.Refine = biome != null && UnknownWords(t) >= 1 && !(place != null && place.Id.StartsWith("atlas_", StringComparison.Ordinal));

            // Naming the world you're already in, with nothing to change, starts it fresh.
            if (!newWorld && biome != null && changes.Count == 0)
            {
                newWorld = true;
                r = place != null ? place.Recipe(seed + 1) : Recipe.For(biome, seed + 1);
                changes.Add("biome");
            }

            if (newWorld)
            {
                h.Act = Act.NewWorld;
                if (r.Look != null)
                {
                    r.Title = r.Look.Name;
                    h.Result = r.Clamp();
                    h.Reply = r.Look.Name + ".";
                }
                else
                {
                    r.Title = Cap(r.Describe(false));
                    h.Result = r.Clamp();
                    h.Reply = "Here's " + r.Describe(true) + ".";
                }
                h.Changed = string.Join(" ", changes);
                return h;
            }
            if (changes.Count > 0)
            {
                h.Act = Act.Edit;
                r.Title = Cap(r.Describe(false));
                h.Result = r.Clamp();
                h.Reply = Cap(string.Join(", ", said)) + ".";
                h.Changed = string.Join(" ", changes);
                return h;
            }

            // Nothing we know. A described place goes to the AI; anything else gets one question.
            h.Act = Act.Unclear;
            h.AskAi = ContentWords(t) >= 1;
            h.Question = current == null ? "Where to?" : "Where to next?";
            h.Choices = Suggest(current, seed);
            h.Reply = "I didn't catch a place.";
            return h;
        }


        static readonly (string heard, string meant)[] Misheard =
        {
            // Heard on the headset (Nov 12): "make me a lion" as "you me a lion", "make me a bee" as "you may be a bee".
            (" you me a ", " make me a "), (" you me an ", " make me an "), (" you may be a ", " make me a "), (" you may be an ", " make me an "),
            (" you make me a ", " make me a "), (" may me a ", " make me a "), (" make make me ", " make me "), (" you me ", " make me "),
            // "I want some piece" was "peace".
            (" some piece ", " some peace "), (" piece and quiet ", " peace and quiet "), (" a piece of quiet ", " peace and quiet "), (" in piece ", " in peace "),
            // Heard on the headset (Nov 12, evening): "you may a dog", "you made a lion".
            (" you may a ", " make me a "), (" you may an ", " make me an "), (" you made a ", " make me a "), (" you made me a ", " make me a "),
            // Nov 13: "pawn another person" was "spawn".
            (" pawn another ", " spawn another "), (" pawn a ", " spawn a "), (" pawn me ", " spawn me "),
            // Nov 13, afternoon: "spawn me a puppy" as "born me a puppy", "bone me a big...", "bonds some deers"; "T. rex" as "T. V. X.".
            (" born me ", " make me "), (" bone me ", " make me "), (" bond me ", " make me "), (" bonds some ", " make me some "), (" bond some ", " make me some "),
            (" spawn me ", " make me "), (" spawn some ", " make me some "), (" lawn may a ", " make me a "), (" born a ", " make me a "),
            (" t v x ", " t rex "), (" t v ex ", " t rex "), (" tv x ", " t rex "), (" tea rex ", " t rex "), (" deers ", " deer "),
            (" turn off the lights ", " darker "), (" lights off ", " darker "), (" turn the lights off ", " darker "),
            (" turn on the lights ", " brighter "), (" lights on ", " brighter "), (" turn the lights on ", " brighter "),
            // Nov 13, night: "take me to India" as "Mato India" and "Me to India"; "give me a tomato" as "Q-Mia, tomato".
            (" mato ", " take me to "), (" q mia ", " give me a "), (" qmia ", " give me a "), (" q mia a ", " give me a "),
            // Nov 14: "take me to Egypt" as "Like Meta, Egypt", "take me to Himalaya" as "Make me to humala ya".
            (" like meta ", " take me to "), (" like me to ", " take me to "), (" make me to ", " take me to "), (" take meta ", " take me to "),
            (" make meta ", " take me to "), (" humala ya ", " himalaya "), (" himala ya ", " himalaya "),
            // "A thunderstorm" as "Hande's storm", "Hand astrom", "Hindustrom" (the second made an astronaut).
            (" hande's storm ", " thunderstorm "), (" hande s storm ", " thunderstorm "), (" handes storm ", " thunderstorm "), (" hand astrom ", " thunderstorm "),
            (" hand a storm ", " thunderstorm "), (" hindustrom ", " thunderstorm "), (" hindu storm ", " thunderstorm "), (" under storm ", " thunderstorm "),
            (" thunder storm ", " thunderstorm "), (" hander storm ", " thunderstorm "),
        };

        // Sounds people make before the words ("uh, give me a puppy").
        static readonly Regex Fillers = new Regex(@"^(?:(?:uh|uhh|um|umm|er|erm|hmm|hm|oh|well|so|okay so|ok so|and|and then|now|then|also|alright|all right|okay|ok|right|hey) )+", RegexOptions.Compiled);
        // Words that start a sentence about doing, not a list of things: "ah, make it rain" is not "add make it rain".
        static readonly HashSet<string> NotThings = new HashSet<string>(
            ("make give get bring show put take can could would will let's lets let i it it's its go turn stop play start open we you " +
             "that this the what how why where who ok okay yes no so um uh please is are was were there here then well ember amber " +
             "never nevermind rewind undo help remove delete save quiet mute louder").Split(' '));

        /// <summary>A short list of things after a misheard "add": "a tree", "one person", "northern lights", "fireflies".</summary>
        static bool LooksLikeThings(string rest)
        {
            var w = rest.Trim().Split(' ');
            return w.Length >= 1 && w.Length <= 4 && w[0].Length > 0 && !NotThings.Contains(w[0]);
        }

        /// <summary>A word that names a real model ("dog", "puppy", "lions").</summary>
        static bool IsMadeWord(string w)
        {
            foreach (var m in MadeKinds.All)
                foreach (var x in m.Words)
                    if (w == x || w == x + "s" || w == x + "es") return true;
            return false;
        }

        static readonly string[] TravelStarts =
        {
            "take me to ", "take us to ", "go to ", "let's go to ", "lets go to ", "i want to go to ", "i want to see ", "i wanna go to ",
            "bring me to ", "fly me to ", "travel to ", "teleport me to ", "transport me to ", "send me to ", "can we go to ", "show me "
        };

        /// <summary>What comes after "take me to" ("india at night"), or "" when the words do not ask to go anywhere.</summary>
        public static string TravelRest(string text)
        {
            string t = FixHeard(Normalise(text ?? "")) + " ";
            foreach (var lead in new[] { "please ", "can you ", "could you " })
                if (t.StartsWith(lead, StringComparison.Ordinal)) t = t.Substring(lead.Length);
            foreach (var s in TravelStarts)
                if (t.StartsWith(s, StringComparison.Ordinal) && t.Length > s.Length + 1)
                {
                    // "Show me a lion", "show me around", "show me how": not a trip.
                    if (s == "show me " && (t.StartsWith("show me a ", StringComparison.Ordinal) || t.StartsWith("show me an ", StringComparison.Ordinal) ||
                                            t.StartsWith("show me around", StringComparison.Ordinal) || t.StartsWith("show me how", StringComparison.Ordinal) ||
                                            t.StartsWith("show me the way", StringComparison.Ordinal))) return "";
                    return t.Substring(s.Length).Trim();
                }
            return "";
        }

        /// <summary>The words ask to go somewhere ("take me to India", "let's go to Paris").</summary>
        public static bool IsTravel(string text)
        {
            string t = FixHeard(Normalise(text ?? "")) + " ";
            foreach (var lead in new[] { "please ", "can you ", "could you " })
                if (t.StartsWith(lead, StringComparison.Ordinal)) t = t.Substring(lead.Length);
            foreach (var s in TravelStarts)
                if (t.StartsWith(s, StringComparison.Ordinal) && t.Length > s.Length + 1) return !(s == "show me " && (t.StartsWith("show me a ", StringComparison.Ordinal) || t.StartsWith("show me an ", StringComparison.Ordinal) || t.StartsWith("show me around", StringComparison.Ordinal) || t.StartsWith("show me how", StringComparison.Ordinal) || t.StartsWith("show me the way", StringComparison.Ordinal)));
            return false;
        }

        /// <summary>The words the recogniser keeps getting wrong, put right (on normalised text).</summary>
        public static string FixHeard(string t)
        {
            if (string.IsNullOrEmpty(t)) return t ?? "";
            string p = " " + t + " ";
            foreach (var (heard, meant) in Misheard) p = p.Replace(heard, meant);
            p = " " + Fillers.Replace(p.Trim(), "") + " ";
            if (p.StartsWith(" you me ", StringComparison.Ordinal)) p = " make me " + p.Substring(8);
            // "Me to India": the "take" was cut off.
            if (p.StartsWith(" me to ", StringComparison.Ordinal) && p.Trim().Length > 6) p = " take me to " + p.Substring(7);
            // "Add" is the word heard worst (Nov 12): "had a tree", "I had one person", "ah, lights", "ride fireflies".
            var add = Regex.Match(p, @"^ (?:i had|had|ah|ride) (.+) $");
            // "Had a river in this desert": the things are "a river"; where they go is the rest.
            string addThings = add.Success ? Regex.Replace(add.Groups[1].Value, @" (?:in|to|on|at|around) (?:this|the|my) [a-z]+$| (?:in |over |right )?here$", "") : "";
            if (add.Success && LooksLikeThings(addThings)) p = " add " + add.Groups[1].Value + " ";
            else if (p.StartsWith(" ah ", StringComparison.Ordinal)) p = p.Substring(3);
            // "You dog", "you a lion": a real thing after "you" was "make me".
            var you = Regex.Match(p, @"^ you (?:may |make |me )?(?:a |an )?([a-z][a-z ]*?) $");
            if (you.Success && IsMadeWord(you.Groups[1].Value)) p = " make me a " + you.Groups[1].Value + " ";
            // "make me lion", "give me puppy", "add tree": the little word was lost, the wish wasn't.
            var mm = Regex.Match(p, @"^ (make me|give me|get me|bring me|show me|generate me|add) (?!a |an |some |the |two |three |four |five |six |[1-6] |it |that |this |more |less |my |your |another |lots |one |any )([a-z][a-z ]*) $");
            // "make me lion", "add tree" always; "give me puppy" only before a thing ("show me around" is a tour).
            if (mm.Success && (mm.Groups[1].Value == "make me" || mm.Groups[1].Value == "generate me" || mm.Groups[1].Value == "add" || Wishes.KindOf(mm.Groups[2].Value.Trim()).Length > 0))
            {
                string rest = mm.Groups[2].Value.Trim();
                // "add lanterns", "add fireflies" stay as said: the world's own lanterns and fireflies (a gift says this).
                bool many = mm.Groups[1].Value == "add" && rest.EndsWith("s", StringComparison.Ordinal) && !rest.EndsWith("ss", StringComparison.Ordinal);
                if (!many) p = " " + mm.Groups[1].Value + " a " + rest + " ";
            }
            return p.Trim();
        }

        /// <summary>The name after "open / load / play / start (my)": "" if the sentence isn't that.</summary>
        public static string RunName(string text)
        {
            var m = Regex.Match(Normalise(text), @"^(please )?(run|open|load|play|start) (my )?(.+)$");
            return m.Success ? m.Groups[4].Value.Trim() : "";
        }

        // ---------- worlds by their words ----------

        static readonly string[] AddWords = { "add", "more", "some", "with", "a bit of", "a little", "less", "fewer", "extra", "remove", "without" };

        /// <summary>True when every word of this kit world in the sentence is something being added or taken away ("add flowers", "no snow").</summary>
        static bool IsAdditionOf(string t, string biome)
        {
            var def = Kit.Get(biome);
            if (def == null) return false;
            string s = " " + t + " ";
            bool any = false;
            foreach (var w in def.Words)
            {
                int i = s.IndexOf(" " + w + " ", StringComparison.Ordinal);
                if (i < 0) continue;
                any = true;
                string before = s.Substring(0, i + 1);
                bool added = before.EndsWith(" no ", StringComparison.Ordinal);
                foreach (var a in AddWords) if (before.Contains(" " + a + " ")) { added = true; break; }
                if (!added) return false;
            }
            return any;
        }

        /// <summary>The kit world the words point at, or null. Longer phrases count more; weather words count less.</summary>
        public static string BestBiome(string text, out float best)
        {
            string t = " " + Normalise(text) + " ";
            best = 0f;
            string pick = null;
            bool tie = false;
            foreach (var b in Kit.Biomes)
            {
                float s = 0f;
                foreach (var w in b.Words)
                {
                    if (t.IndexOf(" " + w + " ", StringComparison.Ordinal) < 0) continue;
                    float weight = w.Contains(" ") ? 1.5f : 1f;
                    if (WeatherWords.Contains(w)) weight = 0.45f;
                    s += weight;
                }
                if (s > best + 1e-4f) { best = s; pick = b.Id; tie = false; }
                else if (s > 0f && Math.Abs(s - best) < 1e-4f) tie = true;
            }
            return tie && best < 1.5f ? null : pick;
        }

        static readonly HashSet<string> WeatherWords = new HashSet<string>
            { "snow", "snowy", "ice", "frozen", "winter", "rain city", "stars", "fire", "waves", "sand", "spring", "music" };

        static string[] Suggest(Recipe current, int seed)
        {
            var all = new List<string>(Kit.Ids);
            if (current != null) all.Remove(current.Biome);
            var rng = new Rng(seed * 31 + 5);
            var picks = new List<string>();
            while (picks.Count < 3 && all.Count > 0)
            {
                int i = rng.Range(0, all.Count);
                picks.Add(all[i]);
                all.RemoveAt(i);
            }
            return picks.ToArray();
        }

        // ---------- edits ----------

        static void ApplyEdits(string t, Recipe r, List<string> changes, List<string> said)
        {
            // Time: absolute words, clock times, then relative.
            float? hour = ClockHour(t);
            if (hour == null)
            {
                if (Any(t, "midnight")) hour = 0f;
                else if (Any(t, "dawn", "sunrise", "daybreak", "first light")) hour = 6.5f;
                else if (Any(t, "sunset", "dusk", "golden hour", "sundown")) hour = 18.5f;
                else if (Any(t, "evening", "twilight")) hour = 20f;
                else if (Any(t, "afternoon")) hour = 15.5f;
                else if (Any(t, "morning")) hour = 9f;
                else if (Any(t, "noon", "midday", "daytime", "daylight", "sunny", "in the day", "during the day")) hour = 12.5f;
                else if (Any(t, "night", "nighttime", "at night", "starry", "stars out", "under the stars", "starlight", "starlit")) hour = 22.5f;
                // "Make it dark": night, where you are (it went to Claude, who rebuilt the place without its landmark).
                else if (Any(t, "dark", "pitch black", "lights out") && !Any(t, "too dark", "so dark", "less dark", "not dark", "not so dark", "dark blue", "dark green", "dark red", "dark purple", "dark pink", "dark orange", "dark grey", "dark gray", "dark brown",
                                                                  "really dark", "very dark", "is dark", "it's dark", "its dark", "was dark", "dark colours", "dark colors")) hour = 22.5f;
            }
            if (hour != null && r.Def.HasTime)
            {
                r.Hour = hour.Value;
                changes.Add("hour");
                said.Add(Recipe.TimeWord(r.Hour));
            }
            if (Any(t, "later")) { r.Hour += 2f; changes.Add("hour"); said.Add("later"); }
            if (Any(t, "earlier")) { r.Hour -= 2f; changes.Add("hour"); said.Add("earlier"); }
            if (Any(t, "romantic", "date night"))
            { r.Hour = 18.5f; if (!r.Extras.Contains(Extras.Lanterns)) r.Extras.Add(Extras.Lanterns); changes.Add("hour"); said.Add("romantic"); }
            if (Any(t, "scary", "creepy", "haunted"))
            { r.Hour = 23f; r.Fog = Math.Max(r.Fog, 0.7f); changes.Add("hour"); said.Add("spooky"); }
            if (Any(t, "magical", "magic", "enchanted", "dreamy"))
            { if (!r.Extras.Contains(Extras.Fireflies)) r.Extras.Add(Extras.Fireflies); r.Light -= 0.15f; changes.Add("extras"); said.Add("magical"); }
            if (Any(t, "relaxing", "relax", "soothing"))
            { r.Wind = Math.Min(r.Wind, 0.1f); r.Fog = Math.Min(r.Fog, 0.2f); changes.Add("wind"); said.Add("calm"); }

            if (Any(t, "darker", "dimmer", "too bright", "more dark", "less light"))
            { r.Light -= 0.35f; changes.Add("light"); said.Add("darker"); }
            if (Any(t, "brighter", "lighter", "too dark", "more light"))
            { r.Light += 0.35f; changes.Add("light"); said.Add("brighter"); }

            // Weather. "No rain" and "stop the rain" clear it.
            bool clear = Any(t, "clear sky", "clear skies", "clear it up", "stop the rain", "no rain", "turn off the rain", "turn the rain off", "rain off", "make it stop raining", "turn off the snow", "less rain", "stop raining", "dry", "stop the snow", "no snow", "clear weather");
            if (clear)
            {
                if (r.Precip != Precip.Bubbles) r.Precip = Precip.None;
                r.Fog = Math.Min(r.Fog, 0.1f);
                changes.Add("precip"); said.Add("clear");
            }
            else if (r.Biome == "space" && Any(t, "rain", "raining", "rainy", "drizzle", "showers", "snow", "snowing", "let it snow", "thunderstorm", "stormy", "thunder", "lightning", "downpour"))
            {
                // No weather in orbit: the nearest thing is a meteor shower.
                if (!r.Extras.Contains(Extras.ShootingStars)) r.Extras.Add(Extras.ShootingStars);
                changes.Add("extras"); said.Add("no rain in space, so a meteor shower");
            }
            else
            {
                if (Any(t, "thunderstorm", "storm", "stormy", "thunder", "lightning", "downpour"))
                { r.Precip = Precip.Rain; r.Wind = Math.Max(r.Wind, 0.85f); changes.Add("precip"); said.Add("a storm"); }
                else if (Any(t, "rain", "raining", "rainy", "drizzle", "showers"))
                { r.Precip = Precip.Rain; changes.Add("precip"); said.Add("rain"); }
                if (Any(t, "snowing", "snowfall", "let it snow", "snowy", "snow") && r.Biome != "snow" || Any(t, "more snow", "snowing", "let it snow"))
                { r.Precip = Precip.Snow; changes.Add("precip"); if (!said.Contains("snow")) said.Add("snow"); }
            }
            if (Any(t, "no fog", "less fog", "clear the fog", "lift the fog"))
            { r.Fog = Math.Max(0f, r.Fog - 0.4f); changes.Add("fog"); said.Add("less fog"); }
            else if (Any(t, "fog", "foggy", "mist", "misty", "haze", "hazy", "mysterious", "spooky", "eerie"))
            { r.Fog = Math.Max(r.Fog, 0.7f); changes.Add("fog"); said.Add("fog"); }

            if (Any(t, "windy", "breezy", "gusty", "wind", "breeze"))
            { r.Wind = Math.Max(r.Wind, 0.75f); changes.Add("wind"); said.Add("wind"); }
            if (Any(t, "calm", "still", "no wind", "peaceful", "quiet"))
            { r.Wind = Math.Min(r.Wind, 0.1f); changes.Add("wind"); said.Add("calm"); }

            if (Any(t, "more trees", "more plants", "denser", "lush", "lusher", "fuller", "busier", "overgrown", "thick", "more stuff"))
            { r.Density += 0.35f; changes.Add("density"); said.Add("fuller"); }
            if (Any(t, "fewer", "less trees", "sparse", "emptier", "empty", "wide open", "open space", "barren", "minimal", "less stuff"))
            { r.Density -= 0.35f; changes.Add("density"); said.Add("emptier"); }

            if (Any(t, "warmer", "warm", "cozy", "cosy", "cozier", "golden", "toasty"))
            { r.Warmth += 0.4f; changes.Add("warmth"); said.Add("warmer"); }
            if (Any(t, "colder", "cooler", "chilly", "icy", "frosty", "cold"))
            { r.Warmth -= 0.4f; changes.Add("warmth"); said.Add("colder"); }

            ExtraEdit(t, r, Extras.Aurora, changes, said, "aurora", "northern lights", "southern lights", "auroras");
            ExtraEdit(t, r, Extras.Fireflies, changes, said, "fireflies", "firefly", "glow bugs");
            ExtraEdit(t, r, Extras.ShootingStars, changes, said, "shooting stars", "shooting star", "meteors", "meteor shower", "falling stars");
            ExtraEdit(t, r, Extras.Lanterns, changes, said, "lanterns", "lantern", "fairy lights", "string lights", "candles");
            ExtraEdit(t, r, Extras.Flowers, changes, said, "flowers", "flower", "blossoms", "blossom");
            ExtraEdit(t, r, Extras.Crystals, changes, said, "crystals", "crystal", "gems", "gemstones");
            ExtraEdit(t, r, Extras.Clouds, changes, said, "clouds", "cloudy", "overcast");
            ExtraEdit(t, r, Extras.Embers, changes, said, "embers", "sparks");
        }

        static void ExtraEdit(string t, Recipe r, string extra, List<string> changes, List<string> said, params string[] words)
        {
            string hit = null;
            foreach (var w in words) if (Has(t, w)) { hit = w; break; }
            if (hit == null) return;
            bool remove = false;
            foreach (var neg in new[] { "no ", "without ", "remove the ", "remove ", "stop the ", "get rid of the ", "get rid of ", "lose the ", "fewer ", "less " })
                if (t.Contains(neg + hit)) remove = true;
            if (remove)
            {
                if (r.Extras.Remove(extra)) { changes.Add("extras"); said.Add("no " + Extras.Say(extra)); }
                return;
            }
            if (!r.Extras.Contains(extra))
            {
                r.Extras.Add(extra);
                changes.Add("extras");
                said.Add(Extras.Say(extra));
            }
            else
            {
                // Asked for what is already there: say so, rather than not understanding.
                changes.Add("extras");
                said.Add(Extras.Say(extra) + " already here");
            }
        }

        static readonly Dictionary<string, int> Numbers = new Dictionary<string, int>
        {
            { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 },
            { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 }
        };

        /// <summary>"2 am", "2 a.m.", "two in the morning", "10 pm", "7 o'clock in the evening" → hour.</summary>
        public static float? ClockHour(string text)
        {
            string t = Normalise(text);
            var m = Regex.Match(t, @"\b(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?::(\d\d))?( ?o'clock)? ?(am|pm|in the morning|in the afternoon|in the evening|at night)?\b");
            while (m.Success && !m.Groups[3].Success && !m.Groups[4].Success) m = m.NextMatch();
            if (!m.Success) return null;
            string n = m.Groups[1].Value;
            int h = char.IsDigit(n[0]) ? int.Parse(n, CultureInfo.InvariantCulture) : Numbers[n];
            if (h < 1 || h > 12) return h == 0 ? 0f : (float?)null;
            float min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 60f : 0f;
            string q = m.Groups[4].Success ? m.Groups[4].Value : "";
            bool pm = q == "pm" || q == "in the afternoon" || q == "in the evening" || (q == "at night" && h >= 6 && h != 12);
            bool am = q == "am" || q == "in the morning" || (q == "at night" && (h < 6 || h == 12));
            if (q.Length == 0) pm = h < 7;   // "seven o'clock" alone: evening for 1–6, morning for 7–12
            int hour24 = h % 12 + (pm ? 12 : 0);
            if (am && h == 12) hour24 = 0;
            return hour24 + min;
        }

        // ---------- text helpers ----------

        public static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = text.ToLowerInvariant();
            t = t.Replace("a.m.", "am").Replace("p.m.", "pm").Replace("a. m.", "am").Replace("p. m.", "pm");
            t = t.Replace('’', '\'');
            var sb = new StringBuilder(t.Length);
            foreach (char c in t)
                sb.Append(char.IsLetterOrDigit(c) || c == '\'' || c == ':' ? c : ' ');
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        static HashSet<string> softVocab;
        // Everyday words that are one letter from a command word and must never be "corrected" into it.
        static readonly HashSet<string> NeverSoften = new HashSet<string>(
            ("think thing there where these those would could should about place right great other after again never every world really little " +
             "store stores still while which their might light night sight tight fight eight being going doing having white write wrong along among " +
             "please thanks thank hello juniper ember amber start stop make made take give gave want need look looks like liked here bring").Split(' '));

        /// <summary>
        /// The on-device recogniser mishears now and then ("rian", "fireflys", "rewined"). A word of five letters or
        /// more that is one letter away from exactly one word we know (two letters for long words) becomes that word.
        /// </summary>
        public static string Soften(string t)
        {
            if (string.IsNullOrEmpty(t)) return t ?? "";
            if (softVocab == null)
            {
                var v = new HashSet<string>();
                foreach (var w in EditVocab) if (w.Length >= 4) v.Add(w);
                foreach (var b in Kit.Biomes) foreach (var w in b.Words) foreach (var part in w.Split(' ')) if (part.Length >= 4) v.Add(part);
                foreach (var w in ("rewind freeze pause resume unfreeze undo surprise music storm meteor shields engines evade repair explore worlds " +
                                   "darker brighter warmer colder lanterns fireflies aurora northern campfire beach desert meadow space").Split(' ')) v.Add(w);
                softVocab = v;
            }
            var words = t.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length < 4 || softVocab.Contains(w) || NeverSoften.Contains(w) || Filler.Contains(w) || char.IsDigit(w[0])) continue;
                int max = w.Length >= 8 ? 2 : 1;
                string pick = null;
                int best = max + 1, ties = 0;
                foreach (var k in softVocab)
                {
                    if (Math.Abs(k.Length - w.Length) > max) continue;
                    if (w.Length == 4 && !Swapped(w, k)) continue;
                    int d = Distance(w, k, max);
                    if (d < best) { best = d; pick = k; ties = 1; }
                    else if (d == best && d <= max)
                    {
                        if (k.Length > pick.Length) { pick = k; ties = 1; }
                        else if (k.Length == pick.Length) ties++;
                    }
                }
                if (pick != null && best <= max && ties == 1) words[i] = pick;
            }
            return string.Join(" ", words);
        }

        /// <summary>The same word with two neighbouring letters swapped ("rian" for "rain").</summary>
        static bool Swapped(string a, string b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                return a[i] == b[i + 1] && a[i + 1] == b[i] && string.CompareOrdinal(a, i + 2, b, i + 2, a.Length) == 0;
            }
            return false;
        }

        /// <summary>Edit distance (a swap of two neighbours counts as one), giving up (max + 1) once it is past <paramref name="max"/>.</summary>
        static int Distance(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                int rowMin = d[i, 0];
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max + 1) return max + 1;
            }
            return Math.Min(d[n, m], max + 1);
        }

        static bool Has(string t, string phrase) => (" " + t + " ").Contains(" " + phrase + " ");

        static bool Any(string t, params string[] phrases)
        {
            foreach (var p in phrases) if (Has(t, p)) return true;
            return false;
        }

        static readonly HashSet<string> Filler = new HashSet<string>
        {
            "a", "an", "the", "to", "me", "take", "go", "let's", "lets", "i", "want", "would", "like", "please", "can", "you",
            "make", "it", "build", "create", "show", "put", "in", "at", "on", "of", "with", "and", "some", "my", "room", "into",
            "turn", "something", "somewhere", "place", "world", "run", "start", "begin", "hey", "okay",
            "ok", "um", "uh", "so", "just", "be", "is", "keep", "get", "where", "there", "here", "this", "that", "now", "new", "very"
        };

        static int ContentWords(string t)
        {
            int n = 0;
            foreach (var w in t.Split(' ')) if (w.Length > 2 && !Filler.Contains(w)) n++;
            return n;
        }

        // Single words the edits understand (so they don't count as an unknown place).
        static readonly HashSet<string> EditVocab = new HashSet<string>(
            ("night nighttime midnight dawn sunrise daybreak first sunset dusk golden hour sundown evening twilight afternoon " +
             "morning noon midday daytime daylight sunny day during starry stars under out later earlier darker dimmer bright " +
             "brighter lighter dark too light clear skies sky stop please rain raining rainy drizzle showers storm stormy thunder " +
             "thunderstorm lightning downpour snow snowing snowy snowfall let fog foggy mist misty haze hazy mysterious spooky " +
             "eerie lift windy breezy gusty wind breeze calm still peaceful quiet more trees plants denser lush lusher fuller " +
             "busier overgrown thick stuff fewer less sparse emptier empty open barren minimal warmer warm cozy cosy cozier " +
             "toasty colder cooler chilly icy frosty cold aurora auroras northern southern lights fireflies firefly glow bugs " +
             "shooting star meteors meteor shower falling lanterns lantern fairy string candles flowers flower blossoms blossom " +
             "crystals crystal gems gemstones clouds cloudy overcast embers sparks add remove without rid lose get o'clock " +
             "clock one two three four five six seven eight nine ten eleven twelve weather dry " +
             "romantic date scary creepy haunted magical magic enchanted dreamy relaxing relax soothing").Split(' '));

        static HashSet<string> biomeVocab;

        /// <summary>Content words that are neither filler, edits, nor any world's words.</summary>
        static HashSet<string> placeVocab;
        static HashSet<string> PlaceVocab
        {
            get
            {
                if (placeVocab != null) return placeVocab;
                var v = new HashSet<string>();
                foreach (var p in Places.All) foreach (var w in p.Words) foreach (var part in Normalise(w).Split(' ')) v.Add(part);
                foreach (var s in Atlas.All) foreach (var w in s.Words) foreach (var part in w.Split(' ')) v.Add(part);
                foreach (var w in new[] { "see", "visit", "travel", "fly", "teleport", "transport", "send", "we", "can" }) v.Add(w);
                foreach (var w in new[] { "take", "turn", "into", "put", "make", "go", "let's", "lets", "want", "show", "bring", "world", "place" }) v.Add(w);
                return placeVocab = v;
            }
        }

        internal static int UnknownWords(string t)
        {
            if (biomeVocab == null)
            {
                var v = new HashSet<string>();
                foreach (var b in Kit.Biomes) foreach (var w in b.Words) foreach (var part in w.Split(' ')) v.Add(part);
                biomeVocab = v;
            }
            int n = 0;
            foreach (var w in t.Split(' '))
            {
                if (w.Length <= 2 || Filler.Contains(w) || EditVocab.Contains(w) || biomeVocab.Contains(w) || PlaceVocab.Contains(w)) continue;
                if (char.IsDigit(w[0])) continue;
                n++;
            }
            return n;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ---------- titles shown to people ----------

        public const int MaxTitle = 40;

        // A short list: titles come from our own templates, the AI (told to stay family friendly) and
        // "save this as …". Anything that matches is dropped and the world's own name is used.
        static readonly string[] Blocked =
        {
            "fuck", "shit", "bitch", "cunt", "pussy", "asshole", "bastard", "whore", "slut", "nigg",
            "faggot", "retard", "porn", "nazi", "suicide", "penis", "vagina", "boobs"
        };

        // Short words that hide inside innocent ones ("grape", "titse"): blocked only as whole words.
        static readonly string[] BlockedWords = { "rape", "raped", "rapist", "tits", "tit", "cum", "sex", "sexy", "kill", "killing" };

        static readonly string[] QuestionStarts =
        {
            "how ", "what ", "what's ", "whats ", "why ", "when ", "where ", "who ", "which ", "is it ", "is this ", "is that ",
            "are they ", "can it ", "could it ", "does it ", "do they ", "did it ", "did they ", "would it ", "will it ",
            "tell me about ", "tell me more", "do you ", "did you ", "have you ", "are you ", "were you "
        };

        /// <summary>A question ("how fast could it run?", "what does it eat"), as heard or typed.</summary>
        public static bool IsQuestion(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.TrimEnd().EndsWith("?", StringComparison.Ordinal)) return true;
            string t = Normalise(text) + " ";
            foreach (var q in QuestionStarts) if (t.StartsWith(q, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>False if the text contains anything on the blocked list (spaces ignored).</summary>
        public static bool IsClean(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            string low = s.ToLowerInvariant();
            string joined = low.Replace(" ", "");
            foreach (var b in Blocked) if (joined.Contains(b)) return false;
            foreach (var w in Normalise(low).Split(' '))
                foreach (var b in BlockedWords) if (w == b) return false;
            return true;
        }

        /// <summary>
        /// A safe display title: letters, digits, spaces and light punctuation, one line, at most 40 characters.
        /// Empty if it contained anything blocked (the caller then uses the world's own name).
        /// </summary>
        public static string CleanTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '\'' || c == '-' || c == ',' || c == '.' || c == '&' || c == ':' || c == '!')
                    sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append(' ');
            }
            string t = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (!IsClean(t)) return "";
            if (t.Length > MaxTitle) t = t.Substring(0, MaxTitle).TrimEnd(' ', ',', '-');
            return t;
        }
    }
}

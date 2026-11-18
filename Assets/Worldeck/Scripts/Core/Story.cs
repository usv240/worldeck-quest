using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>How the meteors come: the player picks it, through Mara ("fast, through the middle" or "slow, around the edge").</summary>
    public struct Pace
    {
        public int Count;
        public float GapMin, GapMax;
        /// <summary>Extra seconds before each of the first three meteors, so the first ones teach.</summary>
        public float SlowStart;
        public string Name;

        public static readonly Pace Fast = new Pace { Count = 7, GapMin = 2.4f, GapMax = 3.4f, SlowStart = 0.8f, Name = "fast" };
        public static readonly Pace Slow = new Pace { Count = 10, GapMin = 3.4f, GapMax = 4.8f, SlowStart = 1.6f, Name = "slow" };
        public static readonly Pace Standard = new Pace { Count = 9, GapMin = 2.6f, GapMax = 4.6f, SlowStart = 0f, Name = "standard" };
    }

    /// <summary>One line from Mara, and how long it stays.</summary>
    public struct Line
    {
        public string Text;
        public float Seconds;
        public Line(string text, float seconds = 0f) { Text = text; Seconds = seconds > 0f ? seconds : Math.Max(3.2f, 1.2f + text.Length * 0.055f); }
    }

    /// <summary>A choice the player can touch (or say).</summary>
    public struct Choice
    {
        public string Id, Label;
        public string[] Said;   // words that mean this choice when spoken
        public Choice(string id, string label, params string[] said) { Id = id; Label = label; Said = said; }
    }

    /// <summary>How a run of the storm ended for Mara.</summary>
    public enum Landing { None, Clean, Rough, Lost }

    /// <summary>
    /// "The Last Signal": a pilot, Mara, is flying through a meteor field and your room is the only beacon she
    /// can see. Three chapters, one per run, each a few minutes: guide her down, see her off, bring her home.
    /// Your real furniture is her signal: every piece the storm breaks, her voice breaks up. All of it is
    /// written here, with no engine, so every line and choice is tested.
    /// </summary>
    public static class Story
    {
        public const int Chapters = 3;
        public const string Pilot = "Mara";

        public static string Title(int chapter) => chapter switch
        {
            0 => "Mara's Call",
            1 => "The Way Back Up",
            2 => "Spring Delivery",
            _ => "One More Landing"
        };

        /// <summary>
        /// What she says when the call comes through, before the first choice. From the second chapter she remembers
        /// the last one: <paramref name="last"/> (how she landed) and <paramref name="held"/> (a piece of your
        /// furniture that never went dark, "sofa", or "").
        /// </summary>
        public static Line[] Call(int chapter, Landing last = Landing.None, string held = "")
        {
            switch (chapter)
            {
                case 0:
                    return new[]
                    {
                        new Line("Hello? Is anyone there?\nThis is Mara. I'm a courier pilot."),
                        new Line("A meteor field took out my navigation.\nYour room is the only light I can see."),
                        new Line(Stakes),
                    };
                case 1:
                    return new[]
                    {
                        last == Landing.Rough
                            ? new Line("It's me, Mara! Hull's still cracked\nfrom that landing, but I'm flying.")
                            : new Line("It's me, Mara! Ship's patched up.\nThanks to you, I'm still breathing."),
                        new Line("I have to climb back out\nthrough the same meteor field."),
                    };
                case 2:
                    var lines = new List<Line> { new Line("Hey, beacon! Mara again.\nI made my delivery run... and came back.") };
                    if (Named(held)) lines.Add(new Line(Remember(held)));
                    lines.Add(new Line("I'm carrying seeds for a world\nthat lost its spring. Meteors on my tail."));
                    return lines.ToArray();
                default:
                    return new[] { new Line("Mara here. Another run, another storm.\nGot room for one more landing?") };
            }
        }

        /// <summary>What is at stake, said in her first call.</summary>
        public const string Stakes = "One day of air left. Medicine for\na colony on board. Please, help me.";

        /// <summary>A piece of furniture she can name ("sofa"; not "furniture" or nothing).</summary>
        static bool Named(string piece) => !string.IsNullOrEmpty(piece) && piece != "furniture" && Array.IndexOf(Pieces, piece) >= 0;

        /// <summary>The piece that held last time, remembered.</summary>
        public static string Remember(string piece) => "I still think about your " + piece + ".\nIt never went dark for me.";

        /// <summary>When the big one's ring appears.</summary>
        public const string BigWarning = "A big one! Both palms on it,\nor hold one there, steady!";
        /// <summary>The big one is aimed at the piece you lit again (her last chapter).</summary>
        public static string BigAgain(string piece) => "Not your " + piece + " again!\nBoth palms on it, or hold one steady!";
        /// <summary>After the storm her ship comes in to your hand.</summary>
        public const string PalmAsk = "Hold out your hand, palm up.\nI'm coming in to land!";
        public const string PalmLanded = "Right in your hand!\nSoftest landing I've ever had.";

        /// <summary>A piece of your furniture still dark from her last storm, as she calls.</summary>
        public static string StillDark(string piece) => "Your " + piece + " is still dark from last time.\nI'll steer by the rest.";
        /// <summary>The same piece, lit again by the world's task.</summary>
        public static string Relit(string piece) => "Your " + piece + " is lit again!\nI can see it from up here.";
        /// <summary>Juniper, as the task begins (added to the task's own line).</summary>
        public static string DarkTask(string piece) => "\nIt lights your " + piece + " again for Mara.";
        /// <summary>Juniper, when the task is done and the piece is lit (she then waits on the radio).</summary>
        public static string RelitLine(string piece, bool sheWaits = true) => sheWaits
            ? "Your " + piece + " is lit again. Mara can see it.\nAnswer her: the bright button on your table."
            : "Your " + piece + " is lit again.\nMara will see it on her next flight.";
        /// <summary>Juniper, after a chapter whose storm left a piece dark.</summary>
        public static string ChapterDark(string piece) => "Your " + piece + " went dark in the storm.\nYour next world can light it again.";

        /// <summary>The chapter's title card, shown as she calls ("CHAPTER 1 OF 3" and its name).</summary>
        public static string TitleCard(int chapter) => chapter < Chapters
            ? "CHAPTER " + (chapter + 1) + " OF " + Chapters + "\n" + Title(chapter)
            : Title(chapter).ToUpperInvariant();

        /// <summary>
        /// After her keepsake, the question the story keeps open: why only your room's light reaches her. The
        /// last chapter answers it.
        /// </summary>
        public static Line[] Hook(int chapter) => chapter switch
        {
            0 => new[] { new Line("One thing I can't work out:\nwhy is your room the only light I see?") },
            1 => new[] { new Line("Still can't work out why only your\nlight reaches me. I'm getting close.") },
            2 => new[]
            {
                new Line("I found out why only your light\nreaches me. Old pilots have a saying:"),
                new Line("Every courier gets one beacon in life.\nA room that keeps a light on. You're mine."),
            },
            _ => new Line[0],
        };

        /// <summary>Juniper's words back in your room when her story is over (not spoken by her: no recording).</summary>
        /// <summary>
        /// Juniper, after the story's last words at home: what happened to your room, from what really happened
        /// ("" when nothing went dark). Not spoken by her: no recording.
        /// </summary>
        public static string RoomStory(string brokeFirst, string relit, bool relitHeldLast)
        {
            if (!Named(brokeFirst) && !Named(relit)) return "";
            var lines = new List<string> { "Your room's story:" };
            if (Named(brokeFirst)) lines.Add("In her first storm, your " + brokeFirst + " went dark.");
            if (Named(relit)) lines.Add(relit == brokeFirst ? "Then you lit it again." : Named(brokeFirst) ? "Then you lit your " + relit + " again." : "You lit your " + relit + " again.");
            if (Named(relit) && relitHeldLast) lines.Add("In her last storm, it held.");
            return string.Join("\n", lines);
        }

        public static string StoryEnd(string held) => "Mara's story: three chapters,\nthree landings. " +
            (Named(held) ? "Your " + held + " never went dark." : "Your light never went dark.") + "\nShe'll still fly by, every day.";

        /// <summary>Juniper's welcome on first launch (each line within her caption's 34 characters).</summary>
        public const string Welcome = "Welcome to Worldeck!\nA lost pilot sees only your room.";

        /// <summary>Before anything flashes, on first launch: the storms are bright; the player chooses how bright.</summary>
        public const string LightNotice = "Heads up: storms here flash.\nPick how bright you want them.\nChange it any time in the menu.";

        /// <summary>The faint call heard while the player is still building: the story starts in the first minute.</summary>
        public const string Faint = "...anyone... there...?\n...I need... a light...";

        /// <summary>The first choice: answer her (and, in the first chapter, ask who she is).</summary>
        public static Choice[] Answer(int chapter, bool askedWho) => chapter == 0 && !askedWho
            ? new[] { new Choice("m:guide", "I'll guide you", "guide", "help", "yes", "okay", "ok", "sure", "i will", "i'll", "land"), new Choice("m:who", "Who are you?", "who", "where", "what"), Later }
            : new[] { new Choice("m:guide", chapter == 1 ? "I'll light the way" : "I've got you", "guide", "help", "yes", "okay", "ok", "sure", "go", "light", "got"), Later };

        /// <summary>Not now: she waits, the world is yours, the button on your table answers her.</summary>
        public static readonly Choice Later = new Choice("m:later", "Call me later", "later", "not now", "wait", "busy", "in a bit", "explore first");

        /// <summary>What she says when asked to wait.</summary>
        public static readonly Line LaterLine = new Line("Okay. I'll keep my radio on.\nCall me when you're ready, beacon.");

        public static Line[] Who() => new[]
        {
            new Line("Just a courier. Ten years flying,\nand I've never been this lost."),
            new Line("I just need someone to keep a light on.\nPlease."),
        };

        /// <summary>The choice that matters: how she flies through. From the second chapter she remembers how you
        /// chose last time (<paramref name="lastPace"/>: "m:fast", "m:slow" or "").</summary>
        public static Line AskPace(int chapter, string lastPace = "") => chapter switch
        {
            1 when lastPace == "m:slow" => new Line("Last time we took it slow.\nStraight up, or the long way again?"),
            1 when lastPace == "m:fast" => new Line("Last time we came in fast.\nStraight up again, or the long way?"),
            2 when lastPace == "m:slow" => new Line("You like it slow, I remember.\nDive fast, or circle in slow?"),
            2 when lastPace == "m:fast" => new Line("You like it fast, I remember.\nDive fast, or circle in slow?"),
            1 => new Line("Straight up is quick but rough.\nThe long way is calmer. Your call."),
            2 => new Line("I can dive fast, or circle in slow.\nYou pick, beacon."),
            _ => new Line("I can come in fast, through the middle,\nor slow, around the edge. Your call."),
        };

        public static Choice[] Paces(int chapter) => new[]
        {
            new Choice("m:fast", chapter == 1 ? "Straight up, fast" : "Come in fast", "fast", "quick", "quickly", "middle", "straight", "dive", "hurry"),
            new Choice("m:slow", chapter == 1 ? "The long way" : "Take it slow", "slow", "slowly", "edge", "long", "careful", "carefully", "safe", "circle", "around"),
        };

        public static Pace PaceFor(string id) => id == "m:fast" ? Pace.Fast : Pace.Slow;

        /// <summary>What she says to the choice, as the storm starts.</summary>
        public static Line Go(string paceId, bool scanned) => paceId == "m:fast"
            ? new Line(scanned ? "Fast it is! Keep your lights on.\nI'm steering by your furniture." : "Fast it is! Keep that light on.\nI'm steering by you.", 4.5f)
            : new Line(scanned ? "Slow and steady. Your furniture is my\nrunway lights. Don't let them go dark." : "Slow and steady. You're my runway light.\nDon't let it go dark.", 5f);

        // ---------- during the storm ----------

        static readonly string[] Cheers = { "Signal's clear!", "Nice block!", "I see you, beacon!", "That one missed me!", "Keep it up!", "Clean!" };
        public static string Cheer(int n) => Cheers[Math.Abs(n) % Cheers.Length];

        /// <summary>Her line when a piece goes dark: the real thing it is, in her words, broken up by static.</summary>
        public static string Hurt(string piece, float signal)
        {
            string what = string.IsNullOrEmpty(piece) || piece == "furniture" ? "light" : piece;
            return Garble(HurtSpoken(piece, signal), 1f - signal, what.Length + 7);
        }

        /// <summary>The same line as she says it (the static is in her voice, not in the words).</summary>
        public static string HurtSpoken(string piece, float signal)
        {
            string what = string.IsNullOrEmpty(piece) || piece == "furniture" ? "light" : piece;
            return signal > 0.5f ? "Your " + what + " went dark! Losing you a bit..." : "Losing... your " + what + "... can't see...";
        }

        /// <summary>Static over her words: some letters lost, more as the signal fades. Deterministic for a given text.</summary>
        public static string Garble(string s, float amount, int seed = 1)
        {
            if (string.IsNullOrEmpty(s) || amount <= 0.15f) return s ?? "";
            var c = s.ToCharArray();
            var rng = new Rng(seed * 31 + s.Length);
            float p = Math.Min(0.45f, amount * 0.5f);
            for (int i = 0; i < c.Length; i++)
                if (char.IsLetter(c[i]) && rng.Next01() < p) c[i] = rng.Next01() < 0.5f ? '-' : '.';
            return new string(c);
        }

        /// <summary>The first piece that breaks in a run: what the player is looking at, said plainly (no static).</summary>
        public static string FirstBreak(string piece) => "That's your real " + (string.IsNullOrEmpty(piece) || piece == "furniture" ? "room" : piece) + " showing!\nMy light on it is out. Block the rest!";

        public static string Halfway(int chapter) => chapter == 1 ? "Halfway up! Hold them off!" : chapter == 2 ? "I can see your table from here!" : "I can see your lights! Almost there!";

        // ---------- the ending ----------

        public static Landing LandingFor(bool roomLost, int taken) => roomLost ? Landing.Lost : taken <= 2 ? Landing.Clean : Landing.Rough;

        /// <summary>What she says when the storm is over. <paramref name="safe"/> names the pieces that held ("sofa and shelf").</summary>
        public static Line[] End(int chapter, Landing l, string safe, int blocked)
        {
            string held = string.IsNullOrEmpty(safe) ? "" : "\nYour " + safe + " kept me on course.";
            switch (l)
            {
                case Landing.Lost:
                    return new[] { new Line("Signal... lost...", 3f) };
                case Landing.Rough:
                    return chapter == 1
                        ? new[] { new Line("I'm through... barely. Hull's cracked.\nBut I'm out. I'm alive." + held) }
                        : new[] { new Line("I'm down... hard. Engines are smoking.\nBut I'm alive." + held) };
                default:
                    return chapter switch
                    {
                        0 => new[] { new Line("Touchdown! I'm down, I'm safe!\nYou blocked " + blocked + " of them." + held), new Line("The medicine gets through, because of you.\nI won't forget this room.") },
                        1 => new[] { new Line("I'm out of the field! Clear sky!" + held), new Line("Going to deliver these seeds.\nKeep that light on for me, okay?") },
                        2 => new[] { new Line("Landed! And the seeds are safe!" + held), new Line("Spring is coming back to that world.\nAnd you were part of it.") },
                        _ => new[] { new Line("Down safe. You're the best beacon\nin the sky." + held) },
                    };
            }
        }

        /// <summary>After a safe landing: a last word to her (the chapter's goodbye).</summary>
        public static Choice[] Farewell(int chapter) => chapter == 2
            ? new[] { new Choice("m:wave", "Wave goodbye", "bye", "goodbye", "wave", "see you"), new Choice("m:stay", "Visit again?", "visit", "again", "come back", "stay") }
            : new[] { new Choice("m:bye", chapter == 0 ? "Safe flight, Mara" : "Come back soon", "safe", "bye", "goodbye", "soon", "see you", "flight"), new Choice("m:wave", "Wave to her", "wave", "hello", "hi") };

        public static Line Reply(int chapter, string id) => id switch
        {
            "m:wave" => new Line("Ha! I see you waving!\nWaving back, beacon!"),
            "m:stay" => new Line("Every spring. I promise.\nLeave the light on."),
            _ => new Line(chapter == 0 ? "Thank you. Really.\nI'll call again when I'm patched up." : "Count on it.\nMara out."),
        };

        /// <summary>What she leaves you after a chapter she survives (kept on your real table).</summary>
        public static string Keepsake(int chapter) => chapter switch { 0 => "flight patch", 1 => "wings", 2 => "pilot's badge", _ => "" };

        public static Line KeepsakeLine(int chapter) => chapter switch
        {
            0 => new Line("I left you my flight patch.\nSo you remember the night you saved me."),
            1 => new Line("Here, my wings. For the beacon\nwho got me back to the stars."),
            2 => new Line("And my pilot's badge.\nYou earned it, beacon."),
            _ => new Line(""),
        };

        /// <summary>
        /// Once her story is told, Mara flies every day: what Juniper says in your room about today's flight
        /// (<paramref name="world"/>: today's world), and how many days in a row you've brought her down.
        /// </summary>
        public static string TodaysFlight(string world, int streak, bool flownToday, string cargo = "") => flownToday
            ? "Mara's down safe today" + (streak > 1 ? ": " + streak + " days in a row." : ".") + "\nShe'll fly again tomorrow."
            : "Mara's flying over " + world + " today" + (cargo.Length > 0 ? ", carrying " + cargo : "") + ".\nShe'll need your light." + (streak > 0 ? "\n" + streak + (streak == 1 ? " day" : " days") + " in a row so far." : "");

        static readonly string[] Cargoes =
        {
            "medicine for the colony", "seeds for a world in winter", "letters from home", "a birthday cake for a lighthouse keeper",
            "a lost dog going home", "spare parts for a broken beacon", "a crate of oranges", "a telescope for a school",
            "warm blankets for the north", "a piano, very carefully"
        };

        /// <summary>What Mara carries on a given day (the same all day, different the next).</summary>
        public static string Cargo(DateTime utc) => Cargoes[(utc.Date.DayOfYear + utc.Year * 7) % Cargoes.Length];

        /// <summary>What Juniper says when you come back with her story unfinished.</summary>
        public static string WhileAway(int chapter) => chapter switch
        {
            1 => "Mara's been calling while you were away.\nShe's patched up and needs a light to climb out.",
            2 => "A signal came in while you were gone.\nMara's on her way back, with something for you.",
            _ => "Mara's been calling.",
        };

        /// <summary>When the room is lost: what Juniper says, so Rewind is the obvious next step.</summary>
        public const string LostHint = "You lost her signal.\nPress Rewind: she's still out there.";
        public const string Back = "...beacon? I've got your signal again!\nLet's try that again.";

        // ---------- her voice ----------

        /// <summary>The pieces of a room she can name (Emergency.Plain), and none.</summary>
        public static readonly string[] Pieces = { "sofa", "table", "shelf", "bed", "screen", "lamp", "plant", "chair", "furniture", "" };
        public const string HeldAny = "Your furniture kept me on course.";

        /// <summary>
        /// A line as separate sentences, to say one by one when the whole line was never recorded ("You blocked 6
        /// of them." "Your sofa kept me on course."). A trailing "..." is a pause, not the end of a sentence.
        /// </summary>
        public static List<string> Sentences(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            string t = text.Replace('\n', ' ');
            int start = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] != ' ' || i == 0) continue;
                int j = i - 1;
                while (j >= start && (t[j] == '.' || t[j] == '!' || t[j] == '?')) j--;
                string run = t.Substring(j + 1, i - j - 1);
                if (run.Length == 0 || run.Contains("..")) continue;
                string one = t.Substring(start, i - start).Trim();
                if (one.Length > 0) list.Add(one);
                start = i + 1;
            }
            string last = t.Substring(start).Trim();
            if (last.Length > 0) list.Add(last);
            return list;
        }

        /// <summary>The name of a recording of <paramref name="text"/>: the same for the same words, whatever the spacing or case.</summary>
        public static string VoiceKey(string text)
        {
            uint h = 2166136261;
            bool space = false;
            foreach (char c0 in (text ?? "").Trim())
            {
                char c = char.ToLowerInvariant(c0);
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space) { h = (h ^ ' ') * 16777619; space = false; }
                h = (h ^ c) * 16777619;
            }
            return "v" + h.ToString("x8");
        }

        /// <summary>
        /// A line for a caption <paramref name="width"/> characters wide, each long paragraph broken evenly over as few
        /// lines as it needs (so no word is left alone on a line). Only spaces become line breaks: the voice key is the same.
        /// </summary>
        public static string Fit(string text, int width = 34)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new System.Text.StringBuilder();
            foreach (var para in text.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                if (para.Length <= width) { sb.Append(para); continue; }
                var words = para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int lines = Lay(words, width, null), w = 1;
                foreach (var word in words) w = Math.Max(w, word.Length);
                w = Math.Max(w, (para.Length + lines - 1) / lines);
                while (w < width && Lay(words, w, null) > lines) w++;
                Lay(words, w, sb);
            }
            return sb.ToString();
        }

        static int Lay(string[] words, int width, System.Text.StringBuilder sb)
        {
            int count = 1, line = 0;
            foreach (var word in words)
            {
                if (line > 0 && line + 1 + word.Length > width) { count++; sb?.Append('\n'); line = 0; }
                else if (line > 0) { sb?.Append(' '); line++; }
                sb?.Append(word);
                line += word.Length;
            }
            return count;
        }

        /// <summary>Every whole line Mara can say, and every sentence of the lines that change (for recording her voice).</summary>
        public static List<string> EverythingSpoken()
        {
            var lines = new List<string>();
            var sentences = new List<string>();
            void Add(string l) { if (!string.IsNullOrEmpty(l) && !lines.Contains(l)) lines.Add(l); }
            void Split(string l) { foreach (var x in Sentences(l)) if (!sentences.Contains(x)) sentences.Add(x); }
            Add(Faint); Add(Back); Add(LaterLine.Text);
            for (int ch = 0; ch <= Chapters; ch++)
            {
                foreach (var l in Call(ch)) Add(l.Text);
                foreach (var l in Call(ch, Landing.Rough)) Add(l.Text);
                foreach (var piece in Pieces) foreach (var l in Call(ch, Landing.Clean, piece)) Add(l.Text);
                foreach (var l in Hook(ch)) Add(l.Text);
                Add(AskPace(ch).Text);
                Add(AskPace(ch, "m:fast").Text);
                Add(AskPace(ch, "m:slow").Text);
                Add(Halfway(ch));
                Add(KeepsakeLine(ch).Text);
                foreach (var id in new[] { "m:wave", "m:stay", "m:bye" }) Add(Reply(ch, id).Text);
                foreach (var landing in new[] { Landing.Clean, Landing.Rough, Landing.Lost })
                    for (int b = 0; b <= Pace.Slow.Count; b++)
                        foreach (var l in End(ch, landing, "", b)) { Add(l.Text); Split(l.Text); }
            }
            foreach (var l in Who()) Add(l.Text);
            foreach (var pace in new[] { "m:fast", "m:slow" }) { Add(Go(pace, true).Text); Add(Go(pace, false).Text); }
            foreach (var c in Cheers) Add(c);
            Add(BigWarning);
            Add(PalmAsk); Add(PalmLanded);
            foreach (var piece in Pieces) if (Named(piece)) Add(BigAgain(piece));
            foreach (var piece in Pieces) if (piece.Length > 0) { Add(StillDark(piece)); Add(Relit(piece)); }
            foreach (var piece in Pieces)
            {
                Add(FirstBreak(piece));
                Add(HurtSpoken(piece, 1f));
                Add(HurtSpoken(piece, 0f));
                if (piece.Length > 0 && piece != "furniture") sentences.Add("Your " + piece + " kept me on course.");
            }
            sentences.Add(HeldAny);
            foreach (var x in sentences) if (!lines.Contains(x)) lines.Add(x);
            return lines;
        }

        /// <summary>The choice the player meant, from what they said ("let's go slow" → the slow choice), or null.</summary>
        public static string FromWords(string said, Choice[] choices)
        {
            if (string.IsNullOrEmpty(said) || choices == null) return null;
            string t = " " + Words.Normalise(said) + " ";
            string found = null;
            foreach (var c in choices)
                foreach (var w in c.Said)
                    if (t.Contains(" " + w + " "))
                    {
                        if (found != null && found != c.Id) return null;   // both: not sure
                        found = c.Id;
                    }
            return found;
        }

        /// <summary>The recap line for the session.</summary>
        public static string Recap(int chapterDone, Landing best) => best == Landing.None ? "" :
            best == Landing.Lost ? "Mara's signal was lost" : "you guided Mara " + (chapterDone == 1 ? "back to the stars" : "home");
    }
}

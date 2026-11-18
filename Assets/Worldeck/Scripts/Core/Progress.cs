using System;
using System.Collections.Generic;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>
    /// Everything kept between visits: saved programs, the souvenirs standing in the real room, which
    /// worlds have been built, the daily world streak and first-launch flags. One JSON file; a damaged
    /// file reads as a fresh start (never a crash), and every list is capped so it can't grow forever.
    /// </summary>
    public sealed class Progress
    {
        public const int MaxPrograms = 24;
        public const int MaxSouvenirs = 16;

        public sealed class Saved
        {
            public string Name = "";
            public Recipe Recipe;
            public string Utc = "";
            public int Visits;
        }

        /// <summary>A souvenir standing in the real room: what it is, and where (a scene anchor plus an offset).</summary>
        public sealed class Souvenir
        {
            public string Biome = "";
            public string Kind = "";
            /// <summary>"night", "rain", "aurora"…: how the world was when it was won.</summary>
            public string Variant = "";
            /// <summary>Scene anchor key it stands on ("" = placed relative to the floor origin).</summary>
            public string Anchor = "";
            public float X, Y, Z, Yaw;
            public string Utc = "";
        }

        public readonly List<Saved> Programs = new List<Saved>();
        public readonly List<Souvenir> Souvenirs = new List<Souvenir>();
        /// <summary>Share codes received from friends: each one stands on your table as a small token.</summary>
        public sealed class Code { public string Text = ""; public string Title = ""; public string Utc = ""; public int Visits; }
        public readonly List<Code> Codes = new List<Code>();
        public const int MaxCodes = 8;

        /// <summary>Keeps a received code (once). Returns the entry, or null if the code doesn't read.</summary>
        public Code Receive(string code, DateTime utc)
        {
            var r = ShareCode.Decode(code);
            if (r == null) return null;
            string norm = ShareCode.Encode(r);
            var existing = Codes.Find(c => c.Text == norm);
            if (existing != null) return existing;
            var entry = new Code { Text = norm, Title = r.Def.Name, Utc = Iso(utc) };
            Codes.Add(entry);
            while (Codes.Count > MaxCodes) Codes.RemoveAt(0);
            return entry;
        }
        public readonly Dictionary<string, int> Visits = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Built = new Dictionary<string, int>();
        public int Launches;
        public bool Onboarded;
        public bool VoiceTried;
        /// <summary>The player turned on-device voice on ("Juniper, …" works anytime).</summary>
        public bool VoiceOn;
        /// <summary>On-device voice was offered once (yes or not now).</summary>
        public bool VoiceOffered;
        /// <summary>Comfort: the stick takes short blink-steps instead of gliding.</summary>
        public bool StepMove;
        /// <summary>Comfort & access: bigger captions and labels.</summary>
        public bool CaptionsLarge;
        /// <summary>Comfort & access: a bigger catch zone and more grace in the storm.</summary>
        public bool GentleStorm;
        /// <summary>Comfort & access: fewer sparks and flashes.</summary>
        public bool CalmEffects;
        /// <summary>Comfort & access: look at a button to press it, even with hands in view.</summary>
        public bool LookToPress;
        /// <summary>
        /// Even Mara's first call waits for the Answer button. (After her first chapter she always waits: she never
        /// rings by herself in a world you opened.)
        /// </summary>
        public bool CallWhenReady;
        /// <summary>Sound level: 0 quiet, 1 normal, 2 loud.</summary>
        public int SoundLevel = 1;
        /// <summary>Every sound off (captions stay).</summary>
        public bool Muted;
        /// <summary>Soft music in the background (on unless turned off).</summary>
        public bool MusicOn = true;
        /// <summary>Records: most meteors blocked in one storm; Mara's landings.</summary>
        public int BestBlocked, Landings;
        /// <summary>Composed places visited, by name ("On Mars").</summary>
        public readonly List<string> PlacesSeen = new List<string>();
        /// <summary>The storm drill: last and best outcome (1 untouched … 3 battered; 0 never), times played.</summary>
        public int StormLast, StormBest, StormRuns;
        /// <summary>The player has seen a piece of their furniture break back to the real one.</summary>
        public bool SeenBreak;
        /// <summary>The Last Signal: chapters finished (0 = Mara hasn't called yet).</summary>
        public int MissionChapter;
        /// <summary>How each finished chapter went, by chapter: "Clean|m:slow|sofa" (landing, pace, a piece that held).</summary>
        public readonly List<string> MaraLog = new List<string>();
        /// <summary>The ending in your room (after her last chapter) has been seen.</summary>
        public bool StoryEndSeen;
        /// <summary>The welcome card on first launch has been seen (or skipped).</summary>
        public bool WelcomeSeen;
        /// <summary>What each local remembers: "key|visits|favour done (0/1)|last thing made there".</summary>
        public readonly List<string> LocalMemory = new List<string>();

        string[] Memory(string key)
        {
            foreach (var m in LocalMemory) { var p = m.Split('|'); if (p.Length >= 4 && p[0] == key) return p; }
            return null;
        }

        void SetMemory(string key, int visits, bool favour, string made)
        {
            LocalMemory.RemoveAll(m => m.StartsWith(key + "|", StringComparison.Ordinal));
            LocalMemory.Add(key + "|" + visits + "|" + (favour ? 1 : 0) + "|" + (made ?? "").Replace("|", ""));
            while (LocalMemory.Count > 60) LocalMemory.RemoveAt(0);
        }

        public int LocalVisits(string key) { var p = Memory(key); return p != null && int.TryParse(p[1], out int v) ? v : 0; }
        public bool FavourDone(string key) { var p = Memory(key); return p != null && p[2] == "1"; }
        public string LocalLastMade(string key) { var p = Memory(key); return p != null ? p[3] : ""; }
        public void LocalMet(string key) => SetMemory(key, LocalVisits(key) + 1, FavourDone(key), LocalLastMade(key));
        public void LocalFavour(string key) => SetMemory(key, Math.Max(1, LocalVisits(key)), true, LocalLastMade(key));
        public void LocalMade(string key, string made) => SetMemory(key, Math.Max(1, LocalVisits(key)), FavourDone(key), made);
        /// <summary>Your furniture that went dark in her last storm (scanned pieces' keys); a world's task lights it again.</summary>
        public readonly List<string> DarkPieces = new List<string>();

        /// <summary>After her storm: the pieces it was aimed at are lit unless they ended dark. At most eight are kept
        /// (the oldest goes: keys from an old scan must not crowd out today's).</summary>
        public void Darken(List<string> aimedAt, List<string> dark)
        {
            if (aimedAt != null) DarkPieces.RemoveAll(k => aimedAt.Contains(k));
            if (dark == null) return;
            foreach (var k in dark)
            {
                if (string.IsNullOrEmpty(k) || k.Length >= 80 || DarkPieces.Contains(k)) continue;
                DarkPieces.Add(k);
                if (DarkPieces.Count > 8) DarkPieces.RemoveAt(0);
            }
        }

        /// <summary>Remembers how a chapter went (landing, pace, a piece that never went dark).</summary>
        public void RememberChapter(int chapter, Landing landing, string pace, string held, string dark = "", string relit = "")
        {
            if (chapter < 0 || chapter > 20) return;
            while (MaraLog.Count <= chapter) MaraLog.Add("");
            MaraLog[chapter] = landing + "|" + (pace ?? "") + "|" + (held ?? "").Replace("|", "") + "|" + (dark ?? "").Replace("|", "") +
                               "|" + (relit ?? "").Replace("|", "");
        }

        /// <summary>What a chapter remembers besides its landing: 3 the piece that went dark, 4 the piece lit again before it.</summary>
        public string ChapterNote(int chapter, int part)
        {
            if (chapter < 0 || chapter >= MaraLog.Count || string.IsNullOrEmpty(MaraLog[chapter])) return "";
            var parts = MaraLog[chapter].Split('|');
            return part < parts.Length ? parts[part] : "";
        }

        /// <summary>The piece most recently lit again in her story ("" for none).</summary>
        public string LastRelit()
        {
            for (int ch = MaraLog.Count - 1; ch >= 0; ch--) { string r = ChapterNote(ch, 4); if (r.Length > 0) return r; }
            return "";
        }

        /// <summary>How chapter <paramref name="chapter"/> went (None if it isn't remembered).</summary>
        public Landing ChapterLanding(int chapter, out string pace, out string held)
        {
            pace = ""; held = "";
            if (chapter < 0 || chapter >= MaraLog.Count || string.IsNullOrEmpty(MaraLog[chapter])) return Landing.None;
            var parts = MaraLog[chapter].Split('|');
            if (parts.Length > 1) pace = parts[1];
            if (parts.Length > 2) held = parts[2];
            return Enum.TryParse(parts[0], out Landing l) ? l : Landing.None;
        }

        /// <summary>A piece that held in every chapter remembered (or, failing that, in the last one), or "".</summary>
        public string HeldThroughout()
        {
            string last = "", every = null;
            bool first = true;
            for (int ch = 0; ch < MaraLog.Count; ch++)
            {
                if (ChapterLanding(ch, out _, out string held) == Landing.None) continue;
                if (held.Length > 0) last = held;
                every = first ? held : every == held ? every : "";
                first = false;
            }
            return !string.IsNullOrEmpty(every) ? every : last;
        }
        /// <summary>Wonders found while exploring ("campfire/the Elder Pine").</summary>
        public readonly List<string> Wonders = new List<string>();
        /// <summary>Exploring was shown once (walk, jump, the wonders).</summary>
        public bool ExploreShown;
        public string LastDailyDone = "";
        public int DailyStreak;
        public int BestDailyStreak;
        /// <summary>The last day Mara was brought down safely, and how many days in a row.</summary>
        public string LastFlight = "";
        public int FlightStreak, BestFlightStreak;
        /// <summary>The last world you were in (offered as "pick up where you were").</summary>
        public Recipe Last;

        public int WorldsBuilt => Built.Count;

        // ---------- programs ----------

        /// <summary>Saves a world. The same world saved again is renamed, not duplicated. Oldest drop past the cap.</summary>
        /// <summary>The saved world whose name best matches what was said ("my happy place" finds "My happy place"), or null.</summary>
        public Saved Find(string said)
        {
            string want = Words.Normalise(said).Replace("my ", "").Replace("the ", "").Trim();
            if (want.Length == 0) return null;
            Saved best = null;
            int bestScore = 0;
            foreach (var p in Programs)
            {
                string name = Words.Normalise(p.Name).Replace("my ", "").Replace("the ", "").Trim();
                int score = name == want ? 100 : name.Contains(want) || want.Contains(name) ? 50 : 0;
                if (score == 0)
                    foreach (var w in want.Split(' ')) if (w.Length > 2 && name.Contains(w)) score += 10;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        public Saved Save(string name, Recipe r, DateTime utc)
        {
            name = Words.CleanTitle(name);
            if (name.Length == 0) name = r.Title.Length > 0 ? r.Title : r.Def.Name;
            foreach (var p in Programs)
            {
                if (p.Recipe != null && p.Recipe.SameWorld(r))
                {
                    p.Name = name;
                    p.Utc = Iso(utc);
                    return p;
                }
            }
            // Names stay unique: "Beach", "Beach 2"…
            string unique = name;
            for (int n = 2; Programs.Exists(p => p.Name == unique); n++) unique = name + " " + n.ToString(CultureInfo.InvariantCulture);
            var s = new Saved { Name = unique, Recipe = r.Copy(), Utc = Iso(utc) };
            Programs.Add(s);
            while (Programs.Count > MaxPrograms) Programs.RemoveAt(0);
            return s;
        }

        public void Visited(string biome)
        {
            Visits.TryGetValue(biome, out int n);
            Visits[biome] = n + 1;
        }

        /// <summary>
        /// Records a finished build. Returns the souvenir to place when it is the first time this world was
        /// built; null otherwise (one souvenir per world keeps a real room from filling up).
        /// </summary>
        public Souvenir Finished(Recipe r, DateTime utc)
        {
            Built.TryGetValue(r.Biome, out int n);
            Built[r.Biome] = n + 1;
            if (n > 0 || Souvenirs.Exists(s => s.Biome == r.Biome)) return null;
            var v = new Souvenir { Biome = r.Biome, Kind = r.Def.Souvenir, Variant = VariantOf(r), Utc = Iso(utc) };
            Souvenirs.Add(v);
            while (Souvenirs.Count > MaxSouvenirs) Souvenirs.RemoveAt(0);
            return v;
        }

        public static string VariantOf(Recipe r)
        {
            if (r.Extras.Contains(Extras.Aurora)) return "aurora";
            if (r.Precip == Precip.Snow && r.Biome != "snow") return "snow";
            if (r.Precip == Precip.Rain) return "rain";
            if (r.Def.HasTime && r.IsNight) return "night";
            return "";
        }

        /// <summary>The daily world was finished on <paramref name="utc"/>'s date: extends or restarts the streak.</summary>
        public void DailyDone(DateTime utc)
        {
            string today = utc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (LastDailyDone == today) return;
            string yesterday = utc.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DailyStreak = LastDailyDone == yesterday ? DailyStreak + 1 : 1;
            BestDailyStreak = Math.Max(BestDailyStreak, DailyStreak);
            LastDailyDone = today;
        }

        public bool DailyDoneOn(DateTime utc) => LastDailyDone == utc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Mara landed safely on <paramref name="utc"/>'s date: extends or restarts the run of days.</summary>
        public void FlightDone(DateTime utc)
        {
            string today = utc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (LastFlight == today) return;
            string yesterday = utc.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            FlightStreak = LastFlight == yesterday ? FlightStreak + 1 : 1;
            BestFlightStreak = Math.Max(BestFlightStreak, FlightStreak);
            LastFlight = today;
        }

        public bool FlownOn(DateTime utc) => LastFlight == utc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>The run of days, as it stands today (0 once a day was missed).</summary>
        public int FlightStreakOn(DateTime utc)
        {
            string today = utc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string yesterday = utc.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return LastFlight == today || LastFlight == yesterday ? FlightStreak : 0;
        }

        // ---------- file ----------

        public string ToJsonString()
        {
            var programs = new List<object>();
            foreach (var p in Programs)
                programs.Add(new Dictionary<string, object> { { "name", p.Name }, { "utc", p.Utc }, { "visits", p.Visits }, { "recipe", p.Recipe.ToJson() } });
            var souvenirs = new List<object>();
            foreach (var s in Souvenirs)
                souvenirs.Add(new Dictionary<string, object>
                {
                    { "biome", s.Biome }, { "kind", s.Kind }, { "variant", s.Variant }, { "anchor", s.Anchor },
                    { "x", (double)s.X }, { "y", (double)s.Y }, { "z", (double)s.Z }, { "yaw", (double)s.Yaw }, { "utc", s.Utc }
                });
            var codes = new List<object>();
            foreach (var c in Codes) codes.Add(new Dictionary<string, object> { { "code", c.Text }, { "title", c.Title }, { "utc", c.Utc }, { "visits", c.Visits } });
            var o = new Dictionary<string, object>
            {
                { "v", 1 }, { "launches", Launches }, { "onboarded", Onboarded }, { "voice_tried", VoiceTried }, { "voice_on", VoiceOn }, { "voice_offered", VoiceOffered }, { "step_move", StepMove }, { "captions_large", CaptionsLarge }, { "gentle_storm", GentleStorm }, { "calm_effects", CalmEffects }, { "look_to_press", LookToPress }, { "call_when_ready", CallWhenReady }, { "sound_level", SoundLevel }, { "muted", Muted }, { "music_on", MusicOn }, { "best_blocked", BestBlocked }, { "landings", Landings }, { "places_seen", new List<object>(PlacesSeen) }, { "storm_last", StormLast }, { "storm_best", StormBest }, { "storm_runs", StormRuns }, { "seen_break", SeenBreak }, { "mission_chapter", MissionChapter }, { "mara_log", new List<object>(MaraLog) }, { "story_end_seen", StoryEndSeen }, { "welcome_seen", WelcomeSeen }, { "local_memory", new List<object>(LocalMemory) }, { "dark_pieces", new List<object>(DarkPieces) }, { "explore_shown", ExploreShown }, { "wonders", new List<object>(Wonders) },
                { "daily_done", LastDailyDone }, { "daily_streak", DailyStreak }, { "best_daily_streak", BestDailyStreak },
                { "last_flight", LastFlight }, { "flight_streak", FlightStreak }, { "best_flight_streak", BestFlightStreak },
                { "visits", Counts(Visits) }, { "built", Counts(Built) }, { "last", Last != null ? (object)Last.ToJson() : null },
                { "programs", programs }, { "souvenirs", souvenirs }, { "codes", codes }
            };
            return Json.Write(o);
        }

        static Dictionary<string, object> Counts(Dictionary<string, int> d)
        {
            var o = new Dictionary<string, object>();
            foreach (var kv in d) o[kv.Key] = kv.Value;
            return o;
        }

        /// <summary>Reads a saved file. Anything unreadable gives a fresh Progress (and <paramref name="damaged"/> = true).</summary>
        public static Progress Parse(string text, out bool damaged)
        {
            var p = new Progress();
            damaged = false;
            if (string.IsNullOrEmpty(text)) return p;
            var o = Json.Parse(text) as Dictionary<string, object>;
            if (o == null) { damaged = true; return p; }
            p.Launches = (int)Json.Num(o, "launches", 0);
            p.Onboarded = Json.Bool(o, "onboarded", false);
            p.VoiceTried = Json.Bool(o, "voice_tried", false);
            p.VoiceOn = Json.Bool(o, "voice_on", false);
            p.VoiceOffered = Json.Bool(o, "voice_offered", false);
            p.StepMove = Json.Bool(o, "step_move", false);
            p.CaptionsLarge = Json.Bool(o, "captions_large", false);
            p.GentleStorm = Json.Bool(o, "gentle_storm", false);
            p.CalmEffects = Json.Bool(o, "calm_effects", false);
            p.LookToPress = Json.Bool(o, "look_to_press", false);
            p.CallWhenReady = Json.Bool(o, "call_when_ready", false);
            p.SoundLevel = Math.Max(0, Math.Min(2, (int)Json.Num(o, "sound_level", 1)));
            p.Muted = Json.Bool(o, "muted", false);
            p.MusicOn = Json.Bool(o, "music_on", true);
            p.BestBlocked = Math.Max(0, (int)Json.Num(o, "best_blocked", 0));
            p.Landings = Math.Max(0, (int)Json.Num(o, "landings", 0));
            var seen = Json.Arr(o, "places_seen");
            if (seen != null) foreach (var x in seen) if (x is string xs && xs.Length > 0 && xs.Length <= 40 && !p.PlacesSeen.Contains(xs) && p.PlacesSeen.Count < 200) p.PlacesSeen.Add(xs);
            p.StormLast = (int)Json.Num(o, "storm_last", 0); p.StormBest = (int)Json.Num(o, "storm_best", 0); p.StormRuns = (int)Json.Num(o, "storm_runs", 0); p.SeenBreak = Json.Bool(o, "seen_break", false); p.MissionChapter = (int)Json.Num(o, "mission_chapter", 0);
            var maraLog = Json.Arr(o, "mara_log");
            if (maraLog != null) foreach (var x in maraLog) if (p.MaraLog.Count < 20) p.MaraLog.Add(x is string xs && xs.Length < 80 ? xs : "");
            p.StoryEndSeen = Json.Bool(o, "story_end_seen", false);
            p.WelcomeSeen = Json.Bool(o, "welcome_seen", false);
            var lm = Json.Arr(o, "local_memory");
            if (lm != null) foreach (var x in lm) if (x is string xs && xs.Length > 0 && xs.Length < 200 && p.LocalMemory.Count < 60) p.LocalMemory.Add(xs);
            var dark = Json.Arr(o, "dark_pieces");
            if (dark != null) foreach (var x in dark) if (x is string xs && xs.Length > 0 && xs.Length < 80 && p.DarkPieces.Count < 8 && !p.DarkPieces.Contains(xs)) p.DarkPieces.Add(xs);
            p.ExploreShown = Json.Bool(o, "explore_shown", false);
            var wonders = Json.Arr(o, "wonders");
            if (wonders != null) foreach (var w in wonders) if (w is string ws && ws.Length > 0 && ws.Length < 80 && !p.Wonders.Contains(ws)) p.Wonders.Add(ws);
            p.LastDailyDone = Json.Str(o, "daily_done", "");
            p.DailyStreak = (int)Json.Num(o, "daily_streak", 0);
            p.BestDailyStreak = (int)Json.Num(o, "best_daily_streak", 0);
            p.LastFlight = Json.Str(o, "last_flight", "");
            p.FlightStreak = (int)Json.Num(o, "flight_streak", 0);
            p.BestFlightStreak = (int)Json.Num(o, "best_flight_streak", 0);
            ReadCounts(Json.Obj(o, "visits"), p.Visits);
            p.Last = Recipe.FromJson(Json.Obj(o, "last"));
            ReadCounts(Json.Obj(o, "built"), p.Built);
            var progs = Json.Arr(o, "programs");
            if (progs != null)
                foreach (var item in progs)
                {
                    if (!(item is Dictionary<string, object> d)) continue;
                    var r = Recipe.FromJson(Json.Obj(d, "recipe"));
                    if (r == null) continue;
                    p.Programs.Add(new Saved { Name = Words.CleanTitle(Json.Str(d, "name", r.Title)), Recipe = r, Utc = Json.Str(d, "utc", ""), Visits = (int)Json.Num(d, "visits", 0) });
                    if (p.Programs.Count >= MaxPrograms) break;
                }
            var codes = Json.Arr(o, "codes");
            if (codes != null)
                foreach (var item in codes)
                {
                    if (!(item is Dictionary<string, object> d)) continue;
                    string codeText = Json.Str(d, "code", "");
                    if (ShareCode.Decode(codeText) == null) continue;
                    p.Codes.Add(new Code { Text = codeText, Title = Words.CleanTitle(Json.Str(d, "title", "")), Utc = Json.Str(d, "utc", ""), Visits = (int)Json.Num(d, "visits", 0) });
                    if (p.Codes.Count >= MaxCodes) break;
                }
            var sous = Json.Arr(o, "souvenirs");
            if (sous != null)
                foreach (var item in sous)
                {
                    if (!(item is Dictionary<string, object> d)) continue;
                    string biome = Json.Str(d, "biome", "");
                    if (Kit.Get(biome) == null) continue;
                    p.Souvenirs.Add(new Souvenir
                    {
                        Biome = biome, Kind = Json.Str(d, "kind", Kit.Get(biome).Souvenir), Variant = Json.Str(d, "variant", ""),
                        Anchor = Json.Str(d, "anchor", ""), X = (float)Json.Num(d, "x", 0), Y = (float)Json.Num(d, "y", 0),
                        Z = (float)Json.Num(d, "z", 0), Yaw = (float)Json.Num(d, "yaw", 0), Utc = Json.Str(d, "utc", "")
                    });
                    if (p.Souvenirs.Count >= MaxSouvenirs) break;
                }
            return p;
        }

        static void ReadCounts(Dictionary<string, object> o, Dictionary<string, int> into)
        {
            if (o == null) return;
            foreach (var kv in o)
                if (Kit.Get(kv.Key) != null && kv.Value is double d && d >= 0 && d < 1e7) into[kv.Key] = (int)d;
        }

        static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}

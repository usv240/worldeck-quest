using System;
using System.Collections.Generic;
using System.Text;

namespace Worldeck.Core
{
    /// <summary>
    /// The player's records, kept on the headset: what the Records page shows, and which of Meta's achievements and
    /// leaderboard scores they have earned (the app sends those when the platform is there).
    /// </summary>
    public static class Records
    {
        public const int WorldwalkerCount = 10;

        /// <summary>Distinct worlds and places visited (kit worlds by id, composed places by name).</summary>
        public static int WorldsVisited(Progress p)
        {
            var all = new HashSet<string>(p.Visits.Keys);
            foreach (var s in p.PlacesSeen) all.Add("place:" + s);
            return all.Count;
        }

        /// <summary>The Records page: a few short lines.</summary>
        public static string Text(Progress p)
        {
            var sb = new StringBuilder();
            int ch = Math.Min(p.MissionChapter, Story.Chapters);
            sb.Append("Mara's story: ").Append(ch >= Story.Chapters ? "complete" : "chapter " + (ch + 1) + " of " + Story.Chapters)
              .Append(p.Landings > 0 ? " · " + p.Landings + (p.Landings == 1 ? " landing" : " landings") : "").Append('\n');
            sb.Append("Most meteors blocked in one storm: ").Append(p.BestBlocked).Append('\n');
            sb.Append("Worlds and places visited: ").Append(WorldsVisited(p)).Append('\n');
            sb.Append("Wonders found: ").Append(p.Wonders.Count).Append('\n');
            sb.Append("Souvenirs on your table: ").Append(p.Souvenirs.Count).Append('\n');
            sb.Append("Daily streak: ").Append(p.DailyStreak).Append(p.BestDailyStreak > p.DailyStreak ? " (best " + p.BestDailyStreak + ")" : "");
            return sb.ToString();
        }

        /// <summary>Every achievement (by its platform name) the progress has earned.</summary>
        public static List<string> Earned(Progress p, bool untouchedStorm = false, bool allWondersHere = false)
        {
            var l = new List<string>();
            if (p.Landings > 0) l.Add("FIRST_LANDING");
            if (p.MissionChapter >= Story.Chapters) l.Add("SPRING_RETURNS");
            if (untouchedStorm || p.StormBest == 1) l.Add("UNTOUCHED");
            if (allWondersHere) l.Add("ALL_WONDERS");
            if (WorldsVisited(p) >= WorldwalkerCount) l.Add("WORLDWALKER");
            if (p.PlacesSeen.Count > 0) l.Add("SAY_A_WORLD");
            return l;
        }
    }
}

using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Meta's achievements and leaderboards (the player's Quest profile). Started only on a Quest; everything here is
    /// optional: if the account, entitlement or network isn't there, Worldeck plays exactly the same and the records
    /// stay on the headset (<see cref="Records"/>). Unlocks asked for before it is ready are kept and sent once it is.
    /// The achievement and leaderboard names below must match the ones created on the developer dashboard.
    /// </summary>
    public sealed class MetaPlatform : MonoBehaviour
    {
        public const string AppId = "1428532160343666";

        // Achievements (simple unlocks).
        public const string FirstLanding = "FIRST_LANDING", StoryDone = "SPRING_RETURNS", Untouched = "UNTOUCHED",
            AllWonders = "ALL_WONDERS", Worldwalker = "WORLDWALKER", SayAWorld = "SAY_A_WORLD";
        // Leaderboards (higher is better).
        public const string MostBlocked = "MOST_BLOCKED", WorldsVisited = "WORLDS_VISITED";

        public static MetaPlatform Instance { get; private set; }
        public bool Ready { get; private set; }
        public int Sent { get; private set; }
        readonly List<Action> pending = new List<Action>();
        readonly HashSet<string> unlocked = new HashSet<string>();

        void Awake() => Instance = this;

        void Start()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                Health.Set(Health.Platform, Health.Level.Fallback, "not on a Quest: records stay on this device", Time.realtimeSinceStartup);
                return;
            }
            try
            {
                Oculus.Platform.Core.AsyncInitialize(AppId).OnComplete(m =>
                {
                    if (m == null || m.IsError) { Fallback("platform did not start"); return; }
                    Oculus.Platform.Entitlements.IsUserEntitledToApplication().OnComplete(e =>
                    {
                        if (e == null || e.IsError) { Fallback("no entitlement (sideloaded?)"); return; }
                        Ready = true;
                        Health.Ok(Health.Platform, "achievements and leaderboards on", Time.realtimeSinceStartup);
                        Trace.Info("Platform", "ready", "pending", pending.Count);
                        foreach (var a in pending) a();
                        pending.Clear();
                    });
                });
            }
            catch (Exception ex) { Fallback(ex.GetType().Name); }
        }

        void Fallback(string why)
        {
            Ready = false;
            pending.Clear();
            Health.Set(Health.Platform, Health.Level.Fallback, why + ": records stay on this device", Time.realtimeSinceStartup);
            Trace.Info("Platform", "off", "why", why);
        }

        void Do(Action a)
        {
            if (Ready) a();
            else if (pending.Count < 32) pending.Add(a);
        }

        /// <summary>Unlocks an achievement (once per session is enough; the platform keeps it).</summary>
        public void Unlock(string name)
        {
            if (string.IsNullOrEmpty(name) || !unlocked.Add(name)) return;
            Trace.Info("Platform", "achievement", "name", name);
            Do(() =>
            {
                try { Oculus.Platform.Achievements.Unlock(name).OnComplete(m => { if (m != null && !m.IsError) Sent++; }); }
                catch (Exception ex) { Trace.Warn("Platform", "unlock failed", "error", ex.GetType().Name); }
            });
        }

        // Which leaderboards the platform knows (asked once each): writing to one that isn't set up on the dashboard
        // gets an empty reply that the SDK can't read, and it logs an exception.
        readonly System.Collections.Generic.Dictionary<string, bool> boards = new System.Collections.Generic.Dictionary<string, bool>();
        readonly System.Collections.Generic.Dictionary<string, long> waiting = new System.Collections.Generic.Dictionary<string, long>();

        /// <summary>Posts a score; the platform keeps the player's best.</summary>
        public void Score(string board, long value)
        {
            if (string.IsNullOrEmpty(board) || value <= 0) return;
            Do(() =>
            {
                if (boards.TryGetValue(board, out bool known)) { if (known) Write(board, value); return; }
                bool asking = waiting.ContainsKey(board);
                waiting[board] = Math.Max(value, asking ? waiting[board] : 0);
                if (asking) return;
                try
                {
                    Oculus.Platform.Leaderboards.Get(board).OnComplete(m =>
                    {
                        bool ok = m != null && !m.IsError && m.Data != null && m.Data.Count > 0;
                        boards[board] = ok;
                        Trace.Info("Platform", ok ? "leaderboard ready" : "leaderboard not set up: scores stay on this device", "board", board);
                        if (ok && waiting.TryGetValue(board, out long best)) Write(board, best);
                        waiting.Remove(board);
                    });
                }
                catch (Exception ex) { boards[board] = false; waiting.Remove(board); Trace.Warn("Platform", "leaderboard check failed", "error", ex.GetType().Name); }
            });
        }

        void Write(string board, long value)
        {
            try { Oculus.Platform.Leaderboards.WriteEntry(board, value).OnComplete(m => { if (m != null && !m.IsError) Sent++; }); }
            catch (Exception ex) { Trace.Warn("Platform", "score failed", "error", ex.GetType().Name); }
        }

        public static void Achieve(string name) => Instance?.Unlock(name);
        public static void Post(string board, long value) => Instance?.Score(board, value);
    }
}

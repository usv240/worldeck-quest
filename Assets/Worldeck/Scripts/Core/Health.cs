using System;
using System.Collections.Generic;
using System.Text;

namespace Worldeck.Core
{
    /// <summary>
    /// One place where every part of the app says how it is really running, so nothing falls back in
    /// silence. A part is Ok, on a Fallback (it works, in a lesser way the player is told about), or at
    /// Fault. Faults in the parts comfort depends on (head tracking, steady frames) end the world and
    /// bring the room back: a juddering world around you is worse than none.
    /// Engine-independent; the Unity layer reports into it and shows it.
    /// </summary>
    public static class Health
    {
        public enum Level { Ok, Fallback, Fault }

        // Parts. The first two are critical: a Fault in them fades the world back to the room.
        public const string Head = "Head tracking";
        public const string Frames = "Frame rate";
        public const string Hands = "Hands";
        public const string Room = "Room";
        public const string Passthrough = "Passthrough";
        public const string Voice = "Voice";
        public const string Ai = "World AI";
        public const string Sound = "Sound";
        public const string Anchors = "Souvenirs";
        public const string Platform = "Meta account";
        public const string Save = "Saving";
        /// <summary>The on-headset play log (its own part: a log write used to report over a failed progress save).</summary>
        public const string Log = "Play log";

        static readonly string[] Critical = { Head, Frames };
        static readonly string[] Order = { Head, Frames, Hands, Passthrough, Room, Voice, Ai, Sound, Anchors, Platform, Save, Log };

        public sealed class Entry
        {
            public string Part;
            public Level Level;
            public string Detail;       // shown in the developer overlay and the log
            public string PlayerLine;   // shown to the player when not Ok ("" = say nothing)
            public double Since;
        }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        /// <summary>Raised when a part changes level or detail (not on repeats).</summary>
        public static event Action<Entry> Changed;

        /// <summary>Last time (in the caller's clock) any critical part was at Fault. -inf if never.</summary>
        public static double CriticalFaultAt { get; private set; } = double.NegativeInfinity;

        public static IEnumerable<Entry> All
        {
            get
            {
                foreach (var p in Order) if (entries.TryGetValue(p, out var e)) yield return e;
                foreach (var kv in entries) if (Array.IndexOf(Order, kv.Key) < 0) yield return kv.Value;
            }
        }

        public static Entry Get(string part) => entries.TryGetValue(part, out var e) ? e : null;
        public static bool IsCritical(string part) => Array.IndexOf(Critical, part) >= 0;

        public static void Set(string part, Level level, string detail, double now, string playerLine = "")
        {
            if (IsCritical(part) && level == Level.Fault) CriticalFaultAt = now;
            if (entries.TryGetValue(part, out var e) && e.Level == level && e.Detail == detail && e.PlayerLine == playerLine)
                return;
            e = new Entry { Part = part, Level = level, Detail = detail ?? "", PlayerLine = playerLine ?? "", Since = now };
            entries[part] = e;
            Changed?.Invoke(e);
        }

        public static void Ok(string part, string detail, double now) => Set(part, Level.Ok, detail, now);

        /// <summary>The critical fault active now, or null.</summary>
        public static Entry ActiveCriticalFault()
        {
            foreach (var p in Critical)
                if (entries.TryGetValue(p, out var e) && e.Level == Level.Fault) return e;
            return null;
        }

        /// <summary>True if a critical fault was active at any time since <paramref name="since"/> (or is now).</summary>
        public static bool CriticalFaultSince(double since) => ActiveCriticalFault() != null || CriticalFaultAt >= since;

        /// <summary>The one line the player should see now: the worst part that has something to say. "" if none.</summary>
        public static string PlayerLine()
        {
            Entry worst = null;
            foreach (var e in All)
            {
                if (e.Level == Level.Ok || string.IsNullOrEmpty(e.PlayerLine)) continue;
                if (worst == null || e.Level > worst.Level) worst = e;
            }
            return worst == null ? "" : worst.PlayerLine;
        }

        /// <summary>"Two-eye view ok (63 mm) · Frame rate ok (90 Hz) · Room fallback (no scan)" for the overlay and logcat.</summary>
        public static string Summary()
        {
            var sb = new StringBuilder();
            foreach (var e in All)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(e.Part).Append(' ').Append(e.Level == Level.Ok ? "ok" : e.Level == Level.Fallback ? "fallback" : "FAULT");
                if (e.Detail.Length > 0) sb.Append(" (").Append(e.Detail).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>"Checks: two-eye view ok · head ok · frames ok" for the Options page ("n/a" where it can't be read).</summary>
        public static string CheckLine()
        {
            var parts = new List<string>();
            foreach (var p in Critical)
            {
                if (!entries.TryGetValue(p, out var e)) continue;
                string name = p == Head ? "head" : "frames";
                parts.Add(name + (e.Level == Level.Ok ? " ok" : e.Level == Level.Fallback ? " n/a" : " FAULT"));
            }
            return parts.Count == 0 ? "" : "Checks: " + string.Join(" · ", parts);
        }

        public static void Reset()
        {
            entries.Clear();
            CriticalFaultAt = double.NegativeInfinity;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// Every error and exception the app logs, counted, with the first of each kind kept for the play log.
    /// Thread-safe (the engine reports log messages from any thread) and bounded: at most MaxDistinct kinds
    /// are kept, whatever else goes wrong is still counted, so a flood can never fill the headset's storage.
    /// Engine-independent.
    /// </summary>
    public sealed class ErrorTally
    {
        public const int MaxDistinct = 20;
        const int KeyChars = 120;

        public sealed class Entry
        {
            public string Level;      // Exception / Error / Assert
            public string Message;
            public string Where;      // the first frame of our own code in the stack, else the first frame
            public int Count;
            public DateTime FirstUtc;
        }

        readonly object gate = new object();
        readonly Dictionary<string, Entry> kinds = new Dictionary<string, Entry>();
        readonly List<Entry> fresh = new List<Entry>();

        public int Total { get; private set; }
        public int Exceptions { get; private set; }
        /// <summary>Errors not kept as their own kind because MaxDistinct was reached (still in Total).</summary>
        public int Overflow { get; private set; }
        public int Distinct { get { lock (gate) return kinds.Count; } }

        /// <summary>Counts one message. Returns true the first time this kind is seen.</summary>
        public bool Record(string level, string message, string stack, DateTime utc)
        {
            message = (message ?? "").Trim();
            string key = level + ":" + (message.Length > KeyChars ? message.Substring(0, KeyChars) : message);
            lock (gate)
            {
                Total++;
                if (level == "Exception") Exceptions++;
                if (kinds.TryGetValue(key, out var e)) { e.Count++; return false; }
                if (kinds.Count >= MaxDistinct) { Overflow++; return false; }
                e = new Entry
                {
                    Level = level,
                    Message = message.Length > 300 ? message.Substring(0, 300) : message,
                    Where = Where(stack),
                    Count = 1,
                    FirstUtc = utc
                };
                kinds[key] = e;
                fresh.Add(e);
                return true;
            }
        }

        /// <summary>The kinds first seen since the last call (to write to the log on the main thread).</summary>
        public List<Entry> TakeNew()
        {
            lock (gate)
            {
                var l = new List<Entry>(fresh);
                fresh.Clear();
                return l;
            }
        }

        public void Clear()
        {
            lock (gate) { kinds.Clear(); fresh.Clear(); Total = Exceptions = Overflow = 0; }
        }

        /// <summary>The first stack line in our own code ("Worldeck."), else the first line.</summary>
        public static string Where(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            string first = null;
            foreach (var raw in stack.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                if (first == null) first = l;
                if (l.IndexOf("Worldeck.", StringComparison.Ordinal) >= 0) return Clip(l);
            }
            return Clip(first ?? "");
        }

        static string Clip(string s) => s.Length > 200 ? s.Substring(0, 200) : s;
    }
}

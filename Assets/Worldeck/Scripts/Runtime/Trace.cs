using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// One way to log everything that matters, so a headset run can be read back like a story. Every line
    /// goes to logcat as "[Worldeck:Area] message key=value … @12.3s f1234 World" and to the on-headset play
    /// log as a "trace" record (same fields, machine readable). Areas: App, Room, Director, Reveal, World,
    /// Skins, Gather, Hands, Panel, Guide, Voice, AI, Watch, Light, Audio, Campfire, Finale, Souvenirs, Perf.
    /// Spammy lines use <see cref="Every"/> so they appear at most once per interval.
    /// </summary>
    public static class Trace
    {
        public static Func<string> State = () => "";
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();
        static readonly StringBuilder sb = new StringBuilder(256);

        public static void Info(string area, string message, params object[] kv) => Write(LogType.Log, area, message, kv);
        public static void Warn(string area, string message, params object[] kv) => Write(LogType.Warning, area, message, kv);
        public static void Error(string area, string message, params object[] kv) => Write(LogType.Error, area, message, kv);

        /// <summary>True at most once per <paramref name="seconds"/> for this key (rate limiting for per-frame callers).</summary>
        public static bool Every(string key, float seconds)
        {
            float now = Time.realtimeSinceStartup;
            if (last.TryGetValue(key, out float t) && now - t < seconds) return false;
            last[key] = now;
            return true;
        }

        /// <summary>Times a phase: Trace.Phase("Reveal", "walls") … returns the elapsed ms when disposed.</summary>
        public static Timer Phase(string area, string what) => new Timer(area, what);

        public readonly struct Timer : IDisposable
        {
            readonly string area, what;
            readonly double start;
            public Timer(string area, string what) { this.area = area; this.what = what; start = Time.realtimeSinceStartupAsDouble; }
            public double Ms => (Time.realtimeSinceStartupAsDouble - start) * 1000.0;
            public void Dispose() => Info(area, what + " done", "ms", Math.Round(Ms));
        }

        static void Write(LogType type, string area, string message, object[] kv)
        {
            string state = "";
            try { state = State?.Invoke() ?? ""; } catch { }
            lock (sb)
            {
                sb.Clear();
                sb.Append("[Worldeck:").Append(area).Append("] ").Append(message);
                var fields = new Dictionary<string, object>(kv != null ? kv.Length / 2 + 4 : 4);
                if (kv != null)
                    for (int i = 0; i + 1 < kv.Length; i += 2)
                    {
                        string k = kv[i]?.ToString() ?? "?";
                        object v = kv[i + 1];
                        // Release builds keep the player's words and the room's coordinates out of the log (the privacy
                        // policy says counts and timings); test builds keep them for diagnosis.
                        if (Redacting() && (v is Vector3 || Private.Contains(k))) v = "-";
                        fields[k] = v is float f ? Math.Round(f, 3) : v is double d ? Math.Round(d, 3) : v is Vector3 p ? $"{p.x:0.00},{p.y:0.00},{p.z:0.00}" : v;
                        sb.Append(' ').Append(k).Append('=').Append(Fmt(fields[k]));
                    }
                sb.Append(" @").Append(Time.realtimeSinceStartup.ToString("0.0")).Append("s f").Append(Time.frameCount);
                if (state.Length > 0) sb.Append(' ').Append(state);
                string line = sb.ToString();
                switch (type)
                {
                    case LogType.Warning: Debug.LogWarning(line); break;
                    case LogType.Error: Debug.LogError(line); break;
                    default: Debug.Log(line); break;
                }
                var log = PlayLog.Instance;
                if (log != null)
                {
                    fields["area"] = area; fields["msg"] = message; fields["level"] = type.ToString();
                    fields["t"] = Math.Round(Time.realtimeSinceStartup, 2); fields["frame"] = Time.frameCount; fields["state"] = state;
                    log.Add("trace", fields);
                }
            }
        }

        static readonly HashSet<string> Private = new HashSet<string> { "name", "title", "text", "said", "saved" };
        static int redact = -1;
        static bool Redacting()
        {
            if (redact < 0) redact = Debug.isDebugBuild ? 0 : 1;
            return redact == 1;
        }

        static string Fmt(object v) => v switch
        {
            null => "null",
            string s => s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s,
            bool b => b ? "yes" : "no",
            _ => v.ToString()
        };
    }
}

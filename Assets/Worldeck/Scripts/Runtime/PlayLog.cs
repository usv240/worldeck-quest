using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// One JSON line per event in persistentDataPath/sessions/&lt;launch&gt;-worldeck.jsonl, kept on the headset:
    /// app start, room, each world opened (how it was asked for, how long words took to become a world),
    /// edits, voice and AI calls (latency, outcome: never the words or the audio), gather tasks, safety
    /// fades, health changes, errors (deduplicated) and frame times per world visit. Tools/stats.py reads it
    /// for the proof card ("words to world, median X s").
    /// </summary>
    public sealed class PlayLog : MonoBehaviour
    {
        public bool WriteFiles = true;
        public string Path { get; private set; } = "";
        public int Lines { get; private set; }
        public readonly ErrorTally Errors = new ErrorTally();
        public readonly FrameStats Frames = new FrameStats();
        public static PlayLog Instance { get; private set; }

        readonly List<string> pending = new List<string>();
        string launch = "";
        double visitStart;
        string visitBiome = "";
        bool skip;

        void Awake()
        {
            Instance = this;
            launch = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + UnityEngine.Random.Range(1000, 9999);
            if (WriteFiles)
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "sessions");
                try { Directory.CreateDirectory(dir); Path = System.IO.Path.Combine(dir, launch + "-worldeck.jsonl"); }
                catch (Exception e) { Debug.LogWarning("[Worldeck] play log off: " + e.Message); Path = ""; }
            }
            Frames.Reset(72);
            string build = Application.buildGUID ?? "";
            Add("app", new Dictionary<string, object>
            {
                { "version", Application.version }, { "unity", Application.unityVersion }, { "device", SystemInfo.deviceModel },
                { "kit", Kit.Version }, { "voice", AppConfig.HasVoice }, { "ai", AppConfig.HasAi },
                { "build", build.Length > 8 ? build.Substring(0, 8) : build }, { "debug", Debug.isDebugBuild },
                { "os", SystemInfo.operatingSystem }, { "mem_total_mb", SystemInfo.systemMemorySize }
            });
            if (WriteFiles && !string.IsNullOrEmpty(Path)) Housekeeping();
        }

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLog;
            Health.Changed += OnHealth;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Health.Changed -= OnHealth;
            EndVisit("closed");
            // A clean exit leaves this mark; a session file without it ended abruptly (killed, crashed, or the battery).
            Add("end", new Dictionary<string, object>
            {
                { "lines", Lines }, { "errors", Errors.Total }, { "exceptions", Errors.Exceptions }, { "kinds", Errors.Distinct }, { "overflow", Errors.Overflow }
            });
            Flush();
        }

        string pausedBiome = "";

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // The visit so far is written now: if the app never comes back, its frame times are not lost.
                pausedBiome = visitBiome;
                EndVisit("paused");
                skip = true;
                Flush();
            }
            else if (pausedBiome.Length > 0)
            {
                visitBiome = pausedBiome; pausedBiome = "";
                visitStart = Time.realtimeSinceStartupAsDouble;
                visitMemMb = MemMb();
                Frames.Reset(OVRManager.display != null ? OVRManager.display.displayFrequency : 72f);
                skip = true;
            }
        }

        /// <summary>Notes whether the last session ended cleanly, and keeps at most 30 session files.</summary>
        void Housekeeping()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                var files = new List<string>(Directory.GetFiles(dir, "*-worldeck.jsonl"));
                files.Sort(StringComparer.Ordinal);
                files.Remove(Path);
                if (files.Count > 0)
                {
                    string last = files[files.Count - 1], tail = "";
                    foreach (var line in File.ReadLines(last)) if (line.Length > 0) tail = line;
                    bool clean = tail.Contains("\"type\":\"end\"");
                    Add("previous", new Dictionary<string, object> { { "file", System.IO.Path.GetFileName(last) }, { "ended", clean ? "cleanly" : "abruptly" } });
                }
                for (int i = 0; i < files.Count - 29; i++) File.Delete(files[i]);
            }
            catch (Exception e) { Debug.LogWarning("[Worldeck] play log housekeeping: " + e.Message); }
        }

        static long MemMb() => UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
        long visitMemMb;

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Errors.Record(type.ToString(), message, stack, DateTime.UtcNow);
        }

        void OnHealth(Health.Entry e)
        {
            Add("health", new Dictionary<string, object> { { "part", e.Part }, { "level", e.Level.ToString() }, { "detail", e.Detail } });
            string line = $"[Worldeck:Health] {e.Part}: {e.Level} ({e.Detail})";
            if (e.Level == Health.Level.Fault) Debug.LogError(line); else if (e.Level == Health.Level.Fallback) Debug.LogWarning(line); else Debug.Log(line);
        }

        void Update()
        {
            if (skip || Time.timeScale == 0f) { skip = false; }
            else if (visitBiome.Length > 0) Frames.Add(Time.unscaledDeltaTime);
            foreach (var e in Errors.TakeNew())
                Add("error", new Dictionary<string, object>
                    { { "level", e.Level }, { "message", e.Message.Length > 200 ? e.Message.Substring(0, 200) : e.Message }, { "where", e.Where }, { "count", e.Count } });
            if (pending.Count > 0 && (Time.frameCount % 120 == 0 || pending.Count > 40)) Flush();
        }

        public void Add(string type, Dictionary<string, object> fields)
        {
            var o = new Dictionary<string, object> { { "type", type }, { "utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "launch", launch } };
            if (fields != null) foreach (var kv in fields) o[kv.Key] = kv.Value;
            pending.Add(Json.Write(o));
            Lines++;
            if (pending.Count > 400) pending.RemoveRange(0, pending.Count - 400);
        }

        /// <summary>A world became the current world: logs the previous visit's frame times.</summary>
        public void StartVisit(Recipe r, string how, float wordsToWorldMs, int triangles = -1, int renderers = -1, float buildMs = -1f)
        {
            EndVisit("changed");
            visitBiome = r.Biome;
            visitStart = Time.realtimeSinceStartupAsDouble;
            visitMemMb = MemMb();
            Frames.Reset(OVRManager.display != null ? OVRManager.display.displayFrequency : 72f);
            Add("world", new Dictionary<string, object>
            {
                { "biome", r.Biome }, { "line", r.Line() }, { "how", how }, { "ms", Math.Round(wordsToWorldMs) },
                { "code", ShareCode.Encode(r) }, { "tris", triangles }, { "renderers", renderers }, { "build_ms", Math.Round(buildMs) },
                { "mem_mb", visitMemMb }
            });
            Debug.Log($"[Worldeck] world: {r.Line()} via {how} in {wordsToWorldMs:0} ms");
        }

        public void EndVisit(string why)
        {
            if (visitBiome.Length == 0) return;
            var f = new Dictionary<string, object>
            {
                { "biome", visitBiome }, { "why", why }, { "seconds", Math.Round(Time.realtimeSinceStartupAsDouble - visitStart, 1) },
                { "frames", Frames.Count }
            };
            if (Frames.Count > 0)
            {
                f["hz"] = Frames.RefreshHz; f["fps_mean"] = Math.Round(Frames.MeanFps, 1);
                f["ms_p50"] = Math.Round(Frames.Percentile(0.5), 2); f["ms_p95"] = Math.Round(Frames.Percentile(0.95), 2);
                f["ms_p99"] = Math.Round(Frames.Percentile(0.99), 2); f["late_pct"] = Math.Round(Frames.LatePercent, 2);
                f["ms_max"] = Math.Round(Frames.MaxMs, 1);
            }
            f["mem_mb"] = MemMb(); f["mem_start_mb"] = visitMemMb;
            Add("visit", f);
            Debug.Log($"[Worldeck] visit: {visitBiome} {why}, {Frames.Line()}");
            visitBiome = "";
        }

        public void Flush()
        {
            if (pending.Count == 0 || string.IsNullOrEmpty(Path)) { if (string.IsNullOrEmpty(Path)) pending.Clear(); return; }
            try
            {
                File.AppendAllLines(Path, pending);
                pending.Clear();
                Health.Ok(Health.Log, "ok", Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Health.Set(Health.Log, Health.Level.Fallback, "write failed: " + e.Message, Time.realtimeSinceStartup);
                pending.Clear();
            }
        }
    }
}

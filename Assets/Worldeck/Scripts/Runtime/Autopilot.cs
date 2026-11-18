using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Development builds only: the app plays itself on the headset and measures. If a file named autopilot.txt is
    /// in the app's files folder (pushed with adb), it opens every world, stands, walks with the simulated stick,
    /// turns, jumps, comes home, and records the frame rate (average and worst frame) for each, then on-device
    /// voice's state, into autopilot-report.txt and the trace log. Nobody needs to wear the headset.
    /// </summary>
    public sealed class Autopilot : MonoBehaviour
    {
        public Director Director;
        public bool Running { get; private set; }

        IEnumerator Start()
        {
            if (!Debug.isDebugBuild || Director == null) yield break;
            string trigger = Path.Combine(Application.persistentDataPath, "autopilot.txt");
            if (!File.Exists(trigger)) yield break;
            Running = true;
            var report = new StringBuilder();
            report.AppendLine("Worldeck autopilot " + Application.version + " " + System.DateTime.UtcNow.ToString("u"));
            Trace.Info("Auto", "started");
            yield return new WaitForSecondsRealtime(8f);
            var d = Director;
            // The player's progress (keepsakes, chapter, worlds) is put back exactly as it was at the end.
            string kept = d.Progress.ToJsonString();
            if (!d.Progress.Onboarded) d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            report.AppendLine("resolution_scale " + UnityEngine.XR.XRSettings.eyeTextureResolutionScale + " hz " + (OVRManager.display != null ? OVRManager.display.displayFrequency : 0f));
            foreach (var b in Kit.Biomes)
            {
                d.Open(Recipe.For(b.Id, 3), "auto");
                float until = Time.realtimeSinceStartup + 40f;
                while (!(d.State == Director.Mode.World && d.Current != null && d.Current.Biome == b.Id) && Time.realtimeSinceStartup < until) yield return null;
                if (d.State != Director.Mode.World) { report.AppendLine(b.Id + " DID NOT OPEN state=" + d.State); Trace.Warn("Auto", "world did not open", "biome", b.Id); continue; }
                yield return new WaitForSecondsRealtime(5f);
                var still = new Meter(); yield return Measure(still, 5f);
                var moving = new Meter();
                if (d.Move != null)
                {
                    d.Move.SimulateSticks = true;
                    d.Move.SimLeftStick = new Vector2(0f, 1f);
                    yield return Measure(moving, 12f);
                    d.Move.SimLeftStick = Vector2.zero;
                    d.Move.SimRightStick = new Vector2(1f, 0f);
                    yield return null; yield return null;
                    d.Move.SimRightStick = Vector2.zero;
                    yield return new WaitForSecondsRealtime(0.5f);
                    d.Move.SimLeftStick = new Vector2(0f, 1f);
                    yield return Measure(moving, 8f);
                    d.Move.SimLeftStick = Vector2.zero;
                }
                float away = d.Move != null ? d.Move.FromCamp : 0f;
                string line = $"{b.Id,-9} still {still.Fps:0.0} fps (worst {still.WorstMs:0} ms)  moving {moving.Fps:0.0} fps (worst {moving.WorstMs:0} ms)  " +
                              $"went {away:0} m  wonders {(d.Wonders != null ? d.Wonders.All.Count : 0)}  sky {d.Look.PhotoId}  mem {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB";
                report.AppendLine(line);
                Trace.Info("Auto", "world", "biome", b.Id, "fps_still", still.Fps, "worst_still_ms", still.WorstMs, "fps_moving", moving.Fps,
                           "worst_moving_ms", moving.WorstMs, "went_m", away, "sky", d.Look.PhotoId);
                if (d.Move != null) { d.Move.Home(false, "auto"); d.Move.SimulateSticks = false; }
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "autopilot-report.txt"), report.ToString());
            }
            yield return MaraRun(d, report);
            d.Close("auto");
            float end = Time.realtimeSinceStartup + 15f;
            while (d.State != Director.Mode.Room && Time.realtimeSinceStartup < end) yield return null;
            report.AppendLine("closed to " + d.State);
            if (d.Ears != null)
            {
                if (d.Ears.State == Ears.Status.Off) d.Ears.Warm();
                float t = Time.realtimeSinceStartup + 25f;
                while (d.Ears.State == Ears.Status.Loading && Time.realtimeSinceStartup < t) yield return null;
                report.AppendLine("voice " + d.Ears.State + " " + d.Ears.Error);
            }
            d.ResetProgress(Progress.Parse(kept, out _));
            report.AppendLine("progress restored");
            report.AppendLine("done");
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "autopilot-report.txt"), report.ToString());
            Trace.Info("Auto", "done");
            Running = false;
        }

        /// <summary>Mara's call on the table: answer, choose fast, the storm with nobody blocking, one Rewind, the end.</summary>
        IEnumerator MaraRun(Director d, StringBuilder report)
        {
            if (d.Mission == null) { report.AppendLine("mara: no mission"); yield break; }
            d.Open(Recipe.For("campfire", 4), "auto");
            float until = Time.realtimeSinceStartup + 40f;
            while (!(d.State == Director.Mode.World && d.Current != null && d.Current.Biome == "campfire") && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            d.StartMission("auto");
            var m = d.Mission;
            var call = new Meter();
            until = Time.realtimeSinceStartup + 30f;
            while (!m.AwaitingChoice && Time.realtimeSinceStartup < until) { yield return Measure(call, 0.5f); }
            bool figure = m.Mara != null && m.Mara.IsFigure;
            m.Pick("m:guide");
            until = Time.realtimeSinceStartup + 30f;
            while (m.Now != Mission.Step.Pace && Time.realtimeSinceStartup < until) { yield return Measure(call, 0.5f); }
            m.Pick("m:fast");
            var storm = new Meter();
            bool rewound = false;
            until = Time.realtimeSinceStartup + 90f;
            while (d.Emergency != null && d.Emergency.Running && d.Emergency.Sim != null && d.Emergency.Sim.State.Phase < Sim.Aftermath && Time.realtimeSinceStartup < until)
            {
                yield return Measure(storm, 0.5f);
                if (!rewound && d.Emergency.Sim.State.Taken >= 2) { rewound = d.Emergency.DoRewind(30f); }
            }
            until = Time.realtimeSinceStartup + 30f;
            while (m.Active && m.Now != Mission.Step.Farewell && Time.realtimeSinceStartup < until) yield return null;
            string line = $"mara      call {call.Fps:0.0} fps (worst {call.WorstMs:0} ms)  storm {storm.Fps:0.0} fps (worst {storm.WorstMs:0} ms)  " +
                          $"figure {figure}  runway {m.RunwayLightCount}  rewound {rewound}  landing {m.Result}  voice {(RadioVoice.HasRecording(Story.Call(0)[0].Text) ? "recorded" : "murmur")}";
            report.AppendLine(line);
            Trace.Info("Auto", "mara", "fps_call", call.Fps, "worst_call_ms", call.WorstMs, "fps_storm", storm.Fps, "worst_storm_ms", storm.WorstMs,
                       "figure", figure, "runway", m.RunwayLightCount, "rewound", rewound, "landing", m.Result.ToString());
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "autopilot-report.txt"), report.ToString());
            m.Stop();
        }

        sealed class Meter
        {
            public int Frames; public float Seconds, WorstMs;
            public float Fps => Seconds > 0f ? Frames / Seconds : 0f;
        }

        static IEnumerator Measure(Meter m, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                m.Frames++; m.Seconds += dt;
                if (dt * 1000f > m.WorstMs) m.WorstMs = dt * 1000f;
            }
        }
    }
}

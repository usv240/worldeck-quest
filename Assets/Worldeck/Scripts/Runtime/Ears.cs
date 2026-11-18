using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Worldeck
{
    /// <summary>
    /// Voice on the headset itself: nothing you say leaves it, and no internet is needed. The microphone runs
    /// while ears are on; a voice detector (Silero VAD) cuts out each phrase you say, a small speech recogniser
    /// (Moonshine v2 base, through sherpa-onnx) turns it into text on a background thread, and a phrase counts
    /// only if it calls Juniper by name ("Juniper, make it rain"), or while Juniper is listening after a touch or a
    /// bare "Juniper". Quiet, far-away speech (a TV across the room) is ignored by loudness.
    /// Main thread: microphone samples in, results out. Worker thread: owns every native object.
    /// </summary>
    public sealed class Ears : MonoBehaviour
    {
        public enum Status { Off, Loading, Ready, Failed }

        public struct Heard
        {
            public string Text;       // what was heard, as the recogniser wrote it
            public string Command;    // what to act on ("" for a bare "Juniper")
            public bool Woke;         // Juniper was called by name
            public bool WasListening; // heard while Juniper was listening (no name needed)
            public float Seconds, DecodeMs, Rms;
        }

        [Tooltip("Phrases quieter than this (RMS) are ignored unless Juniper is listening: far-away voices, the TV.")]
        public float WakeRms = 0.012f;
        public float ListeningRms = 0.006f;
        public float MinSeconds = 0.35f;

        public Status State { get; private set; } = Status.Off;
        public string Error { get; private set; } = "";
        /// <summary>The microphone is on and phrases are being recognised.</summary>
        public bool On { get; private set; }
        /// <summary>Juniper is listening: the next phrase is a command, no name needed.</summary>
        public bool Listening => Time.unscaledTime < listenUntil;
        /// <summary>Microphone loudness, 0–1, for Juniper's glow.</summary>
        public float Level { get; private set; }
        /// <summary>Set while the headset is off or the Quest menu is up: results are dropped.</summary>
        public bool Muted;
        public int Phrases { get; private set; }
        public float LastDecodeMs { get; private set; }

        public event Action<Heard> HeardPhrase;
        /// <summary>The owner says whether words said without the name are plainly a command ("make it rain").</summary>
        public Func<string, bool> ClearCommand;
        public event Action Woke;
        public event Action<Status> Changed;

        /// <summary>On-device voice can work here: Android on the headset, with the models in the build.</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        const int Rate = 16000;
        const string ModelDir = "Voice";
        static readonly string[] ModelFiles =
        {
            "silero_vad.onnx", "moonshine-base-en/encoder_model.ort", "moonshine-base-en/decoder_model_merged.ort",
            "moonshine-base-en/tokens.txt"
        };

        AudioClip clip;
        string device;
        int readPos, clipRate;
        float listenUntil, micStartedAt;
        /// <summary>How long Juniper listens after hearing only its name.</summary>
        public const float ListenAfterName = 10f;
        long micSamples;
        float[] readBuf = new float[4096];
        float resamplePhase;
        float resampleLast;
        bool rateChecked, listenAsked;

        Thread worker;
        volatile bool stopWorker;
        readonly ConcurrentQueue<float[]> audioIn = new ConcurrentQueue<float[]>();
        readonly ConcurrentQueue<(string text, float seconds, float ms, float rms)> textOut = new ConcurrentQueue<(string, float, float, float)>();
        readonly ConcurrentQueue<string> logOut = new ConcurrentQueue<string>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool workerReady, workerFailed, workerDied;
        volatile string workerError = "";
        string root;
        bool debugBuild;

        // ---------- turning on and off ----------

        /// <summary>Loads the models (once) and starts the microphone. The microphone permission must already be granted.</summary>
        public void TurnOn()
        {
            if (!Supported) { Fail("not supported here"); return; }
            if (On) return;
            if (State == Status.Off || State == Status.Failed) StartCoroutine(Load());
            StartMic();
        }

        /// <summary>Loads the models and runs the self-test without the microphone (development builds).</summary>
        public void Warm()
        {
            if (!Supported || State != Status.Off) return;
            StartCoroutine(Load());
        }

        public void TurnOff()
        {
            StopMic();
            listenUntil = 0f;
        }

        /// <summary>The next phrase in <paramref name="seconds"/> is a command (Juniper was touched, or called by name).</summary>
        public void Listen(float seconds = 7f)
        {
            listenUntil = Time.unscaledTime + seconds;
            if (State != Status.Ready) listenAsked = true;
            if (!On) TurnOn();
        }

        public void StopListening() => listenUntil = 0f;

        void SetState(Status s)
        {
            if (State == s) return;
            State = s;
            Trace.Info("Ears", "state", "now", s.ToString(), "error", Error);
            Changed?.Invoke(s);
        }

        void Fail(string why)
        {
            Error = why;
            SetState(Status.Failed);
            StopMic();
        }

        // ---------- models: StreamingAssets (inside the APK) to files native code can open ----------

        IEnumerator Load()
        {
            SetState(Status.Loading);
            debugBuild = UnityEngine.Debug.isDebugBuild;
            float t0 = Time.realtimeSinceStartup;
            root = Path.Combine(Application.persistentDataPath, ModelDir);
            string stamp = Path.Combine(root, "version.txt");
            string version = Application.version + "/" + ModelFiles.Length + "/" + ModelFiles[1];
            bool fresh = File.Exists(stamp) && File.ReadAllText(stamp) == version;
            if (!fresh)
            {
                foreach (var f in ModelFiles)
                {
                    string to = Path.Combine(root, f);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    string from = Path.Combine(Application.streamingAssetsPath, ModelDir, f);
                    if (!from.Contains("://")) from = "file://" + from;
                    using (var req = UnityWebRequest.Get(from))
                    {
                        req.downloadHandler = new DownloadHandlerFile(to) { removeFileOnAbort = true };
                        yield return req.SendWebRequest();   // a local read from the APK, not the network
                        if (req.result != UnityWebRequest.Result.Success) { Fail("model copy failed: " + f + " " + req.error); yield break; }
                    }
                }
                File.WriteAllText(stamp, version);
            }
            Trace.Info("Ears", "models ready", "copied", !fresh, "ms", Mathf.Round((Time.realtimeSinceStartup - t0) * 1000f));
            stopWorker = false;
            workerReady = workerFailed = false;
            worker = new Thread(Work) { IsBackground = true, Name = "Worldeck ears", Priority = System.Threading.ThreadPriority.BelowNormal };
            worker.Start();
            while (!workerReady && !workerFailed) yield return null;
            if (workerFailed) { Fail(workerError); yield break; }
            Trace.Info("Ears", "recogniser loaded", "ms", Mathf.Round((Time.realtimeSinceStartup - t0) * 1000f));
            SetState(Status.Ready);
            if (listenAsked) { listenAsked = false; listenUntil = Time.unscaledTime + 8f; }
        }

        // ---------- the microphone (main thread) ----------

        void StartMic()
        {
            if (On) return;
            if (Microphone.devices.Length == 0) { Fail("no microphone"); return; }
            device = Microphone.devices[0];
            clip = Microphone.Start(device, true, 2, Rate);
            if (clip == null) { Fail("microphone would not start"); return; }
            clipRate = clip.frequency;
            readPos = 0;
            micSamples = 0;
            micStartedAt = Time.unscaledTime;
            rateChecked = false;
            resamplePhase = 0f;
            On = true;
            Trace.Info("Ears", "microphone on", "device", device, "clip_hz", clipRate);
        }

        void StopMic()
        {
            if (!On) return;
            On = false;
            if (device != null) Microphone.End(device);
            clip = null;
            Level = 0f;
            Trace.Info("Ears", "microphone off");
        }

        void Update()
        {
            while (logOut.TryDequeue(out var line)) Trace.Info("Ears", line);
            if (workerDied && State == Status.Ready) { workerDied = false; while (audioIn.TryDequeue(out var drop)) { } Fail(workerError); }
            while (textOut.TryDequeue(out var r)) Deliver(r.text, r.seconds, r.ms, r.rms);
            if (!On || clip == null) return;
            int pos = Microphone.GetPosition(device);
            if (pos < 0 || pos == readPos) return;
            int total = clip.samples;
            int n = pos > readPos ? pos - readPos : total - readPos + pos;
            if (n <= 0 || n > total) { readPos = pos; return; }
            if (readBuf.Length < n) readBuf = new float[Mathf.NextPowerOfTwo(n)];
            // Read across the wrap point in up to two pieces.
            int first = Mathf.Min(n, total - readPos);
            var a = new float[first];
            clip.GetData(a, readPos);
            Array.Copy(a, 0, readBuf, 0, first);
            if (n > first)
            {
                var b = new float[n - first];
                clip.GetData(b, 0);
                Array.Copy(b, 0, readBuf, first, n - first);
            }
            readPos = pos;
            micSamples += n;
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += readBuf[i] * readBuf[i];
            Level = Mathf.Lerp(Level, Mathf.Clamp01(Mathf.Sqrt(sum / n) * 12f), 0.4f);
            // The rate Unity says and the rate the microphone delivers can differ on Android: measure once.
            if (!rateChecked && Time.unscaledTime - micStartedAt > 3f)
            {
                rateChecked = true;
                float measured = micSamples / (Time.unscaledTime - micStartedAt);
                Trace.Info("Ears", "microphone rate", "clip_hz", clipRate, "measured_hz", Mathf.Round(measured));
                if (Mathf.Abs(measured - clipRate) > clipRate * 0.2f)
                {
                    int[] rates = { 16000, 22050, 24000, 32000, 44100, 48000 };
                    int best = clipRate;
                    foreach (var r in rates) if (Mathf.Abs(r - measured) < Mathf.Abs(best - measured)) best = r;
                    clipRate = best;
                    Trace.Warn("Ears", "using the measured microphone rate", "hz", clipRate);
                }
            }
            if (State != Status.Ready) return;   // still loading: drop audio (nothing is buffered without consent anyway)
            var chunk = Resample(readBuf, n, clipRate);
            if (chunk.Length > 0) { audioIn.Enqueue(chunk); wake.Set(); }
        }

        /// <summary>To 16 kHz: a box filter over each output step, then linear interpolation (plenty for speech).</summary>
        float[] Resample(float[] src, int n, int rate)
        {
            if (rate == Rate)
            {
                var same = new float[n];
                Array.Copy(src, same, n);
                return same;
            }
            float step = rate / (float)Rate;
            int outN = Mathf.FloorToInt((n - resamplePhase) / step);
            if (outN <= 0) return Array.Empty<float>();
            var dst = new float[outN];
            int span = Mathf.Max(1, Mathf.RoundToInt(step));
            for (int i = 0; i < outN; i++)
            {
                float x = resamplePhase + i * step;
                int k = (int)x;
                float acc = 0f; int cnt = 0;
                for (int j = 0; j < span && k + j < n; j++) { acc += src[k + j]; cnt++; }
                dst[i] = cnt > 0 ? acc / cnt : resampleLast;
            }
            resamplePhase = resamplePhase + outN * step - n;
            if (resamplePhase < 0f) resamplePhase = 0f;
            resampleLast = src[n - 1];
            return dst;
        }

        // ---------- results (main thread) ----------

        void Deliver(string text, float seconds, float ms, float rms)
        {
            Phrases++;
            LastDecodeMs = ms;
            if (debugBuild)
            {
                // Development builds only: what was heard, kept on the headset so recognition can be tuned. Never in release.
                UnityEngine.Debug.Log("[Worldeck:VoiceText] heard \"" + text + "\" listening=" + Listening);
                string heardPath = Path.Combine(Application.persistentDataPath, "dev-heard.txt");
                try { if (File.Exists(heardPath) && new FileInfo(heardPath).Length > 512 * 1024) File.Delete(heardPath); } catch (Exception) { }
                try { File.AppendAllText(heardPath, Time.realtimeSinceStartup.ToString("0.0") + "\t" + seconds.ToString("0.00") + "s\t" + (Listening ? "L" : "-") + "\t" + text + "\n"); }
                catch (Exception) { }
            }
            bool listening = Listening;
            bool woke = WakeWord.Find(text, out string command);
            Trace.Info("Ears", "phrase", "seconds", seconds, "decode_ms", Mathf.Round(ms), "rms", rms, "woke", woke, "listening", listening,
                       "words", WakeWord.Normalize(text).Split(' ').Length, "muted", Muted);
            if (Muted) return;
            if (!woke && !listening)
            {
                // Not called by name: only a plain command, said clearly, counts. Everything else is forgotten at once.
                string said = WakeWord.Normalize(text);
                if (ClearCommand == null || rms < WakeRms || !ClearCommand(said)) return;
                Trace.Info("Ears", "clear command without the name");
            }
            if (!woke && rms < ListeningRms) return;
            if (woke && !listening && rms < WakeRms) { Trace.Info("Ears", "name heard but too quiet: ignored", "rms", rms); return; }
            if (woke && command.Length == 0)
            {
                // A bare "Juniper": the next phrase is the command. Ten seconds: people stop to think what to ask for
                // (Nov 12: "I had one person" came seven seconds after the name, and was lost at six).
                listenUntil = Time.unscaledTime + ListenAfterName;
                Woke?.Invoke();
                return;
            }
            string act = woke ? command : WakeWord.Normalize(text);
            if (!WakeWord.IsCommand(act)) return;
            listenUntil = 0f;
            HeardPhrase?.Invoke(new Heard { Text = text, Command = act, Woke = woke, WasListening = listening, Seconds = seconds, DecodeMs = ms, Rms = rms });
        }

        // ---------- the worker thread: every native object lives here ----------

        void Work()
        {
            SherpaOnnx.VoiceActivityDetector vad = null;
            SherpaOnnx.OfflineRecognizer asr = null;
            try
            {
                var vc = SherpaOnnx.VadModelConfig.Default();
                vc.SileroVad.Model = Path.Combine(root, "silero_vad.onnx");
                vc.SileroVad.Threshold = 0.4f;
                vc.SileroVad.MinSilenceDuration = 0.8f;
                vc.SileroVad.MinSpeechDuration = 0.2f;
                vc.SileroVad.MaxSpeechDuration = 8f;
                vc.SampleRate = Rate;
                vc.NumThreads = 1;
                vad = new SherpaOnnx.VoiceActivityDetector(vc, 30f);

                var rc = SherpaOnnx.OfflineRecognizerConfig.Default();
                rc.FeatConfig.SampleRate = Rate;
                rc.ModelConfig.Moonshine.Encoder = Path.Combine(root, "moonshine-base-en/encoder_model.ort");
                rc.ModelConfig.Moonshine.MergedDecoder = Path.Combine(root, "moonshine-base-en/decoder_model_merged.ort");
                rc.ModelConfig.Tokens = Path.Combine(root, "moonshine-base-en/tokens.txt");
                rc.ModelConfig.NumThreads = 3;
                rc.ModelConfig.Provider = "cpu";
                rc.DecodingMethod = "greedy_search";
                asr = new SherpaOnnx.OfflineRecognizer(rc);
                SelfTest(asr);
                workerReady = true;
            }
            catch (Exception e)
            {
                workerError = "load: " + e.GetType().Name + " " + e.Message;
                workerFailed = true;
                vad?.Dispose(); asr?.Dispose();
                return;
            }

            var window = new float[512];
            int filled = 0;
            try
            {
                while (!stopWorker)
                {
                    if (!audioIn.TryDequeue(out var chunk)) { wake.WaitOne(200); continue; }
                    int i = 0;
                    while (i < chunk.Length)
                    {
                        int take = Math.Min(window.Length - filled, chunk.Length - i);
                        Array.Copy(chunk, i, window, filled, take);
                        filled += take; i += take;
                        if (filled < window.Length) break;
                        vad.AcceptWaveform(window);
                        filled = 0;
                        while (!vad.IsEmpty())
                        {
                            var seg = vad.Front();
                            vad.Pop();
                            var s = seg.Samples;
                            float seconds = s.Length / (float)Rate;
                            if (seconds < MinSeconds) continue;
                            double sum = 0; for (int k = 0; k < s.Length; k++) sum += s[k] * s[k];
                            float rms = (float)Math.Sqrt(sum / Math.Max(1, s.Length));
                            if (rms < ListeningRms * 0.5f) continue;   // too quiet to be anyone at the table
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            using (var st = asr.CreateStream())
                            {
                                st.AcceptWaveform(Rate, s);
                                asr.Decode(st);
                                string text = st.Result.Text ?? "";
                                textOut.Enqueue((text, seconds, (float)sw.Elapsed.TotalMilliseconds, rms));
                            }
                        }
                    }
                }
            }
            catch (Exception e) { workerError = "worker stopped: " + e.GetType().Name + " " + e.Message; logOut.Enqueue(workerError); workerDied = true; }
            finally { vad?.Dispose(); asr?.Dispose(); }
        }

        /// <summary>Development builds: transcribe the model's own sample recording once and log the result and time.</summary>
        void SelfTest(SherpaOnnx.OfflineRecognizer asr)
        {
            if (!debugBuild) return;
            string wav = Path.Combine(root, "moonshine-base-en/selftest.wav");
            if (!File.Exists(wav)) return;
            var samples = ReadWav(File.ReadAllBytes(wav), out int rate);
            if (samples == null) { logOut.Enqueue("self-test: unreadable wav"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var st = asr.CreateStream())
            {
                st.AcceptWaveform(rate, samples);
                asr.Decode(st);
                logOut.Enqueue("self-test: \"" + st.Result.Text + "\" audio_s=" + (samples.Length / (float)rate).ToString("0.0") +
                               " decode_ms=" + Math.Round(sw.Elapsed.TotalMilliseconds));
            }
        }

        /// <summary>16-bit PCM WAV (mono or the first channel) to floats.</summary>
        public static float[] ReadWav(byte[] b, out int rate)
        {
            rate = 0;
            if (b == null || b.Length < 44 || b[0] != 'R' || b[8] != 'W') return null;
            int channels = 1, bits = 16, i = 12;
            while (i + 8 <= b.Length)
            {
                int size = BitConverter.ToInt32(b, i + 4);
                string id = System.Text.Encoding.ASCII.GetString(b, i, 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(b, i + 10); rate = BitConverter.ToInt32(b, i + 12); bits = BitConverter.ToInt16(b, i + 22); }
                else if (id == "data")
                {
                    if (bits != 16 || channels < 1) return null;
                    int frames = Math.Min(size, b.Length - i - 8) / (2 * channels);
                    var f = new float[frames];
                    for (int k = 0; k < frames; k++) f[k] = BitConverter.ToInt16(b, i + 8 + k * 2 * channels) / 32768f;
                    return f;
                }
                i += 8 + size + (size & 1);
            }
            return null;
        }

        void OnApplicationPause(bool paused)
        {
            // The microphone is released while the app is in the background, and comes back with it.
            if (paused) { wasOn = On; StopMic(); }
            else if (wasOn) StartMic();
        }
        bool wasOn;

        void OnDestroy()
        {
            StopMic();
            stopWorker = true;
            wake.Set();
            if (worker != null && worker.IsAlive) worker.Join(500);
        }
    }
}

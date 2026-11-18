using System;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The microphone, only while the guide is listening: records 16 kHz mono, tracks the level for the
    /// guide, and stops after 1.2 s of quiet once speech has started (or 8 s, or a second touch). Asks for
    /// the microphone permission the first time it's needed, never at launch.
    /// </summary>
    public sealed class MicCapture : MonoBehaviour
    {
        public const int Rate = 16000;
        public float MaxSeconds = 8f;
        public float SilenceToStop = 1.2f;
        public float SpeechLevel = 0.025f;

        public bool Listening { get; private set; }
        public float Level { get; private set; }
        public bool HeardSpeech { get; private set; }
        public string Device { get; private set; }
        public event Action<short[]> Finished;
        public event Action<string> Failed;

        AudioClip clip;
        float startedAt, lastLoud;
        int lastPos;
        readonly float[] window = new float[512];

        public static bool HasPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
                return true;
#endif
            }
        }

        public void RequestPermission(Action<bool> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (HasPermission) { done?.Invoke(true); return; }
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => done?.Invoke(true);
            cb.PermissionDenied += _ => done?.Invoke(false);
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
#else
            done?.Invoke(true);
#endif
        }

        public bool Begin()
        {
            if (Listening) return true;
            if (Microphone.devices.Length == 0) { Failed?.Invoke("no microphone"); return false; }
            Device = Microphone.devices[0];
            clip = Microphone.Start(Device, false, Mathf.CeilToInt(MaxSeconds) + 1, Rate);
            if (clip == null) { Failed?.Invoke("microphone would not start"); return false; }
            Listening = true;
            HeardSpeech = false;
            startedAt = Time.unscaledTime;
            Trace.Info("Voice", "mic on", "device", Device);
            lastLoud = startedAt;
            lastPos = 0;
            Level = 0f;
            return true;
        }

        public void End() => Stop(true);
        public void Cancel() => Stop(false);

        void Stop(bool deliver)
        {
            if (!Listening) return;
            Listening = false;
            Trace.Info("Voice", "mic off", "deliver", deliver, "heard_speech", HeardSpeech, "seconds", Time.unscaledTime - startedAt);
            int pos = Microphone.GetPosition(Device);
            Microphone.End(Device);
            if (!deliver || clip == null) return;
            int n = pos > 0 ? pos : Mathf.Min(clip.samples, Mathf.RoundToInt((Time.unscaledTime - startedAt) * Rate));
            if (n < Rate / 4) { Failed?.Invoke("too short"); return; }
            var f = new float[n];
            clip.GetData(f, 0);
            var pcm = new short[n];
            for (int i = 0; i < n; i++) pcm[i] = (short)Mathf.Clamp(f[i] * 32767f, -32768f, 32767f);
            Finished?.Invoke(pcm);
        }

        void Update()
        {
            if (!Listening) return;
            int pos = Microphone.GetPosition(Device);
            if (pos > window.Length && clip != null)
            {
                clip.GetData(window, pos - window.Length);
                float sum = 0f;
                for (int i = 0; i < window.Length; i++) sum += window[i] * window[i];
                float rms = Mathf.Sqrt(sum / window.Length);
                Level = Mathf.Lerp(Level, Mathf.Clamp01(rms * 12f), 0.4f);
                if (rms > SpeechLevel) { lastLoud = Time.unscaledTime; HeardSpeech = true; }
            }
            lastPos = pos;
            float t = Time.unscaledTime;
            if (t - startedAt > MaxSeconds) End();
            else if (HeardSpeech && t - lastLoud > SilenceToStop) End();
            else if (!HeardSpeech && t - startedAt > 5f) Stop(false);
        }

        void OnApplicationPause(bool paused) { if (paused) Cancel(); }
        void OnDisable() => Cancel();
    }
}

using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Mara's voice over the radio. Every line she can say is recorded ahead of time with an open voice model,
    /// Kokoro-82M (Apache-2.0) (Resources/MaraVoice, one clip per line, named by <see cref="Story.VoiceKey"/>); a line that was never
    /// recorded is said sentence by sentence, and anything still missing falls back to a murmur made in code. All of
    /// it goes through a radio band and breaks up with static as her signal fades. Nothing is downloaded or sent.
    /// </summary>
    public static class RadioVoice
    {
        public const int Rate = 16000;
        public const float MaxSeconds = 4.2f;

        // Vowel formants (F1, F2) in Hz: a, e, i, o, u.
        static readonly float[,] Vowels = { { 800f, 1250f }, { 520f, 1900f }, { 330f, 2350f }, { 560f, 950f }, { 360f, 820f } };

        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        /// <summary>The voice for a line (cached; static is added for a weak signal).</summary>
        public static AudioClip Make(string text, float signal)
        {
            string key = Mathf.RoundToInt(Mathf.Clamp01(signal) * 4f) + "|" + text;
            if (cache.TryGetValue(key, out var c) && c != null) return c;
            if (cache.Count > 64) cache.Clear();
            var d = Recorded(text);
            if (d != null) d = Radio((float[])d.Clone(), signal, (text ?? "").GetHashCode());   // the recording stays clean in its cache
            else d = Samples(text, signal);
            c = AudioClip.Create("Mara says", d.Length, 1, Rate, false);
            c.SetData(d, 0);
            cache[key] = c;
            return c;
        }

        /// <summary>True if this line (or every sentence of it) was recorded.</summary>
        public static bool HasRecording(string text) => Recorded(text) != null;

        public const string Folder = "MaraVoice/";

        /// <summary>The recorded line at <see cref="Rate"/>, or null: the whole line, else its sentences one by one.</summary>
        static float[] Recorded(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var whole = Load(text);
            if (whole != null) return whole;
            var parts = new List<float[]>();
            foreach (var s in Story.Sentences(text))
            {
                var p = Load(s);
                // "Your sofa and shelf kept me on course.": every pair was not recorded; the plain version was.
                if (p == null && s.StartsWith("Your ") && s.EndsWith(" kept me on course.")) p = Load(Story.HeldAny);
                if (p == null) return null;
                parts.Add(p);
            }
            if (parts.Count == 0) return null;
            int gap = Mathf.RoundToInt(0.12f * Rate), n = 0;
            foreach (var p in parts) n += p.Length + gap;
            var d = new float[n];
            int at = 0;
            foreach (var p in parts) { System.Array.Copy(p, 0, d, at, p.Length); at += p.Length + gap; }
            return d;
        }

        static readonly Dictionary<string, float[]> recordings = new Dictionary<string, float[]>();

        static float[] Load(string text)
        {
            string k = Story.VoiceKey(text);
            if (recordings.TryGetValue(k, out var have)) return have;
            float[] d = null;
            var clip = Resources.Load<AudioClip>(Folder + k);
            if (clip != null && clip.LoadAudioData() && clip.samples > 0)
            {
                var raw = new float[clip.samples * clip.channels];
                if (clip.GetData(raw, 0))
                {
                    int ch = Mathf.Max(1, clip.channels);
                    var mono = new float[clip.samples];
                    for (int i = 0; i < mono.Length; i++) { float sum = 0f; for (int j = 0; j < ch; j++) sum += raw[i * ch + j]; mono[i] = sum / ch; }
                    d = clip.frequency == Rate ? mono : Resample(mono, clip.frequency, Rate);
                }
            }
            if (recordings.Count > 80) recordings.Clear();
            recordings[k] = d;
            return d;
        }

        static float[] Resample(float[] src, int from, int to)
        {
            int n = Mathf.Max(1, (int)((long)src.Length * to / from));
            var d = new float[n];
            float step = from / (float)to;
            for (int i = 0; i < n; i++)
            {
                float x = i * step;
                int k = (int)x;
                float f = x - k;
                float a = src[Mathf.Min(k, src.Length - 1)], b = src[Mathf.Min(k + 1, src.Length - 1)];
                d[i] = a + (b - a) * f;
            }
            return d;
        }

        /// <summary>The samples (16 kHz mono), deterministic for a text and signal.</summary>
        public static float[] Samples(string text, float signal)
        {
            text = text ?? "";
            signal = Mathf.Clamp01(signal);
            int syl = Mathf.Clamp(Guide.Syllables(text), 2, 40);
            const float sylDur = 0.165f;
            float total = Mathf.Min(syl * sylDur + 0.3f, MaxSeconds);
            int count = Mathf.Max(1, Mathf.FloorToInt((total - 0.3f) / sylDur));
            int n = Mathf.CeilToInt(total * Rate);
            var d = new float[n];
            var rng = new Rng(text.GetHashCode() * 31 + 7);
            bool question = text.TrimEnd().EndsWith("?");
            var table = new float[256];
            float phase = 0f;
            for (int s = 0; s < count; s++)
            {
                // One period of this syllable's vowel: harmonics of the pitch, loudest near the two formants.
                int v = rng.Range(0, 5);
                float f1 = Vowels[v, 0] * rng.Range(0.92f, 1.08f), f2 = Vowels[v, 1] * rng.Range(0.92f, 1.08f);
                float along = s / (float)Mathf.Max(1, count - 1);
                float f0 = 212f * (1.08f - 0.16f * along) * (1f + 0.05f * Mathf.Sin(s * 1.9f));
                if (question && s >= count - 2) f0 *= 1.18f;
                int harmonics = Mathf.Min(14, Mathf.FloorToInt(3200f / f0));
                for (int i = 0; i < table.Length; i++)
                {
                    float x = i / (float)table.Length * Mathf.PI * 2f, sum = 0f;
                    for (int k = 1; k <= harmonics; k++)
                    {
                        float hz = k * f0;
                        float a = Mathf.Exp(-Sq((hz - f1) / 140f)) + 0.65f * Mathf.Exp(-Sq((hz - f2) / 210f)) + 0.04f / k;
                        sum += a * Mathf.Sin(k * x);
                    }
                    table[i] = sum;
                }
                int start = Mathf.FloorToInt((0.08f + s * sylDur) * Rate);
                int len = Mathf.FloorToInt(sylDur * rng.Range(0.72f, 0.95f) * Rate);
                bool burst = rng.Next01() < 0.55f;
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = i / (float)Rate, dur = len / (float)Rate;
                    float env = Mathf.Clamp01(t / 0.018f) * Mathf.Clamp01((dur - t) / 0.045f);
                    phase += f0 * (1f + 0.012f * Mathf.Sin(t * 31f)) / Rate;
                    if (phase >= 1f) phase -= 1f;
                    float voiced = table[(int)(phase * table.Length) & 255] * env;
                    // A soft consonant at the start of some syllables.
                    float noise = burst && t < 0.03f ? (rng.Next01() * 2f - 1f) * (1f - t / 0.03f) * 0.5f : 0f;
                    d[start + i] += voiced * 0.45f + noise;
                }
            }
            return Radio(d, signal, text.GetHashCode() * 17 + 3);
        }

        /// <summary>The radio: a 300–3400 Hz band, a little static, and dropouts as the signal fades.</summary>
        static float[] Radio(float[] d, float signal, int seed)
        {
            signal = Mathf.Clamp01(signal);
            int n = d.Length;
            var rng = new Rng(seed);
            float hp = 0f, prev = 0f, lp = 0f, peak = 1e-4f;
            float hpA = 1f / (1f + 2f * Mathf.PI * 300f / Rate), lpA = 1f - Mathf.Exp(-2f * Mathf.PI * 3400f / Rate);
            float hissAmt = 0.03f + 0.25f * (1f - signal);
            bool cut = false;
            for (int i = 0; i < n; i++)
            {
                float x = d[i];
                hp = hpA * (hp + x - prev); prev = x;
                lp += (hp - lp) * lpA;
                if (signal < 0.7f && i % 800 == 0) cut = rng.Next01() < (0.7f - signal) * 0.6f;
                // A touch of drive: a radio voice is a little crunchy.
                float v = lp * 2.6f; v = v / (1f + Mathf.Abs(v));
                float y = (cut ? v * 0.15f : v) + (rng.Next01() * 2f - 1f) * hissAmt * 0.2f;
                d[i] = y;
                if (Mathf.Abs(y) > peak) peak = Mathf.Abs(y);
            }
            float g = 0.95f / peak;
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * g, -1f, 1f);
            return d;
        }

        static float Sq(float x) => x * x;
    }
}

using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Someone who lives in this world (Locals): they stand just past where your walls were, breathe, turn to face
    /// you, and notice when you look at them. Wave and they wave back (a rock from foot to foot and a little hop);
    /// when they speak they nod. What they say is the Director's to show.
    /// </summary>
    public sealed class Local : MonoBehaviour
    {
        public LocalDef Def;
        public Transform Head;
        /// <summary>How tall they stand (metres).</summary>
        public float Height = 1.7f;
        /// <summary>You have been looking at them for a moment (or just spoke to them).</summary>
        public bool Watched { get; private set; }
        /// <summary>They have said hello on this visit.</summary>
        public bool Greeted;
        /// <summary>How many times they have waved back (for tests).</summary>
        public int Waves { get; private set; }
        /// <summary>True while they are waving.</summary>
        public bool Waving => Time.unscaledTime < waveUntil;
        /// <summary>How far they lean just now, degrees: side to side (a wave) and forwards (a nod). For tests.</summary>
        public float Roll { get; private set; }
        public float Pitch { get; private set; }

        public const float WaveSeconds = 1.6f, NodSeconds = 0.7f;

        Vector3 scale, foot;
        Quaternion facing;
        float watched, phase, waveUntil = -10f, nodUntil = -10f;
        bool started;

        void Start() { Begin(); }

        void Begin()
        {
            if (started) return;
            started = true;
            scale = transform.localScale;
            foot = transform.position;
            facing = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            phase = Random.value * 10f;
        }

        /// <summary>Where their words go: just above their head.</summary>
        public Vector3 HeadTop => (started ? foot : transform.position) + Vector3.up * (Height + 0.22f);

        /// <summary>Spoken to: as good as being looked at.</summary>
        public void Notice() { watched = Mathf.Max(watched, 4f); Watched = true; }

        /// <summary>You waved: they wave back.</summary>
        public void Wave()
        {
            Begin();
            if (Waving) return;
            waveUntil = Time.unscaledTime + WaveSeconds;
            Waves++;
            Notice();
        }

        /// <summary>They are saying something: a nod.</summary>
        public void Nod() { Begin(); nodUntil = Time.unscaledTime + NodSeconds; }

        /// <summary>You are in front of them and looking roughly their way (so a wave is to them).</summary>
        public bool InView(float degrees)
        {
            if (Head == null) return false;
            Vector3 seen = transform.position + Vector3.up * Height * 0.6f - Head.position;
            return seen.magnitude < 30f && Vector3.Angle(Head.forward, seen) < degrees;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || Time.timeScale <= 0f || Head == null) return;   // time frozen: so are they
            Begin();
            float t = Time.unscaledTime;
            transform.localScale = new Vector3(scale.x, scale.y * (1f + 0.008f * Mathf.Sin((t + phase) * 1.5f)), scale.z);
            bool inView = InView(13f);
            watched = inView ? Mathf.Min(6f, watched + dt) : Mathf.Max(0f, watched - dt * 0.8f);
            Watched = watched > 0.7f;
            Vector3 to = Head.position - foot; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) facing = Quaternion.RotateTowards(facing, Quaternion.LookRotation(to.normalized, Vector3.up), 70f * dt);
            // A wave: rocking from foot to foot, three times, with a little hop; it eases in and out.
            float roll = 0f, hop = 0f, pitch = 0f;
            if (t < waveUntil)
            {
                float k = 1f - (waveUntil - t) / WaveSeconds;
                float ease = Mathf.Sin(k * Mathf.PI);
                roll = Mathf.Sin(k * Mathf.PI * 6f) * 11f * ease;
                hop = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * 0.06f * ease * Height / 1.7f;
            }
            // A nod: forwards and back, once.
            if (t < nodUntil) pitch = Mathf.Sin((1f - (nodUntil - t) / NodSeconds) * Mathf.PI) * 9f;
            Roll = roll; Pitch = pitch;
            transform.SetPositionAndRotation(foot + Vector3.up * hop, facing * Quaternion.Euler(pitch, 0f, roll));
        }
    }
}

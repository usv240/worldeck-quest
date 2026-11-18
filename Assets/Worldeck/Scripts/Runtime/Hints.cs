using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Just-in-time labels: one short line next to the thing your hand (or eyes) is on, saying what to do with
    /// it ("Pinch to pick up", "Let go over the ring", "Pull it out now!"). Each frame, parts of the app ask
    /// for a label with <see cref="Request"/>; the most important request wins and shows, the label follows its
    /// thing, and it disappears a moment after nobody asks. A hint stops showing once the player has done the
    /// thing a few times (<see cref="Learned"/>; remembered on the device). Hovered buttons with a
    /// <see cref="PokeButton.Hint"/> get their label here too.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class Hints : MonoBehaviour
    {
        public static Hints Instance { get; private set; }
        public Transform Head;
        public Font Font;
        public Material Panel;
        [Tooltip("Seconds a label stays after the last request (so it doesn't flicker at the edge of reach).")]
        public float Linger = 1.0f;
        public float MinShow = 1.5f;
        public float Cooldown = 10f;
        /// <summary>Set while the coach's hand is showing something: only urgent hints (priority 4+) appear.</summary>
        public bool Quiet;

        Caption label;
        string bestId, bestText;
        Vector3 bestAt;
        int bestPriority = int.MinValue;
        string shownId = "";
        float lastAsked = -10f, shownSince = -10f;
        readonly Dictionary<string, float> hiddenAt = new Dictionary<string, float>();
        readonly Dictionary<string, int> uses = new Dictionary<string, int>();

        public string Showing => label != null && label.Showing ? shownId : "";

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (Font == null) return;
            label = Caption.Make("Hint", transform, Font, Panel, Head, 13f, 22f);
            label.Priority = 3;
            label.LineChars = 26;
        }

        /// <summary>How many times the player has done what hint <paramref name="id"/> teaches.</summary>
        public int Count(string id)
        {
            if (uses.TryGetValue(id, out int n)) return n;
            try { n = PlayerPrefs.GetInt(Key(id), 0); } catch { n = 0; }
            uses[id] = n;
            return n;
        }

        int generation = -1;
        /// <summary>Where a hint's count is kept: a new player (Forget) starts a new set of keys.</summary>
        string Key(string id)
        {
            if (generation < 0) { try { generation = PlayerPrefs.GetInt("hint.generation", 0); } catch { generation = 0; } }
            return generation == 0 ? "hint." + id : "hint." + generation + "." + id;
        }

        /// <summary>A new player: nothing has been learned yet, so every hint is shown again.</summary>
        public void Forget()
        {
            Key("");
            generation++;
            uses.Clear();
            try { PlayerPrefs.SetInt("hint.generation", generation); PlayerPrefs.Save(); } catch { }
            Trace.Info("Hints", "forgotten: a new player", "generation", generation);
        }

        /// <summary>True while hint <paramref name="id"/> is still worth showing (done fewer than <paramref name="times"/> times).</summary>
        public bool Wants(string id, int times = 2) => Count(id) < times;

        /// <summary>The player did the thing hint <paramref name="id"/> teaches.</summary>
        public void Learned(string id)
        {
            int n = Count(id) + 1;
            uses[id] = n;
            try { PlayerPrefs.SetInt(Key(id), n); } catch { }
            if (n == 1) Trace.Info("Hints", "learned", "hint", id);
        }

        /// <summary>Asks for a label at <paramref name="at"/> this frame. The highest priority this frame wins.</summary>
        public void Request(string id, Vector3 at, string text, int priority = 0)
        {
            if (string.IsNullOrEmpty(text) || priority < bestPriority) return;
            if (Quiet && priority < 4) return;
            // Not straight back after it went away (a hand hovering at the edge of reach made it flicker).
            if (id != shownId && priority < 3 && hiddenAt.TryGetValue(id, out float gone) && Time.unscaledTime - gone < Cooldown) return;
            if (priority == bestPriority && id != shownId) return;   // ties keep what's already showing
            bestPriority = priority; bestId = id; bestText = text; bestAt = at;
        }

        void OnApplicationPause(bool paused) { if (paused) { try { PlayerPrefs.Save(); } catch { } } }

        void LateUpdate()
        {
            // Buttons: what a hovered button does, until it's been pressed a few times.
            foreach (var b in PokeButton.Live)
            {
                if (b == null || !b.FingerHover || string.IsNullOrEmpty(b.Hint) || !b.isActiveAndEnabled) continue;
                if (!Wants("button:" + b.Hint, 3)) continue;
                Request("button:" + b.Hint, b.transform.position + b.transform.up * (b.Size.y * 0.5f * b.transform.lossyScale.y + 0.028f), b.Hint, -1);
            }
            if (label == null) { bestPriority = int.MinValue; return; }
            float now = Time.unscaledTime;
            if (bestPriority > int.MinValue)
            {
                // A label that has just appeared stays a moment, even if something else asks.
                if (bestId != shownId && label.Showing && now - shownSince < MinShow) { bestPriority = int.MinValue; bestId = bestText = null; return; }
                if (bestId != shownId || !label.Showing)
                {
                    if (shownId.Length > 0 && bestId != shownId) hiddenAt[shownId] = now;
                    shownSince = now;
                    label.Say(bestText, 0f, new Color(1f, 0.93f, 0.8f));
                    label.transform.position = bestAt;
                    if (bestId != shownId) Trace.Info("Hints", "shown", "hint", bestId, "text", bestText);
                    shownId = bestId;
                }
                else if (label.Text != null && label.Text.text != Caption.Wrap(bestText, label.LineChars)) label.Say(bestText, 0f, new Color(1f, 0.93f, 0.8f));
                label.transform.position = Vector3.Lerp(label.transform.position, bestAt, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
                lastAsked = now;
            }
            else if (label.Showing && now - lastAsked > Linger && now - shownSince > MinShow)
            {
                hiddenAt[shownId] = now;
                label.Clear();
                shownId = "";
            }
            bestPriority = int.MinValue;
            bestId = bestText = null;
        }
    }
}

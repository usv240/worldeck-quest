using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Typing instead of talking: a keyboard you poke with a fingertip appears in front of you, with what you have
    /// typed above it and the four things you most likely mean as buttons on top (<see cref="Suggestions"/>). Touch a
    /// suggestion, or Enter, and it happens exactly as if you had said it. It is Worldeck's own keyboard, not Quest's:
    /// the app keeps your hands and the world keeps running while you type, so the suggestions can always be touched.
    /// It closes on Close, on Enter, or after 30 s untouched.
    /// </summary>
    public sealed class TypeBox : MonoBehaviour
    {
        public Guide Guide;
        public Transform Head;
        public Font Font;
        public Material Panel;
        /// <summary>What was typed (Enter, or a suggestion touched).</summary>
        public event Action<string, string> Submitted;
        public bool Open { get; private set; }
        public string Text { get; private set; } = "";
        public List<string> Shown { get; private set; } = new List<string>();
        public const int MaxLength = 60;
        public const float IdleClose = 30f;

        Transform root;
        Caption typed;
        readonly List<PokeButton> chips = new List<PokeButton>();
        float lastTouch;
        string note = "";

        static readonly string[] Rows = { "qwertyuiop", "asdfghjkl'", "zxcvbnm,.?" };

        public void Show()
        {
            if (Open) Hide();
            Open = true;
            Text = "";
            note = "";
            lastTouch = Time.unscaledTime;
            Build();
            Refresh();
            Guide?.SetMood(Guide.Mood.Listening);
            Trace.Info("Type", "opened", "keyboard", root != null ? "worldeck" : "none");
        }

        public void Hide()
        {
            if (!Open) return;
            Open = false;
            if (root != null) Util.Kill(root.gameObject);
            root = null; typed = null; chips.Clear();
            Guide?.SetMood(Guide.Mood.Idle);
            Trace.Info("Type", "closed");
        }

        /// <summary>For tests and the editor: as if these letters had been typed so far.</summary>
        public void TypeForTests(string text) { Text = text ?? ""; Refresh(); }

        /// <summary>One key (a letter, " ", or "\b" for delete).</summary>
        public void Key(string k)
        {
            if (!Open) return;
            lastTouch = Time.unscaledTime;
            note = "";
            if (k == "\b") { if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1); }
            else if (k == " ") { if (Text.Length > 0 && !Text.EndsWith(" ", StringComparison.Ordinal) && Text.Length < MaxLength) Text += " "; }
            else if (Text.Length < MaxLength) Text += k;
            Sfx.Instance?.Ui("ui_select", 0.35f);
            Refresh();
        }

        /// <summary>A suggestion was touched, or a phrase is submitted.</summary>
        public void Pick(string phrase, string how)
        {
            if (!Open || string.IsNullOrWhiteSpace(phrase)) return;
            Hide();
            Trace.Info("Type", "submitted", "how", how, "words", phrase.Split(' ').Length);
            Submitted?.Invoke(phrase.Trim(), how);
        }

        /// <summary>
        /// Enter: understood offline (a place, a change, a thing), it happens. A part-word ("volc") that a suggestion
        /// completes: the keyboard stays, its suggestions say what you might mean. Anything else goes on as typed (the
        /// relay may know it).
        /// </summary>
        public void Done(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { Hide(); return; }
            if (Understood(text)) { Pick(text, "typed"); return; }
            var s = Suggestions.For(text, 4);
            if (s.Count > 0 && s[0] != Words.Normalise(text) && note.Length == 0)
            {
                Text = text;
                note = "Did you mean…";
                Shown = s;
                Refresh();
                Trace.Info("Type", "offered after enter", "count", s.Count);
                return;
            }
            Pick(text, "typed");
        }

        static bool Understood(string text)
        {
            var h = Words.Parse(text, null, 1);
            return (h.Act != Act.Unclear && h.Act != Act.None && !h.AskAi) || (Wishes.Read(text)?.Kind ?? "").Length > 0;
        }

        void Update()
        {
            if (!Open) return;
            if (Time.unscaledTime - lastTouch > IdleClose) { Trace.Info("Type", "closed: untouched"); Hide(); }
        }

        // ---------- the keyboard ----------

        const float KeyW = 0.033f, KeyH = 0.034f, Gap = 0.006f;

        void Build()
        {
            if (Font == null || Panel == null || Head == null) return;   // tests: no keyboard, just the logic
            root = new GameObject("Keyboard").transform;
            root.SetParent(transform, false);
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            root.position = Head.position + f * 0.4f + Vector3.down * 0.2f;
            // Tilted to face the eyes, like a laptop keyboard leaning back.
            root.rotation = Quaternion.LookRotation(root.position - Head.position, Vector3.up);

            typed = Caption.Make("Typed", root, Font, Panel, Head, 13f, 30f);
            typed.Fixed = true;
            typed.transform.localPosition = new Vector3(0f, 0.15f, 0f);

            float pitch = KeyW + Gap;
            for (int r = 0; r < Rows.Length; r++)
            {
                string row = Rows[r];
                float x0 = -(row.Length - 1) * pitch * 0.5f + r * 0.008f;
                for (int i = 0; i < row.Length; i++)
                {
                    string k = row[i].ToString();
                    var b = PokeButton.Make("type key " + k, root, Panel, Font, k, new Vector2(KeyW, KeyH), 14f);
                    b.transform.localPosition = new Vector3(x0 + i * pitch, 0.02f - r * (KeyH + Gap), 0f);
                    b.Pressed += () => Key(k);
                }
            }
            float y = 0.02f - 3 * (KeyH + Gap);
            Add("type close", "Close", new Vector2(0.06f, KeyH), new Vector3(-0.155f, y, 0f), () => Hide());
            Add("type space", "space", new Vector2(0.14f, KeyH), new Vector3(-0.04f, y, 0f), () => Key(" "));
            Add("type delete", "Delete", new Vector2(0.065f, KeyH), new Vector3(0.075f, y, 0f), () => Key("\b"));
            var enter = Add("type enter", "Enter", new Vector2(0.065f, KeyH), new Vector3(0.155f, y, 0f), () => Done(Text));
            enter.Highlight = true;
        }

        PokeButton Add(string id, string label, Vector2 size, Vector3 at, Action onPress)
        {
            var b = PokeButton.Make(id, root, Panel, Font, label, size, 13f);
            b.transform.localPosition = at;
            b.Pressed += onPress;
            return b;
        }

        void Refresh()
        {
            if (note.Length == 0) Shown = Suggestions.For(Text, 4);
            if (typed != null) typed.Say((note.Length > 0 ? note + "\n" : "") + (Text.Length == 0 ? "Type a world, or anything" : Text + "_"), 0f);
            if (root == null) return;
            foreach (var c in chips) if (c != null) Util.Kill(c.gameObject);
            chips.Clear();
            // Suggestions, two by two above the keys.
            for (int i = 0; i < Shown.Count && i < 4; i++)
            {
                string phrase = Shown[i];
                int col = i % 2, row = i / 2;
                var b = PokeButton.Make("type: " + phrase, root, Panel, Font, Clip(phrase, 26), new Vector2(0.185f, 0.032f), 12f);
                b.transform.localPosition = new Vector3(col == 0 ? -0.096f : 0.096f, 0.104f - row * 0.038f, 0f);
                b.Highlight = i == 0 && Text.Length > 0;
                b.Pressed += () => Pick(phrase, "typed-suggestion");
                chips.Add(b);
            }
        }

        static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}

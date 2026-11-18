using System.Text;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Words in the world: a TextMesh that stays legible at any distance (LegibleText), on a soft panel
    /// (TextPanel), wrapped to a line length, turned towards the eyes. Lines show for a while and fade.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class Caption : MonoBehaviour
    {
        /// <summary>Every caption alive (CaptionLayout keeps them from overlapping).</summary>
        public static readonly System.Collections.Generic.List<Caption> All = new System.Collections.Generic.List<Caption>();
        /// <summary>Who keeps their place when two would overlap: the higher number. Mara 9, Juniper 7, a local 5.</summary>
        public int Priority = 1;
        /// <summary>Never moved (part of a menu, a card with its buttons, the dock): the others keep clear of it.</summary>
        public bool Fixed;
        /// <summary>How far the layout has moved it, in view heights (eased).</summary>
        public float LayoutShift;
        Vector3 layoutApplied;
        TextPanel textPanel;
        public TextMesh Text;
        public Transform Head;
        public int LineChars = 34;
        public bool FaceHead = true;
        /// <summary>While true, only captions marked <see cref="Loud"/> show (the card of a thing you are looking at).</summary>
        public static bool Hush;
        /// <summary>This caption waits, hidden (its words kept), while something it would cover is up.</summary>
        public bool Muted;
        public bool Loud;
        float hideAt = -1f;
        MeshRenderer textRenderer;

        // Words are never hidden behind the world (a bench, a tree, Mara's table): panel and text draw on top.
        static Material onTopText, onTopPanel;
        static Font onTopFont;
        static Material basePanel;

        public static Material OnTopText(Font font)
        {
            if (onTopText != null && onTopFont == font) return onTopText;
            onTopFont = font;
            onTopText = new Material(font.material) { name = "Caption text (on top)", renderQueue = 4001 };
            onTopText.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            return onTopText;
        }

        public static Material OnTopPanel(Material panel)
        {
            if (panel == null) return null;
            if (onTopPanel != null && basePanel == panel) return onTopPanel;
            basePanel = panel;
            onTopPanel = new Material(panel) { name = "Caption panel (on top)", renderQueue = 4000 };
            onTopPanel.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            return onTopPanel;
        }

        public static Caption Make(string name, Transform parent, Font font, Material panel, Transform head, float dmm = 18f, float maxWidthDeg = 26f)
        {
            dmm = Mathf.Max(dmm * 2f, 30f);
            if (maxWidthDeg > 0f) maxWidthDeg *= 1.7f;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 84;
            tm.characterSize = 0.001f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.97f, 0.92f, 0.97f);
            go.GetComponent<MeshRenderer>().sharedMaterial = OnTopText(font);
            var legible = go.AddComponent<LegibleText>();
            legible.Head = head; legible.TargetDmm = dmm; legible.MinDmm = Mathf.Min(dmm, 24f); legible.MaxWidthDeg = maxWidthDeg;
            go.AddComponent<TextPanel>().Material = OnTopPanel(panel);
            var c = go.AddComponent<Caption>();
            c.Text = tm;
            c.Head = head;
            return c;
        }

        /// <summary>
        /// How long someone new needs to find a line and read it: 1.5 s, and 0.32 s a word (about 190 words a
        /// minute, slower than on a page: text in a headset is read with the head as well as the eyes).
        /// </summary>
        public static float ReadSeconds(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            int words = 0;
            bool inWord = false;
            foreach (char ch in text)
            {
                bool w = !char.IsWhiteSpace(ch);
                if (w && !inWord) words++;
                inWord = w;
            }
            return 1.5f + 0.32f * words;
        }

        /// <summary>When the line on show appeared.</summary>
        public float ShownAt { get; private set; } = -10f;
        /// <summary>When the line on show goes (≤ 0: it stays).</summary>
        public float HideAt => hideAt;
        /// <summary>A line is on show that has not been up long enough to read yet.</summary>
        public bool Fresh => Showing && Time.unscaledTime - ShownAt < ReadSeconds(Text.text);

        /// <summary>Shows <paramref name="text"/>, wrapped; hides after <paramref name="seconds"/> (≤ 0: stays), and
        /// never before it can be read (<see cref="ReadSeconds"/>).</summary>
        public void Say(string text, float seconds = 0f, Color? color = null)
        {
            if (Text == null) return;
            Text.text = Wrap(text ?? "", LineChars);
            Text.color = color ?? new Color(1f, 0.97f, 0.92f, 0.97f);
            if (seconds > 0f && Text.text.Length > 0) seconds = Mathf.Max(seconds, ReadSeconds(Text.text));
            hideAt = seconds > 0f ? Time.unscaledTime + seconds : -1f;
            ShownAt = Time.unscaledTime;
            gameObject.SetActive(Text.text.Length > 0);
        }

        public void Clear() { if (Text != null) Text.text = ""; hideAt = -1f; }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); CaptionLayout.Ensure(); }
        void OnDisable()
        {
            All.Remove(this);
            if (layoutApplied != Vector3.zero) { transform.position -= layoutApplied; layoutApplied = Vector3.zero; }
            LayoutShift = 0f;
        }

        // Before anything places it this frame: back where its owner put it (the layout adds its move again later).
        void Update()
        {
            if (layoutApplied != Vector3.zero) { transform.position -= layoutApplied; layoutApplied = Vector3.zero; }
        }

        /// <summary>Called by CaptionLayout, last in the frame.</summary>
        public void ApplyLayout(Vector3 offset)
        {
            if (layoutApplied != Vector3.zero) transform.position -= layoutApplied;
            transform.position += offset;
            layoutApplied = offset;
        }

        /// <summary>On show to the eye: words, not hushed, its object active.</summary>
        public bool OnShow => Showing && gameObject.activeInHierarchy && (textRenderer == null || textRenderer.enabled);

        /// <summary>Where its panel (or, without one, its words) falls on the view.</summary>
        public bool MeasurePanel(Camera cam, out Rect rect, out float depth)
        {
            if (textPanel == null) textPanel = GetComponent<TextPanel>();
            var p = textPanel != null ? textPanel.Panel : null;
            if (p != null && p.enabled) return CaptionLayout.ViewRect(cam, p.transform, 1f, 1f, out rect, out depth);
            rect = default; depth = 0f;
            if (textRenderer == null) textRenderer = Text != null ? Text.GetComponent<MeshRenderer>() : null;
            if (textRenderer == null) return false;
            var b = textRenderer.localBounds;
            return CaptionLayout.ViewRect(cam, transform, b.size.x, b.size.y, out rect, out depth, b.center);
        }
        public string Current => Text != null ? Text.text.Replace('\n', ' ') : "";
        public bool Showing => Text != null && Text.text.Length > 0;

        void LateUpdate()
        {
            if (hideAt > 0f && Time.unscaledTime >= hideAt) { Clear(); hideAt = -1f; }
            if (Text != null)
            {
                if (textRenderer == null) textRenderer = Text.GetComponent<MeshRenderer>();
                bool show = (!Hush || Loud) && !Muted;
                if (textRenderer != null && textRenderer.enabled != show) textRenderer.enabled = show;
            }
            if (FaceHead && Head != null && Text != null && Text.text.Length > 0)
            {
                Vector3 to = transform.position - Head.position;
                to.y *= 0.5f;
                if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to, Vector3.up);
            }
        }

        public static string Wrap(string s, int width)
        {
            if (s.Length <= width) return s;
            var sb = new StringBuilder();
            foreach (var para in s.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                int line = 0;
                foreach (var w in para.Split(' '))
                {
                    if (w.Length == 0) continue;
                    if (line > 0 && line + 1 + w.Length > width) { sb.Append('\n'); line = 0; }
                    else if (line > 0) { sb.Append(' '); line++; }
                    sb.Append(w);
                    line += w.Length;
                }
            }
            return sb.ToString();
        }
    }
}

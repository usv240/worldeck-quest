using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A button you press with a fingertip (the other hand's index, or a controller's tip), or by looking
    /// at it and pinching. Sizes follow Meta's hand UI guidance: ≥ 40 mm, ≥ 12 mm apart. Press on entering the
    /// front face moving in; one press per entry; hover tick when a fingertip is close.
    /// </summary>
    public sealed class PokeButton : MonoBehaviour
    {
        public Vector2 Size = new Vector2(0.056f, 0.04f);
        public Color Idle = new Color(0.07f, 0.08f, 0.14f, 0.95f);
        public Color HoverColor = new Color(0.16f, 0.2f, 0.34f, 0.92f);
        public Color Accent = new Color(1f, 0.62f, 0.25f, 0.95f);
        public bool Highlight;
        public event Action Pressed;
        public string Id = "";
        [Tooltip("What pressing it does, shown next to it on hover until it has been pressed a few times (Hints).")]
        public string Hint = "";
        [Tooltip("Look-to-press takes this many times the usual dwell (a wrist button you glance at should not open at once).")]
        public float DwellScale = 1f;

        public TextMesh Label { get; private set; }
        public Renderer Back { get; private set; }
        public float LastPress { get; private set; } = -10f;
        public bool Hovered { get; private set; }
        /// <summary>A fingertip or a controller ray is on it (not only the eyes).</summary>
        public bool FingerHover;
        /// <summary>A fingertip does not press it before this time (it has only just appeared).</summary>
        public float ArmedAt;
        /// <summary>True while a fingertip must not press it (a hand is busy carrying something past it).</summary>
        public System.Func<bool> Blocked;
        public bool Armed => Time.unscaledTime >= ArmedAt && (Blocked == null || !Blocked());
        /// <summary>A fingertip that rests on it presses it, however it got there (the guide's choices: a finger that came up from the lap slid on from below and was "not a press").</summary>
        public bool HoldPress;

        MaterialPropertyBlock block;
        float press;   // 0..1 visual depress
        static readonly int ColorId = Shader.PropertyToID("_Color"), SizeId = Shader.PropertyToID("_Size"), RadiusId = Shader.PropertyToID("_Radius");

        public static readonly List<PokeButton> Live = new List<PokeButton>();

        public static PokeButton Make(string name, Transform parent, Material panel, Font font, string text, Vector2 size, float textDmm = 16f)
        {
            var go = new GameObject("Button " + name);
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<PokeButton>();
            b.Id = name;
            b.Size = size;
            var back = Util.MeshObject("Back", go.transform, Util.Quad(Color.white), panel);
            back.transform.localScale = new Vector3(size.x, size.y, 1f);
            b.Back = back.GetComponent<Renderer>();
            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = new Vector3(0, 0, -0.002f);
            var tm = tgo.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 72;
            tm.characterSize = 0.0011f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.97f, 0.92f, 0.98f);
            tm.text = text;
            tgo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            b.Label = tm;
            b.FitText(textDmm);
            return b;
        }

        /// <summary>
        /// Sizes the label for <paramref name="dmm"/> at 45 cm and keeps it inside the button. Measured in the button's
        /// own space, so it is the same whichever way the panel faces (the world-space box shrank when the panel was
        /// seen at an angle, and long labels spilled out). A label too long to read on one line goes on two.
        /// </summary>
        public void FitText(float dmm = 16f)
        {
            if (Label == null) return;
            // TextMesh type height ≈ 0.1 × fontSize × characterSize; dmm = mm of type per metre of distance.
            float size = Size.y * 0.42f / (0.1f * Label.fontSize);
            Label.characterSize = size;
            float k = Shrink();
            if (k < 0.75f && Label.text.IndexOf('\n') < 0)
            {
                string one = Label.text, two = TwoLines(one);
                if (two != one)
                {
                    Label.text = two;
                    float k2 = Shrink();
                    if (k2 > k) k = k2; else Label.text = one;
                }
            }
            Label.characterSize = size * k;
        }

        /// <summary>How much the label must shrink to sit inside the button: 1 when it already does.</summary>
        float Shrink()
        {
            var r = Label.GetComponent<MeshRenderer>();
            if (r == null) return 1f;
            var b = r.localBounds;
            Vector3 s = Label.transform.localScale;
            float w = b.size.x * Mathf.Abs(s.x), h = b.size.y * Mathf.Abs(s.y);
            float k = 1f;
            if (w > Size.x * 0.9f) k = Mathf.Min(k, Size.x * 0.9f / w);
            if (h > Size.y * 0.86f) k = Mathf.Min(k, Size.y * 0.86f / h);
            return Mathf.Clamp(k, 0.05f, 1f);
        }

        /// <summary>The words on two lines, broken at the space nearest the middle (unchanged when there is none).</summary>
        public static string TwoLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int mid = s.Length / 2, best = -1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ' ' && (best < 0 || Mathf.Abs(i - mid) < Mathf.Abs(best - mid))) best = i;
            return best <= 0 ? s : s.Substring(0, best) + "\n" + s.Substring(best + 1);
        }

        public void SetText(string text, float dmm = 16f)
        {
            if (Label == null) return;
            Label.text = text;
            FitText(dmm);
        }

        /// <summary>When it appeared (looking at it does not press it for a moment after).</summary>
        public float BornAt { get; private set; }
        void OnEnable() { BornAt = Time.unscaledTime; if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); Hovered = false; }

        /// <summary>Fingertip in this button's space: x right, y up, z out of the face (+ towards the user).</summary>
        public Vector3 Local(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            Vector3 s = transform.lossyScale;
            // Buttons face the user along -Z of their transform (quad front), so "in front" is negative z.
            return new Vector3(l.x * s.x, l.y * s.y, -l.z * s.z);
        }

        public bool Over(Vector3 world, float margin = 0.006f)
        {
            var l = Local(world);
            return Mathf.Abs(l.x) <= Size.x * 0.5f + margin && Mathf.Abs(l.y) <= Size.y * 0.5f + margin;
        }

        public void Press()
        {
            if (Time.unscaledTime - LastPress < 0.35f) return;
            LastPress = Time.unscaledTime;
            press = 1f;
            Sfx.Instance?.Ui("ui_select", 0.8f);
            if (!string.IsNullOrEmpty(Hint)) Hints.Instance?.Learned("button:" + Hint);
            Pressed?.Invoke();
        }

        public void SetHover(bool on)
        {
            if (on && !Hovered) Sfx.Instance?.Ui("ui_hover", 0.35f);
            Hovered = on;
        }

        void LateUpdate()
        {
            press = Mathf.MoveTowards(press, 0f, Time.unscaledDeltaTime * 5f);
            if (Back == null) return;
            block ??= new MaterialPropertyBlock();
            Color c = Highlight ? Color.Lerp(Idle, Accent, 0.45f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f)) : Idle;
            if (Hovered) c = Color.Lerp(c, HoverColor, 0.7f);
            if (press > 0f) c = Color.Lerp(c, Accent, press);
            Back.GetPropertyBlock(block);
            block.SetColor(ColorId, c);
            Vector3 s = transform.lossyScale;
            block.SetVector(SizeId, new Vector4(Size.x * s.x, Size.y * s.y, 0, 0));
            block.SetFloat(RadiusId, Mathf.Min(Size.x, Size.y) * 0.28f * s.y);
            Back.SetPropertyBlock(block);
            Back.transform.localPosition = new Vector3(0, 0, press * 0.004f);
        }
    }

}

using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A soft rounded panel behind a TextMesh, fitted to the text every frame (after LegibleText resizes
    /// it), so words read cleanly over any real room. Hidden while the text is empty; fades in when the
    /// text appears.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    [DefaultExecutionOrder(100)]
    public sealed class TextPanel : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Padding around the text, in letter heights.")]
        public float Padding = 0.55f;
        [Tooltip("Corner radius, in letter heights.")]
        public float Radius = 0.6f;
        public float FadeSeconds = 0.18f;

        TextMesh text;
        Renderer textRenderer;
        MeshRenderer panel;
        MaterialPropertyBlock block;
        float shown;
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public Renderer Panel => panel;
        /// <summary>Panel size in metres (world), for tests.</summary>
        public Vector2 WorldSize { get; private set; }

        void Awake()
        {
            text = GetComponent<TextMesh>();
            textRenderer = GetComponent<Renderer>();
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Panel";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            panel = go.GetComponent<MeshRenderer>();
            panel.sharedMaterial = Material;
            panel.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            panel.receiveShadows = false;
            panel.enabled = false;
            block = new MaterialPropertyBlock();
        }

        void LateUpdate() => Fit();

        public void Fit()
        {
            if (panel == null || text == null) return;
            if (panel.sharedMaterial != Material) panel.sharedMaterial = Material;   // added at runtime: set after Awake
            bool has = Material != null && !string.IsNullOrEmpty(text.text) && textRenderer.enabled && text.gameObject.activeInHierarchy;
            shown = has ? Mathf.MoveTowards(shown, 1f, FadeSeconds > 0 ? Time.unscaledDeltaTime / FadeSeconds : 1f) : 0f;
            panel.enabled = has;
            if (!has) return;

            var b = textRenderer.localBounds;
            float em = 0.1f * text.fontSize * text.characterSize;
            float pad = Padding * em;
            var size = new Vector2(b.size.x + 2f * pad, b.size.y + 2f * pad);
            var t = panel.transform;
            t.localPosition = new Vector3(b.center.x, b.center.y, 0.002f);   // just behind the letters
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(size.x, size.y, 1f);

            Vector3 s = transform.lossyScale;
            WorldSize = new Vector2(size.x * Mathf.Abs(s.x), size.y * Mathf.Abs(s.y));
            panel.GetPropertyBlock(block);
            block.SetVector(SizeId, new Vector4(WorldSize.x, WorldSize.y, 0, 0));
            block.SetFloat(RadiusId, Radius * em * Mathf.Abs(s.y));
            Color c = Material.GetColor(ColorId);
            c.a *= Mathf.SmoothStep(0f, 1f, shown);
            block.SetColor(ColorId, c);
            panel.SetPropertyBlock(block);
        }
    }
}

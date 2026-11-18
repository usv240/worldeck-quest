using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Keeps a TextMesh readable wherever it ends up. Meta's typography guidance asks for type of at least
    /// 14, and 18 or more for comfortable reading, in units where 1 is 1 mm of type seen from 1 m away
    /// ("dmm"). The first device run (Nov 9) showed fixed world sizes landing at 7–10 dmm on a real desk, so
    /// every frame this sets the character size from the distance to the head: TargetDmm when it fits,
    /// smaller only to stay inside a width (a square, or an angle for long blocks), and never below MinDmm.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public sealed class LegibleText : MonoBehaviour
    {
        public Transform Head;
        [Tooltip("Type size to aim for, in mm per metre of viewing distance.")]
        public float TargetDmm = 18f;
        /// <summary>Comfort & access: every caption and label larger (1 normal).</summary>
        public static float Scale = 1f;
        [Tooltip("Never smaller than this (Meta's minimum for legibility).")]
        public float MinDmm = 14f;
        [Tooltip("If set, the text stays this fraction of the target's width inside it (a menu label on its square).")]
        public Transform FitInside;
        public float FitFraction = 0.9f;
        [Tooltip("If above zero, a long block is narrowed to this many degrees wide (still never below MinDmm).")]
        public float MaxWidthDeg;

        /// <summary>The type size as seen from the head right now, in dmm.</summary>
        public float Dmm { get; private set; }

        TextMesh tm;
        Renderer rend;

        void Awake()
        {
            tm = GetComponent<TextMesh>();
            rend = GetComponent<Renderer>();
        }

        void LateUpdate() => Apply();

        /// <summary>Sizes the text now (also called by tests and by code that just changed the text).</summary>
        public void Apply()
        {
            if (tm == null) Awake();
            if (Head == null || tm == null || tm.fontSize <= 0) return;
            float d = Vector3.Distance(Head.position, transform.position);
            if (d < 0.05f) return;
            float scale = Mathf.Max(1e-6f, transform.lossyScale.y);
            // TextMesh type height is about 0.1 × fontSize × characterSize in local units.
            float perDmm = d * 0.001f / (0.1f * tm.fontSize * scale);
            float size = TargetDmm * Scale * perDmm;

            float widthPerSize = WidthPerCharacterSize();
            if (widthPerSize > 0f)
            {
                float maxWidth = float.MaxValue;
                if (FitInside != null) maxWidth = Mathf.Min(maxWidth, FitInside.lossyScale.x * FitFraction);
                if (MaxWidthDeg > 0f) maxWidth = Mathf.Min(maxWidth, 2f * d * Mathf.Tan(0.5f * MaxWidthDeg * Mathf.Deg2Rad));
                if (maxWidth < float.MaxValue) size = Mathf.Min(size, maxWidth / widthPerSize);
            }
            size = Mathf.Max(size, MinDmm * Scale * perDmm);
            if (!Mathf.Approximately(tm.characterSize, size)) tm.characterSize = size;
            Dmm = size / perDmm;
        }

        /// <summary>World width of the text per unit of characterSize (text width scales linearly with it).</summary>
        float WidthPerCharacterSize()
        {
            if (rend == null || tm.characterSize <= 0f || string.IsNullOrEmpty(tm.text)) return 0f;
            float w = rend.localBounds.size.x * Mathf.Abs(transform.lossyScale.x);
            return w > 0f ? w / tm.characterSize : 0f;
        }
    }
}

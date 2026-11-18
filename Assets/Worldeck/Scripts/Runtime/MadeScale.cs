using UnityEngine;

namespace Worldeck
{
    /// <summary>Pops a made thing up to its size (and to a new size when it is changed).</summary>
    public sealed class MadeScale : MonoBehaviour
    {
        public float Target = 1f;
        /// <summary>Breathing: how much taller than its size it is just now (Alive sets it; 0.01 = 1%).</summary>
        public float Breath;
        float shown;

        public void Init(float target) { Target = target; shown = 0f; }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, 1f, Time.unscaledDeltaTime / 0.55f);
            float want = Target * Util.Pop(shown);
            transform.localScale = Vector3.Lerp(transform.localScale, new Vector3(want, want * (1f + Breath), want), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
        }
    }
}

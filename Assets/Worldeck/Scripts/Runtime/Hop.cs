using UnityEngine;

namespace Worldeck
{
    /// <summary>A made thing touched with a fingertip: a little hop and a squash as it lands, then it is still again.</summary>
    public sealed class Hop : MonoBehaviour
    {
        public const float Seconds = 0.42f;
        float t, height, baseY;

        public static void On(Transform target, float size)
        {
            if (target == null) return;
            var h = target.GetComponent<Hop>();
            if (h != null) return;   // already hopping
            h = target.gameObject.AddComponent<Hop>();
            h.height = Mathf.Clamp(size * 0.18f, 0.015f, 0.12f);
            h.baseY = target.position.y;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Seconds);
            float up = Mathf.Sin(k * Mathf.PI) * height;
            var p = transform.position;
            transform.position = new Vector3(p.x, baseY + up, p.z);
            if (k >= 1f) { transform.position = new Vector3(transform.position.x, baseY, transform.position.z); Destroy(this); }
        }
    }
}

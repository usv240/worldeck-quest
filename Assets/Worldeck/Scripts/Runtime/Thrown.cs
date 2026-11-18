using UnityEngine;

namespace Worldeck
{
    /// <summary>A made thing flicked away: it flies on the way it was thrown, tumbles, shrinks to nothing and is gone.</summary>
    public sealed class Thrown : MonoBehaviour
    {
        public const float Seconds = 0.55f;
        Vector3 v, scale0;
        float t;

        public void Go(Vector3 velocity)
        {
            v = Vector3.ClampMagnitude(velocity * 1.3f, 7f);
            scale0 = transform.localScale;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            transform.position += v * dt;
            v *= Mathf.Exp(-dt * 1.5f);
            transform.Rotate(Vector3.up, 420f * dt, Space.World);
            float k = Mathf.Clamp01(1f - t / Seconds);
            transform.localScale = scale0 * (k * k);
            if (t >= Seconds) Destroy(gameObject);
        }
    }
}

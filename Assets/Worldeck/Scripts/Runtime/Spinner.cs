using UnityEngine;

namespace Worldeck
{
    /// <summary>A thing turning on its own: flicked as it was let go (slowing down), or asked to ("spin it": steady).</summary>
    public sealed class Spinner : MonoBehaviour
    {
        public float Speed, Drag;

        public static void On(Transform t, float speed, float drag)
        {
            var s = t.GetComponent<Spinner>();
            if (s == null) s = t.gameObject.AddComponent<Spinner>();
            s.Speed = speed; s.Drag = drag;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            transform.Rotate(Vector3.up, Speed * dt, Space.World);
            if (Drag > 0f)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, Mathf.Abs(Speed) * Drag * dt + 20f * dt);
                if (Mathf.Abs(Speed) < 2f) Destroy(this);
            }
        }
    }
}

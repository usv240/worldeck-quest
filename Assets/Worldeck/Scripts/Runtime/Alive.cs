using System;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A made animal or person that seems alive: it breathes, and when you look at it, it slowly turns to face you
    /// (and now and then glances away). Never while it is held, spinning or thrown, and not at life size beyond a
    /// metre and a half, where turning would swing it through the room.
    /// </summary>
    public sealed class Alive : MonoBehaviour
    {
        public Transform Head;
        /// <summary>True while something else moves it (held in a hand).</summary>
        public Func<bool> Busy;
        /// <summary>Its size now (metres, longest side).</summary>
        public Func<float> Size;
        [Tooltip("Turning speed, degrees a second.")]
        public float TurnSpeed = 45f;
        /// <summary>Whether a step to this point keeps it on the same ground (the same table top, not off the edge).</summary>
        public Func<Vector3, Vector3, bool> CanStepTo;
        /// <summary>True while it is walking somewhere (for tests).</summary>
        public bool Walking { get; private set; }
        public int Walks { get; private set; }
        /// <summary>Where an open hand is held out to it, on its own ground (null: nobody is calling it).</summary>
        public Func<Vector3?> Called;
        /// <summary>True while it is on its way to a hand.</summary>
        public bool Coming { get; private set; }
        /// <summary>How many times it has walked over to a hand.</summary>
        public int Arrivals { get; private set; }
        /// <summary>It has just reached the hand it was called by.</summary>
        public event Action Arrived;
        bool greeted, stepped;

        MadeScale scale;
        Gait gait;
        float phase, glanceAt, glance, nextWalk;
        Vector3 walkTo;
        public float Yaw => transform.eulerAngles.y;

        void Start()
        {
            scale = GetComponent<MadeScale>();
            gait = GetComponent<Gait>();
            phase = UnityEngine.Random.value * 10f;
            glanceAt = Time.unscaledTime + UnityEngine.Random.Range(5f, 9f);
            nextWalk = Time.unscaledTime + UnityEngine.Random.Range(3f, 6f);
        }

        /// <summary>While you look away, a small animal on a table or the floor takes a few steps; it stops when you look back.</summary>
        bool Potter(float t, bool watched)
        {
            float size = Size != null ? Size() : 0.3f;
            if (CanStepTo == null || size > 0.7f) { Walking = false; return false; }
            if (watched) { Walking = false; nextWalk = Mathf.Max(nextWalk, t + 2f); return false; }
            if (!Walking && t > nextWalk)
            {
                nextWalk = t + UnityEngine.Random.Range(4f, 8f);
                float a0 = UnityEngine.Random.Range(0f, Mathf.PI * 2f), far = UnityEngine.Random.Range(0.06f, 0.16f);
                for (int k = 0; k < 8 && !Walking; k++)
                {
                    float a = a0 + k * Mathf.PI * 0.25f;
                    var target = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (k < 4 ? far : 0.06f);
                    if (CanStepTo(transform.position, target)) { walkTo = target; Walking = true; Walks++; }
                }
            }
            if (!Walking) return false;
            Vector3 d = walkTo - transform.position; d.y = 0f;
            if (d.magnitude < 0.005f) { Walking = false; return true; }
            var face = Quaternion.LookRotation(d.normalized, Vector3.up);
            var flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            var turned = Quaternion.RotateTowards(flat, face, 160f * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Euler(transform.eulerAngles.x, turned.eulerAngles.y, transform.eulerAngles.z);
            if (Quaternion.Angle(turned, face) < 20f)
            {
                Vector3 step = d.normalized * Mathf.Min(d.magnitude, (0.05f + size * 0.12f) * Time.unscaledDeltaTime);
                if (CanStepTo(transform.position, transform.position + step)) { transform.position += step; stepped = true; }
                else Walking = false;
            }
            return true;
        }

        /// <summary>An open hand held out on its ground: it turns, trots over, and greets the hand when it gets there.</summary>
        bool Come()
        {
            float size = Size != null ? Size() : 0.3f;
            Vector3? hand = Called != null && CanStepTo != null && size <= 0.7f ? Called() : null;
            if (hand == null) { Coming = false; greeted = false; return false; }
            Walking = false;
            Vector3 d = hand.Value - transform.position; d.y = 0f;
            float stop = size * 0.5f + 0.05f;
            if (d.magnitude > 0.01f)
            {
                var face = Quaternion.LookRotation(d.normalized, Vector3.up);
                var flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                var turned = Quaternion.RotateTowards(flat, face, 200f * Time.unscaledDeltaTime);
                transform.rotation = Quaternion.Euler(transform.eulerAngles.x, turned.eulerAngles.y, transform.eulerAngles.z);
                // Once beside the hand it stays put until the hand has moved half a centimetre on.
                if (d.magnitude > stop + (Coming ? 0f : 0.005f) && Quaternion.Angle(turned, face) < 25f)
                {
                    Vector3 step = d.normalized * Mathf.Min(d.magnitude - stop, (0.12f + size * 0.3f) * Time.unscaledDeltaTime);
                    // Only over its own ground: at the table's edge it stops and waits, looking at the hand.
                    if (CanStepTo(transform.position, transform.position + step)) { transform.position += step; Coming = true; stepped = true; d -= step; }
                }
            }
            if (d.magnitude <= stop + 0.005f)
            {
                // A greeting only when it walked there: a hand that was already beside it is just looked at.
                if (Coming && !greeted) { greeted = true; Arrivals++; Arrived?.Invoke(); }
                Coming = false;
            }
            return true;
        }

        void Update()
        {
            float t = Time.unscaledTime;
            if (scale != null) scale.Breath = 0.012f * Mathf.Sin((t + phase) * Mathf.PI * 2f * 0.28f) + (stepped ? 0.03f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f * 3f)) : 0f);
            // Its legs step while it walks (Gait); a small bob goes with them.
            if (gait != null) gait.Walk = stepped;
            stepped = false;
            if (Head == null || (Busy != null && Busy()) || GetComponent<Spinner>() != null || GetComponent<Thrown>() != null) return;
            if (Size != null && Size() > 1.5f) return;
            if (Come()) return;
            Vector3 to = Head.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.05f || dist > 3f) return;
            // Only when you look its way: it notices you (and stops pottering about to look back).
            Vector3 seen = transform.position - Head.position;
            bool watched = Vector3.Angle(Head.forward, seen) <= 45f;
            if (Potter(t, watched) || !watched) return;
            if (t > glanceAt) { glance = UnityEngine.Random.Range(-35f, 35f); glanceAt = t + UnityEngine.Random.Range(6f, 11f); }
            if (t > glanceAt - 4f) glance = Mathf.MoveTowards(glance, 0f, Time.unscaledDeltaTime * 20f);
            var want = Quaternion.AngleAxis(glance, Vector3.up) * Quaternion.LookRotation(to / dist, Vector3.up);
            var flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            var next = Quaternion.RotateTowards(flat, want, TurnSpeed * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Euler(transform.eulerAngles.x, next.eulerAngles.y, transform.eulerAngles.z);
        }
    }
}

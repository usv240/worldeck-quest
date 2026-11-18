using System;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// An animal living out in the world, at its real size: it breathes, stands and grazes, ambles a few steps and
    /// back (never far from where it was born), and if you watch it for a moment it turns to look at you. It keeps
    /// to the ground, and stops with the world when time is frozen.
    /// </summary>
    public sealed class Wild : MonoBehaviour
    {
        public Transform Head;
        /// <summary>Ground height at (x, z).</summary>
        public Func<float, float, float> Ground;
        /// <summary>Its size (metres, longest side): bigger animals walk with longer steps.</summary>
        public float Size = 1f;
        public string Kind = "";

        Vector3 home, scale;
        Gait gait;
        float heading, want, nextTurn, walkUntil, watched, phase, grazeUntil, pitch;
        /// <summary>True while it has its head down, grazing.</summary>
        public bool Grazing => Time.time < grazeUntil && !Watching;
        /// <summary>True while it is turned to look at you.</summary>
        public bool Watching { get; private set; }

        /// <summary>Spoken to: it looks up and turns to you.</summary>
        public void Greet() => watched = Mathf.Max(watched, 4f);

        void Start()
        {
            home = transform.position;
            scale = transform.localScale;
            heading = want = transform.eulerAngles.y;
            gait = GetComponent<Gait>();
            phase = UnityEngine.Random.value * 10f;
            nextTurn = Time.time + UnityEngine.Random.Range(2f, 6f);
        }

        void Update()
        {
            float t = Time.time, dt = Time.deltaTime;
            if (dt <= 0f) return;   // time frozen: so is it
            transform.localScale = new Vector3(scale.x, scale.y * (1f + 0.01f * Mathf.Sin((t + phase) * 1.6f)), scale.z);
            // Watched for a moment (in the middle of your view, within 40 m): it looks back.
            if (Head != null)
            {
                Vector3 seen = transform.position + Vector3.up * Size * 0.3f - Head.position;
                bool inView = seen.magnitude < 40f && Vector3.Angle(Head.forward, seen) < 14f;
                watched = inView ? watched + dt : Mathf.Max(0f, watched - dt * 0.5f);
            }
            bool was = Watching;
            Watching = watched > 1.2f;
            if (Watching)
            {
                Vector3 back = Head.position - transform.position; back.y = 0f;
                if (back.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(back).eulerAngles.y;
                walkUntil = 0f;
            }
            else if (was || t > nextTurn)
            {
                nextTurn = t + UnityEngine.Random.Range(4f, 9f);
                Vector3 toHome = home - transform.position; toHome.y = 0f;
                want = toHome.magnitude > 1.5f + Size ? Quaternion.LookRotation(toHome).eulerAngles.y : heading + UnityEngine.Random.Range(-70f, 70f);
                // Big animals mostly stand and look about; small ones wander more. Standing, they often graze.
                walkUntil = UnityEngine.Random.value < (Size > 3f ? 0.25f : 0.5f) ? t + UnityEngine.Random.Range(1.5f, 3.5f) : 0f;
                grazeUntil = walkUntil == 0f && UnityEngine.Random.value < 0.6f ? t + UnityEngine.Random.Range(2.5f, 5.5f) : 0f;
            }
            heading = Mathf.MoveTowardsAngle(heading, want, 32f * dt);
            if (t < walkUntil && Mathf.Abs(Mathf.DeltaAngle(heading, want)) < 25f)
            {
                Vector3 p = transform.position + Quaternion.Euler(0f, heading, 0f) * Vector3.forward * Mathf.Min(0.2f + Size * 0.08f, 1.1f) * dt;
                if (Ground != null) p.y = Ground(p.x, p.z) - 0.02f;
                transform.position = p;
                if (gait != null) gait.Walk = true;   // its legs step
            }
            pitch = Mathf.MoveTowards(pitch, Grazing ? 7f : 0f, 9f * dt);
            transform.rotation = Quaternion.Euler(pitch, heading, 0f);
        }
    }
}

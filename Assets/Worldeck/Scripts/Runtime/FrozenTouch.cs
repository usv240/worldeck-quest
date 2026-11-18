using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// "Freeze": time stops and the world holds still mid-motion: raindrops and snowflakes hang in the
    /// air, the flames stand still, embers float. While frozen, your fingertips can touch them: each one you touch
    /// pops with a tiny chime and a glint. (Particles run on scaled time, so Time.timeScale = 0 holds them; this
    /// runs on unscaled time.)
    /// </summary>
    public sealed class FrozenTouch : MonoBehaviour
    {
        public HandInput Hands;
        public WorldBuilder Builder;
        public Transform Extra;          // e.g. the build site (its flames and embers)
        public Material Glow;
        public float Reach = 0.03f;
        public bool Active;
        public int Popped { get; private set; }

        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[700];
        float nextScan, lastChime;

        public void Begin()
        {
            Active = true;
            systems.Clear();
            if (Builder != null && Builder.Root != null) systems.AddRange(Builder.Root.GetComponentsInChildren<ParticleSystem>());
            if (Extra != null) systems.AddRange(Extra.GetComponentsInChildren<ParticleSystem>());
        }

        public void End() { Active = false; systems.Clear(); }

        void Update()
        {
            if (!Active || Hands == null) return;
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                if (!s.Tracked) continue;
                Vector3 tip = s.IndexTip;
                foreach (var ps in systems)
                {
                    if (ps == null) continue;
                    int n = ps.particleCount;
                    if (n == 0) continue;
                    if (buffer.Length < n) buffer = new ParticleSystem.Particle[n];
                    n = ps.GetParticles(buffer);
                    bool changed = false;
                    bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = world ? buffer[i].position : ps.transform.TransformPoint(buffer[i].position);
                        float r = Reach + buffer[i].GetCurrentSize(ps) * 0.5f;
                        if ((p - tip).sqrMagnitude > r * r) continue;
                        buffer[i].remainingLifetime = 0f;
                        changed = true;
                        Popped++;
                        Glint(p);
                    }
                    if (changed) ps.SetParticles(buffer, n);
                }
            }
        }

        void Glint(Vector3 at)
        {
            if (Time.unscaledTime - lastChime > 0.06f)
            {
                lastChime = Time.unscaledTime;
                Sfx.Instance?.At("place_" + (1 + Popped % 6), at, 0.35f, 1.5f + (Popped % 5) * 0.1f);
            }
            if (Glow == null) return;
            var g = Effects.Halo("Glint", transform, Glow, 0.035f, new Color(0.9f, 0.95f, 1f));
            g.transform.position = at;
            StartCoroutine(Fade(g));
        }

        static System.Collections.IEnumerator Fade(GameObject g)
        {
            float t = 0f;
            while (t < 0.35f && g != null)
            {
                t += Time.unscaledDeltaTime;
                g.transform.localScale = Vector3.one * 0.035f * (1f + t * 4f) * (1f - t / 0.35f);
                yield return null;
            }
            if (g != null) Destroy(g);
        }
    }
}

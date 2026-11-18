using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The build on the table: grows a step with each piece placed (the fire grows, the castle rises) and
    /// finishes with a flourish. Fire builds also drive the world's fire light and crackle.
    /// </summary>
    public sealed class BuildSite : MonoBehaviour
    {
        public Material World, Glow;
        [Tooltip("The real-looking fire (Fire shader).")]
        public Material Fire;
        RealFire realFire;
        public WorldLook Look;
        public string Build = "";
        public float Progress;      // 0 cold … 1 complete
        float shown;
        GameObject result;
        ParticleSystem flames, embers;
        GameObject glow;
        bool done;

        public void Make(Recipe r, float footprint)
        {
            Clear();
            Build = r.Def.Build;
            var mb = new MeshBuilder();
            Builds.Result(mb, Build, WorldColors.For(r), new Rng(r.Seed * 5 + 1), footprint);
            result = Util.MeshObject("Result " + Build, transform, mb.ToMesh(Build), World);
            result.transform.localScale = Vector3.one * 0.001f;
            Color glowCol = WorldColors.Hex(r.Def.Palette.Emissive, Color.white);
            glow = Effects.Halo("Build glow", transform, Glow, footprint * 1.6f, glowCol * 0.6f);
            glow.transform.localPosition = Vector3.up * footprint * 0.4f;
            glow.SetActive(false);
            if (Build == "fire")
            {
                if (Fire != null)
                {
                    realFire = RealFire.Make(transform, Fire, footprint * 0.2f);
                    realFire.transform.localPosition = Vector3.up * 0.02f;
                }
                else
                {
                    flames = Effects.Flames(transform, Glow, footprint * 0.18f);
                    flames.transform.localPosition = Vector3.up * 0.03f;
                    flames.Play();
                }
                embers = Effects.Embers(transform, Glow, 4f);
                embers.transform.localPosition = Vector3.up * 0.08f;
                embers.Play();
            }
            Progress = 0.15f;
            shown = 0f;
            done = false;
        }

        public void Clear()
        {
            Util.KillChildren(transform);
            result = null; flames = null; embers = null; glow = null; realFire = null;
            if (Look != null) Look.FireStrength = 0f;
            done = false;
        }

        public void Step(float progress) => Progress = Mathf.Clamp01(progress);

        public void Finish()
        {
            Progress = 1f;
            done = true;
            if (glow != null) glow.SetActive(true);
        }

        public bool Done => done;

        /// <summary>Jumps the growth to the current progress (editor captures, tests).</summary>
        public void Snap() { shown = Progress; Update(); }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, Progress, Time.deltaTime * 0.8f);
            if (result != null)
            {
                float s = Build == "fire" ? Mathf.Lerp(0.85f, 1f, shown) : Mathf.Lerp(0.25f, 1f, Util.Pop(shown));
                result.transform.localScale = Vector3.one * Mathf.Max(0.001f, s);
                if (Build == "star_map" || Build == "sun_crystal") result.transform.Rotate(0f, (done ? 18f : 6f) * Time.deltaTime, 0f);
            }
            if (flames != null)
            {
                var m = flames.main;
                m.startSizeMultiplier = Mathf.Lerp(0.35f, 1.25f, shown);
                var e = flames.emission;
                e.rateOverTime = Mathf.Lerp(10f, 40f, shown);
            }
            if (embers != null) { var em = embers.emission; em.rateOverTime = Mathf.Lerp(2f, 18f, shown); }
            if (realFire != null) realFire.Strength = Look != null ? Mathf.Max(Mathf.Lerp(0.25f, 1f, shown), Look.FireStrength) : Mathf.Lerp(0.25f, 1f, shown);
            if (Look != null && Build == "fire")
            {
                Look.FirePosition = transform.position + Vector3.up * 0.15f;
                Look.FireStrength = Mathf.Lerp(0.25f, 1f, shown);
            }
            if (glow != null && done) glow.transform.localScale = Vector3.one * (0.3f + 0.03f * Mathf.Sin(Time.time * 2f));
        }
    }
}

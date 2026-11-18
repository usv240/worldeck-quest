using System.Linq;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Mara, as she comes through: a small pilot in a white flight jacket standing on your table, rising out of
    /// sparks from a beacon, lit at the edges by its warm light (the same as Juniper's, so she belongs to this world).
    /// She turns to you, nods as she talks, reaches out to wave, and breaks up into static as her signal fades (your
    /// furniture is her signal). Her figure is Kenney's Mini Characters (CC0) with its own animations; without it she
    /// is built in code from simple shapes in the see-through rim-lit material.
    /// </summary>
    public sealed class HoloPerson : MonoBehaviour
    {
        public Transform Head;
        /// <summary>0 lost, 1 clear. Drives the flicker, the jitter and how solid she looks.</summary>
        public float Signal = 1f;
        public bool Talking;
        /// <summary>How loud her voice is right now (0..1), from the radio: drives her meter.</summary>
        public float Voice;
        public float Height { get; private set; }
        /// <summary>True when she is the animated figure, not the shapes built in code.</summary>
        public bool IsFigure { get; private set; }
        /// <summary>The animation playing now (for tests and logs).</summary>
        public string Playing => anim == null ? "" : current;

        Transform body, head, armL, armR, foreL, foreR;
        float rimGlitch;
        Renderer[] rends;
        MaterialPropertyBlock block;
        float shown, target = 1f, waveUntil, glitchUntil;
        /// <summary>How bad her signal looks, following <see cref="Signal"/> at 1.5 a second: a hit dims her and
        /// brightens her outline over half a second, never in one frame.</summary>
        float badShown;
        Vector3 basePos;
        static readonly Color Tint = new Color(1f, 0.74f, 0.42f, 1f);
        ParticleSystem sparks;

        public static HoloPerson Make(string name, Transform parent, Material holo, Material glow, float height, Material figure = null, Material glowFlat = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var p = root.AddComponent<HoloPerson>();
            var model = figure != null ? Resources.Load<GameObject>("Mara/mara") : null;
            p.solid = glowFlat != null ? glowFlat : glow;
            if (model != null) p.BuildFigure(model, figure, glow, height);
            else p.Build(holo, glow, height);
            return p;
        }

        // ---------- the figure ----------

        Material solid;   // glow for solid shapes (beacon, meter, face); particles keep the halo glow
        Animation anim;
        string current = "", idleClip, nodClip, waveClip, noClip;
        float nextGesture;
        bool wasTalking, wavePlayed;

        void BuildFigure(GameObject model, Material figure, Material glow, float height)
        {
            Height = height;
            IsFigure = true;
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            var go = Instantiate(model, body, false);
            go.name = "Figure";
            // Scale her to stand <height> tall, feet on the beacon.
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer sm) sm.updateWhenOffscreen = true;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                r.sharedMaterials = Enumerable.Repeat(figure, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            float h = any ? Mathf.Max(0.01f, bounds.size.y) : 1f;
            go.transform.localScale = Vector3.one * (height / h);
            if (any) go.transform.localPosition = new Vector3(0f, -(bounds.min.y - go.transform.position.y) * (height / h), 0f);
            head = Find(go.transform, "head") ?? go.transform;
            anim = go.GetComponent<Animation>();
            if (anim != null)
            {
                foreach (AnimationState st in anim)
                {
                    string n = st.name.Contains("|") ? st.name.Substring(st.name.LastIndexOf('|') + 1) : st.name;
                    if (n == "idle") { idleClip = st.name; st.wrapMode = WrapMode.Loop; }
                    else if (n == "emote-yes") nodClip = st.name;
                    else if (n == "emote-no") noClip = st.name;
                    else if (n == "interact-right") waveClip = st.name;
                }
                anim.cullingType = AnimationCullingType.AlwaysAnimate;
                Play(idleClip, 0f);
            }
            // Her radio's meter: bars beside the beacon that move with her voice.
            meter = new GameObject("Radio meter").transform;
            meter.SetParent(transform, false);
            meter.localPosition = new Vector3(0.12f, 0.02f, 0.02f);
            for (int i = 0; i < Bars; i++)
            {
                var bm = new MeshBuilder();
                bm.Box(new Vector3(0f, 0.5f, 0f), Vector3.one, new Color(1f, 0.8f, 0.45f), null, 1f);
                var bar = Util.MeshObject("Bar " + i, meter, bm.ToMesh("bar"), solid).transform;
                bar.localPosition = new Vector3((i - (Bars - 1) * 0.5f) * 0.012f, 0f, 0f);
                bar.localScale = new Vector3(0.007f, 0.004f, 0.007f);
                bars[i] = bar;
            }
            // The beacon she stands on, and sparks rising from it while she is there.
            var disc = new MeshBuilder();
            disc.Cylinder(Vector3.zero, 0.09f, 0.08f, 0.012f, 24, new Color(1f, 0.72f, 0.4f), true, false, 1f);
            Util.MeshObject("Beacon", transform, disc.ToMesh("beacon"), solid);
            sparks = Effects.Embers(transform, glow, 10f);
            sparks.transform.localPosition = Vector3.up * 0.01f;
            var sh = sparks.shape; sh.radius = 0.07f;
            rends = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            transform.localScale = Vector3.one * 0.001f;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        void Play(string clip, float fade)
        {
            if (anim == null || string.IsNullOrEmpty(clip) || anim[clip] == null) return;
            if (fade <= 0f) anim.Play(clip); else anim.CrossFade(clip, fade);
            if (clip != idleClip && idleClip != null) anim.CrossFadeQueued(idleClip, 0.25f);
            current = clip;
        }

        /// <summary>Shakes her head (a "no", when you pick something she didn't want to hear).</summary>
        public void ShakeHead() => Play(noClip, 0.15f);

        const int Bars = 7;
        readonly Transform[] bars = new Transform[Bars];
        Transform meter;
        float level;

        void Meter(float now, float dt)
        {
            if (meter == null) return;
            // Face the player along with her, and rise and fall with her voice; static scatters the bars.
            meter.localRotation = body.localRotation;
            level = Mathf.Lerp(level, Mathf.Clamp01(Voice), 1f - Mathf.Exp(-dt * 18f));
            float bad = badShown;
            for (int i = 0; i < Bars; i++)
            {
                if (bars[i] == null) continue;
                float shape = 1f - Mathf.Abs(i - (Bars - 1) * 0.5f) / Bars;
                float wobble = Mathf.PerlinNoise(now * 9f + i * 1.7f, i * 0.3f);
                float h = 0.004f + level * 0.07f * shape * (0.5f + wobble);
                if (bad > 0.3f) h += bad * 0.03f * Mathf.PerlinNoise(now * 31f, i * 5.1f);
                var sc = bars[i].localScale; sc.y = h; bars[i].localScale = sc;
            }
        }

        void Animate(float now)
        {
            if (anim == null) return;
            if (!anim.isPlaying) Play(idleClip, 0.2f);
            else if (idleClip != null && anim.IsPlaying(idleClip) && !anim.IsPlaying(current)) current = idleClip;
            bool waving = now < waveUntil;
            if (waving && !wavePlayed) { wavePlayed = true; Play(waveClip, 0.15f); nextGesture = now + 1.2f; }
            else if (waving && now > nextGesture) { Play(waveClip, 0.15f); nextGesture = now + 1.2f; }
            if (!waving) wavePlayed = false;
            // Talking: a nod as she starts, then now and then while she goes on.
            if (Talking && !waving && (!wasTalking || now > nextGesture))
            {
                Play(nodClip, 0.15f);
                nextGesture = now + Random.Range(2.4f, 3.6f);
            }
            wasTalking = Talking;
            // Her clips play on the world's clock: Freeze holds her still.
            foreach (AnimationState st in anim) st.speed = Time.timeScale <= 0.001f ? 0f : 1f;
        }

        static Transform Part(string name, Transform parent, Material m, System.Action<MeshBuilder> build, Vector3 at)
        {
            var mb = new MeshBuilder();
            build(mb);
            var go = Util.MeshObject(name, parent, mb.ToMesh(name), m);
            go.transform.localPosition = at;
            return go.transform;
        }

        void Build(Material holo, Material glow, float height)
        {
            Height = height;
            var w = Color.white;
            // Built at human size (1.75 m), then scaled down to stand on the table.
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            body.localScale = Vector3.one * (height / 1.75f);
            // Legs, boots, hips.
            Part("Leg L", body, holo, mb => { mb.Cylinder(Vector3.zero, 0.075f, 0.09f, 0.88f, 10, w, true, true); mb.Box(new Vector3(0f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 0.24f), w); }, new Vector3(-0.1f, 0f, 0f));
            Part("Leg R", body, holo, mb => { mb.Cylinder(Vector3.zero, 0.075f, 0.09f, 0.88f, 10, w, true, true); mb.Box(new Vector3(0f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 0.24f), w); }, new Vector3(0.1f, 0f, 0f));
            // Flight suit: hips, a torso wider at the chest, a collar, a belt.
            Part("Torso", body, holo, mb =>
            {
                mb.Box(new Vector3(0f, 0.93f, 0f), new Vector3(0.34f, 0.16f, 0.2f), w);
                mb.Cylinder(new Vector3(0f, 0.98f, 0f), 0.15f, 0.2f, 0.46f, 12, w, true, false);
                mb.Cylinder(new Vector3(0f, 1.42f, 0f), 0.075f, 0.065f, 0.1f, 10, w, false, false);
                mb.Box(new Vector3(0f, 1.0f, 0.1f), new Vector3(0.3f, 0.05f, 0.03f), w);
                mb.Box(new Vector3(0.08f, 1.3f, 0.17f), new Vector3(0.08f, 0.05f, 0.02f), w);   // the badge
                mb.Box(new Vector3(-0.2f, 1.41f, 0f), new Vector3(0.12f, 0.05f, 0.16f), w);   // shoulder pads
                mb.Box(new Vector3(0.2f, 1.41f, 0f), new Vector3(0.12f, 0.05f, 0.16f), w);
                mb.Cylinder(new Vector3(0f, 0.86f, 0f), 0.21f, 0.18f, 0.14f, 12, w, false, false);   // jacket hem
                mb.Cylinder(new Vector3(0f, 1.36f, 0f), 0.11f, 0.1f, 0.08f, 12, w, true, false);              // scarf
                mb.Box(new Vector3(0.07f, 1.22f, 0.12f), new Vector3(0.07f, 0.2f, 0.03f), w);                 // its tail
            }, Vector3.zero);
            // Head: face, hair tied back, a headset mic.
            head = Part("Head", body, holo, mb =>
            {
                mb.Ico(new Vector3(0f, 0.11f, 0f), new Vector3(0.095f, 0.115f, 0.1f), w, null, 0f);
                mb.Ico(new Vector3(0f, 0.16f, -0.02f), new Vector3(0.108f, 0.085f, 0.11f), w, null, 0f);   // cap-helmet
                mb.Box(new Vector3(0f, 0.205f, 0.07f), new Vector3(0.2f, 0.02f, 0.06f), w);                  // its brim
                mb.Ico(new Vector3(0f, 0.11f, -0.14f), new Vector3(0.04f, 0.05f, 0.045f), w, null, 0f);     // ponytail
                mb.Ico(new Vector3(0f, 0.03f, -0.17f), new Vector3(0.03f, 0.07f, 0.03f), w, null, 0f);
                mb.Box(new Vector3(0.1f, 0.1f, 0.03f), new Vector3(0.02f, 0.05f, 0.05f), w);
                mb.Box(new Vector3(0.07f, 0.04f, 0.09f), new Vector3(0.015f, 0.015f, 0.08f), w);
            }, new Vector3(0f, 1.5f, 0f));
            head.localScale = Vector3.one * 1.3f;
            // Eyes and the line of a headset visor, in plain glow, so her face reads at a glance.
            var face = new MeshBuilder();
            face.Ico(new Vector3(-0.038f, 0.12f, 0.09f), Vector3.one * 0.026f, new Color(1f, 0.97f, 0.9f), null, 0f, 1f);
            face.Ico(new Vector3(0.038f, 0.12f, 0.09f), Vector3.one * 0.026f, new Color(1f, 0.97f, 0.9f), null, 0f, 1f);
            face.Box(new Vector3(0f, 0.065f, 0.094f), new Vector3(0.045f, 0.01f, 0.01f), new Color(1f, 0.85f, 0.6f), null, 1f);   // mouth
            face.Box(new Vector3(0f, 0.19f, 0.105f), new Vector3(0.19f, 0.03f, 0.03f), new Color(1f, 0.8f, 0.5f), null, 1f);      // visor band
            Util.MeshObject("Face", head, face.ToMesh("face"), solid);
            // Arms: shoulder pivots, so she can gesture and wave.
            armL = Arm("Arm L", holo, -1f, out foreL);
            armR = Arm("Arm R", holo, 1f, out foreR);
            // The beacon she stands on.
            var disc = new MeshBuilder();
            disc.Cylinder(Vector3.zero, 0.07f, 0.06f, 0.012f, 20, new Color(1f, 0.72f, 0.4f), true, false, 1f);
            Util.MeshObject("Beacon", transform, disc.ToMesh("beacon"), solid);
            // Sparks rise from the beacon while she is there (warm, like the campfire's).
            sparks = Effects.Embers(transform, glow, 10f);
            sparks.transform.localPosition = Vector3.up * 0.01f;
            var sh = sparks.shape; sh.radius = 0.05f;
            rends = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            transform.localScale = Vector3.one * 0.001f;
        }

        Transform Arm(string name, Material m, float side, out Transform fore)
        {
            var w = Color.white;
            var pivot = new GameObject(name).transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.22f * side, 1.4f, 0f);
            Part("Upper", pivot, m, mb => mb.Cylinder(new Vector3(0f, -0.3f, 0f), 0.045f, 0.055f, 0.3f, 8, w, true, true), Vector3.zero);
            var elbow = new GameObject("Elbow").transform;
            elbow.SetParent(pivot, false);
            elbow.localPosition = new Vector3(0f, -0.3f, 0f);
            Part("Fore", elbow, m, mb => { mb.Cylinder(new Vector3(0f, -0.27f, 0f), 0.04f, 0.045f, 0.27f, 8, w, true, true); mb.Ico(new Vector3(0f, -0.31f, 0f), new Vector3(0.04f, 0.05f, 0.03f), w, null, 0f); }, Vector3.zero);
            fore = elbow;
            pivot.localRotation = Quaternion.Euler(0f, 0f, 8f * side);
            return pivot;
        }

        public void Appear() { target = 1f; }
        public void Vanish() { target = 0f; }
        public bool Gone => shown <= 0.001f && target <= 0f;
        public void Wave(float seconds = 2.6f) => waveUntil = Time.unscaledTime + seconds;
        public void Glitch(float seconds = 0.6f) => glitchUntil = Time.unscaledTime + seconds;

        void LateUpdate()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, target, dt / (target > shown ? 1.1f : 0.8f));
            // She builds up from the beacon in a column of light, and drains back into it.
            float s = Mathf.Max(0.001f, Util.Ease(shown));
            transform.localScale = new Vector3(Mathf.Lerp(0.2f, 1f, s), s, Mathf.Lerp(0.2f, 1f, s));
            if (Head != null)
            {
                // Face the player, turning the whole figure slowly (and, built in code, the head a little more).
                Vector3 to = Head.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) body.rotation = Quaternion.Slerp(body.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 2.5f));
                Vector3 look = Head.position - head.position;
                if (!IsFigure && look.sqrMagnitude > 1e-4f) head.rotation = Quaternion.Slerp(head.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 4f));
            }
            badShown = Mathf.MoveTowards(badShown, Mathf.Clamp01(1f - Signal), dt * 1.5f);
            if (IsFigure) { Animate(now); FigureSignal(now); Meter(now, dt); return; }
            // Breathing, and hands that move while she talks.
            float breathe = 1f + 0.012f * Mathf.Sin(now * 1.7f);
            body.localScale = new Vector3(body.localScale.x, body.localScale.x * breathe, body.localScale.x);
            bool waving = now < waveUntil;
            float talk = Talking ? 1f : 0f;
            armR.localRotation = Quaternion.Slerp(armR.localRotation, waving
                ? Quaternion.Euler(0f, 0f, 150f + 18f * Mathf.Sin(now * 9f))
                : Quaternion.Euler(-20f * talk + 6f * talk * Mathf.Sin(now * 3.1f), 0f, 10f + 8f * talk), 1f - Mathf.Exp(-dt * 8f));
            foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(waving ? 0f : -35f * talk - 10f, 0f, 0f), 1f - Mathf.Exp(-dt * 8f));
            armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-12f * talk + 5f * talk * Mathf.Sin(now * 2.3f + 1f), 0f, -10f - 6f * talk), 1f - Mathf.Exp(-dt * 6f));
            foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-25f * talk - 10f, 0f, 0f), 1f - Mathf.Exp(-dt * 6f));
            // Signal: a weak one flickers, jitters sideways and fades; a glitch is a short burst of that.
            float bad = badShown;
            bool glitch = now < glitchUntil;
            float flicker = SignalDim(now, bad, glitch);
            float alpha = Mathf.Lerp(1f, 0.35f, bad) * flicker;
            body.localPosition = (glitch || bad > 0.5f) ? new Vector3((Mathf.PerlinNoise(now * 3f, 2f) - 0.5f) * 0.03f * (glitch ? 1f : bad), 0f, 0f) : Vector3.zero;
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_Tint", Tint * (0.75f + 0.25f * (1f - bad)));
                block.SetFloat("_Alpha", alpha);
                r.SetPropertyBlock(block);
            }
            if (sparks != null) { var em = sparks.emission; em.rateOverTime = shown > 0.5f ? 4f + 10f * Signal : 0f; }
        }

        /// <summary>
        /// How a weak signal dims her (1 = full). A slow, smooth swell down to 60%, never a strobe: it used to switch
        /// between full and a quarter 4-12 times a second for half a minute of the storm, a photosensitivity risk.
        /// Steady under Calm effects.
        /// </summary>
        public static float SignalDim(float now, float bad, bool glitch)
        {
            // (A glitch shows as the sideways jitter, not as a dimming: switching the depth on and off would itself be a flash.)
            if (Effects.Calm || bad <= 0.2f) return 1f;
            float depth = Mathf.Clamp01((bad - 0.2f) * 1.6f);
            float wave = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(now * 0.9f, 0.3f));
            return Mathf.Lerp(1f, 0.6f, depth * wave);
        }

        /// <summary>The figure's signal: a weak one flickers, jitters and turns see-through; the rim burns brighter.</summary>
        void FigureSignal(float now)
        {
            float bad = badShown;
            bool glitch = now < glitchUntil;
            float flicker = SignalDim(now, bad, glitch);   // the figure too: a slow swell, never a strobe
            float alpha = Mathf.Lerp(1f, 0.4f, bad) * flicker;
            // The glitch brightens her outline over a quarter second, not in one frame; not at all under Calm effects.
            rimGlitch = Mathf.MoveTowards(rimGlitch, glitch && !Effects.Calm ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            body.localPosition = (glitch || bad > 0.5f) ? new Vector3((Mathf.PerlinNoise(now * 3f, 2f) - 0.5f) * 0.03f * (glitch ? 1f : bad), 0f, 0f) : Vector3.zero;
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                if (r is SkinnedMeshRenderer)
                {
                    block.SetFloat("_Alpha", alpha);
                    block.SetFloat("_Rim", 0.6f + 1.1f * bad + 0.8f * rimGlitch);
                }
                else
                {
                    block.SetColor("_Tint", Tint * (0.75f + 0.25f * (1f - bad)));
                    block.SetFloat("_Alpha", alpha);
                }
                r.SetPropertyBlock(block);
            }
            if (sparks != null) { var em = sparks.emission; em.rateOverTime = shown > 0.5f ? 4f + 10f * Signal : 0f; }
        }
    }
}

using System;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The real world takes on the world's light. A colour look-up table on the passthrough layer tints what
    /// you see of reality: while a world opens, your room dims towards the world's colour; once the walls have
    /// gone, passthrough only shows through your hands, so the table is how your real hands are lit (warm and
    /// flickering by the fire, blue under the sea, pink by the neon). Back in your room it fades off.
    /// Colour modes on the passthrough layer exclude each other, so brightness and saturation are baked in.
    /// </summary>
    public sealed class PassthroughLight : MonoBehaviour
    {
        public OVRPassthroughLayer Layer;
        public WorldLook Look;
        [Range(0, 1)] public float Amount;   // 0 = untouched passthrough, 1 = the world's light
        const int N = 16;

        OVRPassthroughColorLut world, fire;
        string biome = "";
        float shown = -1f;
        bool failed;

        /// <summary>Prepares the light of a world (builds two 16³ tables: about a millisecond).</summary>
        public void For(Recipe r)
        {
            if (failed || Layer == null || r == null || r.Biome == biome) return;
            // No headset runtime (editor tests, captures): the passthrough layer can't take a colour table.
            if (!OVRPlugin.initialized || !OVRManager.isHmdPresent) return;
            try
            {
                Dispose();
                var (tint, gain, sat) = Grade(r);
                world = new OVRPassthroughColorLut(Table(tint, gain, sat), OVRPassthroughColorLut.ColorChannels.Rgb);
                // The fire's flicker: a warmer, brighter version to blend towards.
                Color warm = Color.Lerp(tint, new Color(1.15f, 0.82f, 0.55f), r.Def.Build == "fire" ? 0.7f : 0.2f);
                fire = new OVRPassthroughColorLut(Table(warm, gain * 1.18f, sat), OVRPassthroughColorLut.ColorChannels.Rgb);
                if (!world.IsValid || !fire.IsValid) throw new Exception("lut not valid");
                biome = r.Biome;
                shown = -1f;
                Trace.Info("Light", "passthrough colour table ready", "world", r.Biome, "gain", gain, "sat", sat);
            }
            catch (Exception e)
            {
                failed = true;   // editor without a headset, or passthrough not ready: the app works without it
                Trace.Warn("Light", "passthrough colour table off", "error", e.Message);
                Dispose();
            }
        }

        static (Color tint, float gain, float sat) Grade(Recipe r)
        {
            var p = WorldColors.From(r.Def.Palette);
            // The light that falls on your hands: the world's ambient and sun, never darker than 55 % (camera grain).
            Color amb = Color.Lerp(p.Ambient, p.Horizon, 0.35f);
            float night = r.IsNight ? 1f : 0f;
            Color tint = Color.Lerp(Color.white, Normalise(amb), 0.55f);
            float gain = Mathf.Lerp(0.95f, 0.62f, night) * (1f + r.Light * 0.15f);
            if (r.Biome == "reef") { tint = new Color(0.62f, 0.95f, 1.05f); gain = 0.8f; }
            if (r.Biome == "space") { tint = new Color(0.85f, 0.92f, 1.08f); gain = 0.8f; }
            if (r.Biome == "city") { tint = Color.Lerp(tint, new Color(1.05f, 0.75f, 0.95f), 0.4f); }
            return (tint, Mathf.Max(0.55f, gain), r.Biome == "desert" || r.Biome == "beach" ? 1.05f : 0.85f);
        }

        static Color Normalise(Color c)
        {
            float m = Mathf.Max(0.05f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        static Color[] Table(Color tint, float gain, float sat)
        {
            var c = new Color[N * N * N];
            for (int b = 0; b < N; b++)
                for (int g = 0; g < N; g++)
                    for (int r = 0; r < N; r++)
                    {
                        var x = new Color(r / (N - 1f), g / (N - 1f), b / (N - 1f));
                        float y = 0.2126f * x.r + 0.7152f * x.g + 0.0722f * x.b;
                        var s = Color.Lerp(new Color(y, y, y), x, sat) * tint * gain;
                        c[r + g * N + b * N * N] = new Color(Mathf.Clamp01(s.r), Mathf.Clamp01(s.g), Mathf.Clamp01(s.b), 1f);
                    }
            return c;
        }

        void Update()
        {
            if (Layer == null || failed || !OVRPlugin.initialized) return;
            float a = Mathf.Clamp01(Amount);
            if (a <= 0.001f || world == null)
            {
                if (shown != 0f) { try { Layer.DisableColorMap(); } catch { } shown = 0f; }
                return;
            }
            // Between untouched and the world's light, the fire's flicker riding on top.
            float flicker = Look != null && Look.FireStrength > 0f
                ? Mathf.Clamp01(0.25f + (Effects.Calm ? 0.15f : 0.3f * Mathf.PerlinNoise(Time.time * 3f, 0.7f)) * Mathf.Min(1f, Look.FireStrength))
                : 0f;
            try
            {
                if (flicker > 0f && a > 0.99f) Layer.SetColorLut(world, fire, flicker);
                else Layer.SetColorLut(world, a);
                shown = a;
            }
            catch (Exception e) { failed = true; Debug.Log("[Worldeck] passthrough light off: " + e.Message); }
        }

        void Dispose()
        {
            world?.Dispose(); fire?.Dispose();
            world = fire = null;
            biome = "";
        }

        void OnDestroy() => Dispose();
    }
}

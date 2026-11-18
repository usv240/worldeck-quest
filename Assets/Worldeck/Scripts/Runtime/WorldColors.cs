using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// One world's colours as Unity colours (gamma-space sRGB, as authored), after the recipe's warmth and
    /// light knobs. Meshes take albedo from here; the World shader takes sky, fog, sun and ambient.
    /// </summary>
    public struct WorldColors
    {
        public Color Top, Horizon, Fog, Ground, GroundAccent, Prop, PropAccent, Emissive, Ambient, Neon;
        /// <summary>Composed worlds: how far artist-made props lean to this palette (0 their own colours).</summary>
        public float ModelTint;

        public static Color Hex(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        public static WorldColors From(Palette p)
        {
            return new WorldColors
            {
                Top = Hex(p.Top, Color.black), Horizon = Hex(p.Horizon, Color.gray), Fog = Hex(p.Fog, Color.gray),
                Ground = Hex(p.Ground, Color.gray), GroundAccent = Hex(p.GroundAccent, Color.gray),
                Prop = Hex(p.Prop, Color.gray), PropAccent = Hex(p.PropAccent, Color.gray),
                Emissive = Hex(p.Emissive, Color.white), Ambient = Hex(p.Ambient, Color.gray),
                Neon = Hex(p.Neon, Hex(p.Emissive, Color.white))
            };
        }

        /// <summary>The world's colours for a recipe: its palette, shifted warmer/colder (albedo only; sky and light come from <see cref="Sky"/>).</summary>
        public static WorldColors For(Recipe r)
        {
            var c = From(r.Def.Palette);
            c.ModelTint = r.Def.ModelTint;
            if (Mathf.Abs(r.Warmth) > 0.01f)
            {
                Color warm = new Color(1.08f, 1.0f, 0.88f), cold = new Color(0.9f, 0.97f, 1.1f);
                Color k = r.Warmth > 0 ? Color.Lerp(Color.white, warm, r.Warmth) : Color.Lerp(Color.white, cold, -r.Warmth);
                c.Ground *= k; c.GroundAccent *= k; c.Prop *= k; c.PropAccent *= k;
            }
            return c;
        }

        /// <summary>Scales a colour's value by up to ±<paramref name="amount"/> (art rule: value jitter ±8 %).</summary>
        public static Color Jitter(Color c, Rng rng, float amount = 0.08f)
        {
            float f = 1f + (rng.Next01() * 2f - 1f) * amount;
            return new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
        }
    }
}

using System;
using System.Text;

namespace Worldeck.Core
{
    /// <summary>
    /// A world as 11 characters ("K7QM-2XPA-9RD"): world, time, weather, knobs, extras and seed, with a
    /// 6-bit check so a typo is refused instead of building the wrong place. The code carries the world,
    /// not the room: a friend's headset fits the same world to their own furniture.
    /// Crockford base32 (no I, L, O, U), so it reads aloud and types cleanly.
    /// </summary>
    public static class ShareCode
    {
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const int Version = 1;
        public const int Length = 11;

        public static string Encode(Recipe r)
        {
            r = r.Copy().Clamp();
            ulong bits = 0;
            int at = 0;
            void Put(int value, int width) { bits |= ((ulong)value & ((1UL << width) - 1)) << at; at += width; }

            Put(Version, 2);
            Put(Math.Max(0, Kit.IndexOf(r.Biome)), 4);
            Put((int)Math.Round(r.Hour * 2f) % 48, 6);
            Put(Math.Max(0, Array.IndexOf(Precip.All, r.Precip)), 2);
            Put(Q(r.Fog, 0f, 1f), 3);
            Put(Q(r.Wind, 0f, 1f), 3);
            Put(Q(r.Density, Recipe.MinDensity, Recipe.MaxDensity), 3);
            Put(Q(r.Warmth, -1f, 1f), 3);
            Put(Q(r.Light, -1f, 1f), 3);
            int flags = 0;
            for (int i = 0; i < Extras.All.Length && i < 8; i++) if (r.Extras.Contains(Extras.All[i])) flags |= 1 << i;
            Put(flags, 8);
            Put(r.Seed & 0xFFF, 12);
            Put(Check(bits, 49), 6);

            var sb = new StringBuilder();
            for (int i = 0; i < Length; i++)
            {
                sb.Append(Alphabet[(int)((bits >> (5 * i)) & 31)]);
                if (i == 3 || i == 7) sb.Append('-');
            }
            return sb.ToString();
        }

        /// <summary>The world in a code, or null if it is not a valid code (wrong length, letters or check).</summary>
        public static Recipe Decode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var clean = new StringBuilder();
            foreach (char ch in code.ToUpperInvariant())
            {
                char c = ch == 'O' ? '0' : ch == 'I' || ch == 'L' ? '1' : ch;   // Crockford's forgiving reads
                if (Alphabet.IndexOf(c) >= 0) clean.Append(c);
                else if (c != '-' && c != ' ') return null;
            }
            if (clean.Length != Length) return null;
            ulong bits = 0;
            for (int i = 0; i < Length; i++) bits |= (ulong)Alphabet.IndexOf(clean[i]) << (5 * i);

            int at = 0;
            int Get(int width) { int v = (int)((bits >> at) & ((1UL << width) - 1)); at += width; return v; }
            if (Get(2) != Version) return null;
            int biome = Get(4);
            int half = Get(6);
            int precip = Get(2);
            int fog = Get(3), wind = Get(3), density = Get(3), warmth = Get(3), light = Get(3);
            int flags = Get(8);
            int seed = Get(12);
            int check = Get(6);
            if (check != Check(bits & ((1UL << 49) - 1), 49)) return null;
            if (biome >= Kit.Biomes.Length || half >= 48 || precip >= Precip.All.Length) return null;

            var r = Recipe.For(Kit.Biomes[biome].Id, seed);
            r.Hour = half / 2f;
            r.Precip = Precip.All[precip];
            r.Fog = D(fog, 0f, 1f);
            r.Wind = D(wind, 0f, 1f);
            r.Density = D(density, Recipe.MinDensity, Recipe.MaxDensity);
            r.Warmth = D(warmth, -1f, 1f);
            r.Light = D(light, -1f, 1f);
            r.Extras.Clear();
            for (int i = 0; i < Extras.All.Length && i < 8; i++) if ((flags & (1 << i)) != 0) r.Extras.Add(Extras.All[i]);
            r.Title = "Shared: " + r.Def.Name;
            return r.Clamp();
        }

        static int Q(float v, float lo, float hi) => (int)Math.Round(Math.Max(0f, Math.Min(1f, (v - lo) / (hi - lo))) * 7f);
        static float D(int q, float lo, float hi) => lo + (hi - lo) * q / 7f;

        static int Check(ulong bits, int width)
        {
            // A small mixing hash of the payload, 6 bits: refuses about 63 in 64 mistyped codes.
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < width; i += 8)
            {
                h ^= (bits >> i) & 0xFF;
                h *= 1099511628211UL;
            }
            return (int)((h ^ (h >> 29)) & 63);
        }
    }
}

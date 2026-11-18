using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck
{
    /// <summary>
    /// Props made by an artist: CC0 models from Kenney's kits (kenney.nl), turned by Tools/kit/build-kit.py into flat
    /// coloured triangles (Resources/Kit/kit.bytes). They go into the same merged world meshes as everything else, so
    /// they take the world's fog, light, wind and the walls' dissolve with no extra draw calls. A kind that is not in
    /// the kit is still made in code (<see cref="Props"/>).
    /// </summary>
    public static class ModelKit
    {
        public sealed class Model
        {
            public string Kind;
            public Color32[] Palette;
            public float Width;
            public short[] Pos;   // 9 per triangle, × 1/8192, base at y = 0, 1 unit tall
            public byte[] Col;    // 1 per triangle
            public int Triangles => Col.Length;
        }

        /// <summary>Off: every prop is made in code again (for comparison and as a fallback).</summary>
        public static bool Enabled = true;

        static Dictionary<string, List<Model>> kinds;
        public static int ModelCount { get; private set; }

        static readonly HashSet<string> Sways = new HashSet<string> { "pine", "snow_pine", "palm", "oak", "fern", "bush", "flower_patch", "flower", "dune_grass", "potted_plant" };
        static readonly HashSet<string> Small = new HashSet<string> { "fern", "bush", "flower_patch", "flower", "dune_grass" };
        static readonly HashSet<string> Lit = new HashSet<string> { "street_lamp", "lantern_hanging", "console", "antenna" };

        static void Load()
        {
            if (kinds != null) return;
            kinds = new Dictionary<string, List<Model>>();
            var asset = Resources.Load<TextAsset>("Kit/kit");
            if (asset == null) { Trace.Warn("Kit", "no model kit: props are made in code"); return; }
            try { Read(asset.bytes); }
            catch (Exception e) { kinds.Clear(); ModelCount = 0; Trace.Warn("Kit", "model kit unreadable: props are made in code", "error", e.Message); }
            Resources.UnloadAsset(asset);
        }

        /// <summary>Reads a kit file (public for tests).</summary>
        public static void Read(byte[] bytes)
        {
            kinds ??= new Dictionary<string, List<Model>>();
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WKIT") throw new InvalidDataException("not a kit");
            int version = r.ReadUInt16(), count = r.ReadUInt16();
            if (version != 1) throw new InvalidDataException("kit version " + version);
            for (int m = 0; m < count; m++)
            {
                string key = Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
                int pal = r.ReadByte();
                var palette = new Color32[pal];
                for (int i = 0; i < pal; i++) { var b = r.ReadBytes(4); palette[i] = new Color32(b[0], b[1], b[2], b[3]); }
                float width = r.ReadSingle();
                int tris = (int)r.ReadUInt32();
                var pos = new short[tris * 9];
                var col = new byte[tris];
                for (int t = 0; t < tris; t++)
                {
                    for (int k = 0; k < 9; k++) pos[t * 9 + k] = r.ReadInt16();
                    col[t] = r.ReadByte();
                }
                string kind = key.Contains("#") ? key.Substring(0, key.IndexOf('#')) : key;
                if (!kinds.TryGetValue(kind, out var list)) kinds[kind] = list = new List<Model>();
                list.Add(new Model { Kind = kind, Palette = palette, Width = width, Pos = pos, Col = col });
                ModelCount++;
            }
        }

        public static bool Has(string kind)
        {
            if (!Enabled) return false;
            Load();
            return kinds.ContainsKey(kind);
        }

        /// <summary>
        /// Which of a kind's models the next Build uses (-1: any, by the seed). Set it, build, and set it back:
        /// WorldBuilder does for the person who lives in a world, so the pirate captain is always the bearded one.
        /// </summary>
        public static int Variant = -1;

        public static IReadOnlyList<Model> Models(string kind)
        {
            Load();
            return kinds.TryGetValue(kind, out var l) ? l : (IReadOnlyList<Model>)Array.Empty<Model>();
        }

        /// <summary>
        /// Adds one of <paramref name="kind"/>'s models, <paramref name="height"/> metres tall, standing on the origin.
        /// Its colours lean a little toward the world's light so it belongs there.
        /// </summary>
        public static bool Build(MeshBuilder mb, string kind, WorldColors c, Rng rng, float height)
        {
            if (!Has(kind) || mb == null) return false;
            var list = kinds[kind];
            int pick = rng.Range(0, list.Count);
            // A named someone (a world's local) is always the same one of the people.
            var model = list[Variant >= 0 ? Variant % list.Count : pick];
            bool sway = Sways.Contains(kind), small = Small.Contains(kind), lit = Lit.Contains(kind);
            bool snowy = kind == "snow_pine" || kind == "snow_rock";
            Color snow = new Color(0.9f, 0.94f, 1f);
            var cols = new Color[model.Palette.Length];
            var glow = new float[model.Palette.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                Color k = model.Palette[i];
                Color.RGBToHSV(k, out float hh, out float s, out float v);
                // Bright warm or white parts of lamps and screens give light.
                glow[i] = lit && v > 0.9f && (s < 0.25f || (hh > 0.08f && hh < 0.2f)) ? 0.9f : 0f;
                Color own = Color.Lerp(k, c.Ambient, 0.18f);
                // Leaves in the world's own green (the kit's trees are teal; a meadow's are not): the model's light
                // and dark kept, its hue most of the way to the world's foliage colour.
                if (sway && s > 0.2f && hh > 0.2f && hh < 0.6f)
                {
                    Color.RGBToHSV(c.Prop, out float ph, out float ps, out float _);
                    if (ps > 0.15f)
                    {
                        Color leaf = Color.HSVToRGB(Mathf.Lerp(hh, ph, 0.75f), Mathf.Min(0.72f, Mathf.Lerp(s, ps, 0.5f)), v);
                        own = Color.Lerp(leaf, c.Ambient, 0.18f);
                    }
                }
                // A composed world (a red Mars, a violet cavern): the model's light and dark kept, its colour the world's.
                if (c.ModelTint > 0f)
                {
                    Color target = v < 0.5f ? c.Prop : Color.Lerp(c.Prop, c.PropAccent, 0.5f);
                    own = Color.Lerp(own, target * (0.6f + 0.6f * v), c.ModelTint);
                }
                cols[i] = glow[i] > 0f ? (Color)k : WorldColors.Jitter(own, rng, 0.04f);
            }
            float s9 = height / 8192f;
            var p = model.Pos;
            for (int t = 0; t < model.Triangles; t++)
            {
                int o = t * 9;
                var a = new Vector3(-p[o] * s9, p[o + 1] * s9, p[o + 2] * s9);
                var b = new Vector3(-p[o + 3] * s9, p[o + 4] * s9, p[o + 5] * s9);
                var d = new Vector3(-p[o + 6] * s9, p[o + 7] * s9, p[o + 8] * s9);
                int ci = model.Col[t];
                Color col = cols[ci];
                // Snow settles on what faces up.
                if (snowy && Vector3.Cross(d - a, b - a).normalized.y > 0.45f) col = Color.Lerp(snow, c.Ambient, 0.12f);
                // OBJ is right-handed: mirror x and reverse the winding, as Unity's own importer does.
                mb.Tri(a, d, b, col, glow[ci], Wind(a.y, height, sway, small), Wind(d.y, height, sway, small), Wind(b.y, height, sway, small));
            }
            return true;
        }

        static float Wind(float y, float h, bool sway, bool small)
        {
            if (!sway || h <= 0f) return 0f;
            float t = Mathf.Clamp01(y / h);
            return small ? t : Mathf.Clamp01((t - 0.3f) / 0.7f);
        }
    }
}

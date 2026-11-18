using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Photographic ground sets (Poly Haven, CC0), made by Tools/make_grounds.py: which set each world walks on,
    /// and each set's average colour, so the Ground shader keeps the world's palette and adds the photo's detail.
    /// </summary>
    public sealed class Grounds : MonoBehaviour
    {
        public Texture2D[] Albedo, Normal;
        public TextAsset Meta;

        public static readonly Dictionary<string, string> ForBiome = new Dictionary<string, string>
        {
            { "campfire", "forest_floor" }, { "beach", "sand" }, { "desert", "dunes" }, { "snow", "snow" },
            { "city", "cobbles" }, { "meadow", "grass" }, { "reef", "coral_gravel" }, { "space", "moon" }
        };

        public struct Set { public string Name; public Texture2D Albedo, Normal; public Color Mean; public float Metres; }

        Dictionary<string, Set> map;

        void Ensure()
        {
            if (map != null) return;
            map = new Dictionary<string, Set>();
            var meta = Meta != null ? Json.Parse(Meta.text) as Dictionary<string, object> : null;
            if (Albedo == null) return;
            foreach (var a in Albedo)
            {
                if (a == null || !a.name.EndsWith("_albedo", StringComparison.Ordinal)) continue;
                string name = a.name.Substring(0, a.name.Length - "_albedo".Length);
                Texture2D n = null;
                if (Normal != null) foreach (var x in Normal) if (x != null && x.name == name + "_normal") n = x;
                var set = new Set { Name = name, Albedo = a, Normal = n, Mean = new Color(0.3f, 0.25f, 0.2f), Metres = 2f };
                if (meta != null && meta.TryGetValue(name, out var m) && m is Dictionary<string, object> o)
                {
                    var mean = Json.Arr(o, "mean");
                    if (mean != null && mean.Count >= 3) set.Mean = new Color(Convert.ToSingle(mean[0]), Convert.ToSingle(mean[1]), Convert.ToSingle(mean[2]), 1f);
                    set.Metres = (float)Json.Num(o, "metres", 2.0);
                }
                map[name] = set;
            }
        }

        public bool TryFor(string biome, out Set set)
        {
            set = default;
            Ensure();
            return biome != null && ForBiome.TryGetValue(biome, out var name) && map.TryGetValue(name, out set) && set.Albedo != null;
        }

        static MaterialPropertyBlock block;

        /// <summary>Puts this world's ground set on a renderer drawn with the Ground material.</summary>
        public void Apply(Renderer r, Set set)
        {
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetTexture("_Albedo", set.Albedo);
            if (set.Normal != null) block.SetTexture("_Normal", set.Normal);
            block.SetColor("_Mean", set.Mean);
            block.SetFloat("_Repeat", set.Metres);
            r.SetPropertyBlock(block);
        }
    }
}

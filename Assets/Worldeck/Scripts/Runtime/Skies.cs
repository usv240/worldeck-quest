using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Photographed skies (Poly Haven, CC0), made by Tools/make_skies.py: for a world and an hour, the photo that
    /// fits, and what was measured from it (where its sun is, the sun's colour, the horizon's colour, the sky's
    /// average light), so the world's own light, fog and ambient can match the photo.
    /// </summary>
    public sealed class Skies : MonoBehaviour
    {
        public Texture2D[] Photos;
        public TextAsset Meta;

        public struct Sky
        {
            public string Id, Kind;
            public Texture2D Texture;
            public bool HasSun;
            public Vector3 Sun;
            public Color SunColor, Horizon, Average, Ground;
        }

        /// <summary>For each world, which photo goes with which hour (24 h clock). Reef and space keep their own skies.</summary>
        public static readonly Dictionary<string, (string id, float hour)[]> Table = new Dictionary<string, (string, float)[]>
        {
            { "campfire", new[] { ("campfire_night", 22f), ("campfire_dawn", 6f), ("campfire_day", 12f), ("campfire_sunset", 15.5f), ("open_sunset", 18.5f) } },
            { "beach", new[] { ("beach_sunset", 18.5f), ("beach_day", 12f), ("open_dawn", 6f), ("open_moonrise", 22f) } },
            { "desert", new[] { ("desert_golden", 17f), ("desert_day", 12f), ("desert_dawn", 6f), ("desert_night", 23f) } },
            { "snow", new[] { ("open_moonrise", 22f), ("snow_sunrise", 7.5f), ("snow_day", 12f), ("open_sunset", 18f) } },
            { "city", new[] { ("city_night", 22f), ("city_day", 12f), ("open_dusk", 19f) } },
            { "meadow", new[] { ("meadow_morning", 9f), ("meadow_evening", 18f), ("meadow_night", 22f), ("open_dawn", 6f) } },
        };

        Dictionary<string, Sky> map;

        void Ensure()
        {
            if (map != null) return;
            map = new Dictionary<string, Sky>();
            var meta = Meta != null ? Json.Parse(Meta.text) as Dictionary<string, object> : null;
            if (Photos == null) return;
            foreach (var tex in Photos)
            {
                if (tex == null) continue;
                var sky = new Sky { Id = tex.name, Texture = tex, Kind = "day", SunColor = Color.white, Horizon = Color.gray, Average = Color.gray, Ground = Color.gray };
                if (meta != null && meta.TryGetValue(tex.name, out var m) && m is Dictionary<string, object> o)
                {
                    sky.Kind = Json.Str(o, "kind", "day");
                    var sun = Vec(o, "sun");
                    sky.HasSun = sun.HasValue;
                    if (sun.HasValue) sky.Sun = sun.Value.normalized;
                    sky.SunColor = Col(o, "sun_color", Color.white);
                    sky.Horizon = Col(o, "horizon", Color.gray);
                    sky.Average = Col(o, "sky", Color.gray);
                    sky.Ground = Col(o, "ground", Color.gray);
                }
                map[tex.name] = sky;
            }
        }

        static Vector3? Vec(Dictionary<string, object> o, string key)
        {
            var a = Json.Arr(o, key);
            if (a == null || a.Count < 3) return null;
            return new Vector3(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2]));
        }

        static Color Col(Dictionary<string, object> o, string key, Color fallback)
        {
            var v = Vec(o, key);
            return v.HasValue ? new Color(v.Value.x, v.Value.y, v.Value.z, 1f) : fallback;
        }

        public int Count { get { Ensure(); return map.Count; } }

        /// <summary>The photo for this world at this hour: the one closest in time, if within 4.5 hours.</summary>
        public bool TryPick(Recipe r, out Sky sky)
        {
            sky = default;
            if (r == null) return false;
            Ensure();
            if (!Table.TryGetValue(r.SkyBiome, out var list)) return false;   // "none": a sky painted from the world's colours
            float best = 4.5f;
            string pick = null;
            foreach (var (id, hour) in list)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(r.Hour * 15f, hour * 15f)) / 15f;
                if (d < best && map.ContainsKey(id)) { best = d; pick = id; }
            }
            if (pick == null) return false;
            sky = map[pick];
            return true;
        }
    }
}

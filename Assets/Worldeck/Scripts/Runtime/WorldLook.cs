using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The light of a world: sun or moon direction and colour, ambient, sky gradient, fog, stars, wind and
    /// the fire light, all from the recipe's world, hour and weather. Changes glide over
    /// <see cref="BlendSeconds"/> (no flash: comfort rule, luminance ramps ≥ 0.8 s). Sets the shader globals
    /// in WorldeckCommon.hlsl every frame and the sky material's colours.
    /// </summary>
    public sealed class WorldLook : MonoBehaviour
    {
        public Material Sky;
        [Tooltip("Photographed skies; the world's light, fog and ambient follow the photo.")]
        public Skies Photos;
        /// <summary>Which way the player faces at the table (a photo without a sun puts its middle there).</summary>
        public Vector3 Facing = Vector3.forward;
        public string PhotoId { get; private set; } = "";
        Texture2D photo;
        float photoYaw, photoMix = 1f, photoAmount, photoTarget;
        Color photoTint = Color.white;
        float photoSat = 1f;
        public float BlendSeconds = 1.4f;

        /// <summary>Where the fire is (world) and how strongly it burns (0 = none, 1 = a campfire).</summary>
        public Vector3 FirePosition;
        public float FireStrength;
        public Color FireColor = new Color(1f, 0.6f, 0.24f);
        /// <summary>0 clear … 1 all fog: one world dissolving into the next without a flash.</summary>
        public float Veil;
        /// <summary>The emergency: how much power the lights get (0.45–1), a red alarm (0–1), colour drained while rewinding (0–1).</summary>
        public float Power = 1f, Alarm, Desaturate;

        public struct State
        {
            public Color Top, Horizon, Below, SunColor, Ambient, Fog;
            public Vector3 SunDir;
            public float FogDensity, Exposure, Stars, Moon, Wind;
        }

        State from, to, now;
        float t = 1f;
        bool has;

        public State Now => now;
        public bool Blending => t < 1f;

        static readonly int SunDirId = Shader.PropertyToID("_SwSunDir"), SunColorId = Shader.PropertyToID("_SwSunColor"),
            AmbientId = Shader.PropertyToID("_SwAmbient"), FogColorId = Shader.PropertyToID("_SwFogColor"),
            FogDensityId = Shader.PropertyToID("_SwFogDensity"), ExposureId = Shader.PropertyToID("_SwExposure"),
            WindId = Shader.PropertyToID("_SwWindDir"), FirePosId = Shader.PropertyToID("_SwFirePos"),
            FireColorId = Shader.PropertyToID("_SwFireColor"), VeilId = Shader.PropertyToID("_SwVeil");

        /// <summary>Moves the light towards a recipe (instantly with <paramref name="snap"/>).</summary>
        public void Show(Recipe r, bool snap = false)
        {
            var target = Compute(r);
            Photo(ref target, r, snap || !has);
            if (!has || snap) { from = to = now = target; t = 1f; has = true; Apply(); return; }
            from = now;
            to = target;
            t = 0f;
        }

        /// <summary>The light for the real room (souvenirs and the guide over passthrough): neutral, no fog.</summary>
        public void ShowRoom(bool snap = false)
        {
            var s = new State
            {
                Top = Color.gray, Horizon = Color.gray, Below = Color.gray, Fog = new Color(0.5f, 0.5f, 0.5f, 1f),
                SunColor = new Color(0.62f, 0.6f, 0.56f, 1f), Ambient = new Color(0.5f, 0.5f, 0.54f, 1f),
                SunDir = new Vector3(0.3f, 0.85f, -0.4f).normalized, FogDensity = 0f, Exposure = 1f, Stars = 0f, Moon = 0f, Wind = 0.2f
            };
            photoTarget = 0f;
            if (snap) photoAmount = 0f;
            if (!has || snap) { from = to = now = s; t = 1f; has = true; Apply(); return; }
            from = now; to = s; t = 0f;
        }

        /// <summary>
        /// A photographed sky for this world and hour, if there is one: the light follows it (the sun where the
        /// photo's sun is, its colour; fog in the photo's horizon colour; ambient towards the photo's sky), and the
        /// sky crossfades from the last photo. Rain and fog grey it a little.
        /// </summary>
        void Photo(ref State target, Recipe r, bool snap)
        {
            if (Photos == null || !Photos.TryPick(r, out var sky)) { photoTarget = 0f; PhotoId = ""; return; }
            float yaw;
            if (sky.HasSun)
            {
                float want = Mathf.Atan2(target.SunDir.x, target.SunDir.z), have = Mathf.Atan2(sky.Sun.x, sky.Sun.z);
                yaw = want - have;
                target.SunDir = (Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * sky.Sun).normalized;
                float strength = Mathf.Max(target.SunColor.r, Mathf.Max(target.SunColor.g, target.SunColor.b));
                target.SunColor = sky.SunColor * Mathf.Max(0.6f, strength);
                target.Moon = 0f;
            }
            else yaw = Mathf.Atan2(Facing.x, Facing.z);
            target.Fog = Color.Lerp(target.Fog, sky.Horizon, 0.85f);
            target.Ambient = Color.Lerp(target.Ambient, sky.Average * (sky.Kind == "night" ? 0.8f : 0.75f), 0.55f);
            bool wet = r.Precip != Core.Precip.None;
            photoTint = wet ? new Color(0.72f, 0.74f, 0.78f) : Color.white;
            photoSat = wet ? 0.55f : r.Fog > 0.35f ? 0.75f : 1f;
            if (sky.Texture != photo)
            {
                if (Sky != null)
                {
                    Sky.SetTexture("_Photo2", photo != null ? photo : sky.Texture);
                    Sky.SetFloat("_PhotoYaw2", photo != null ? photoYaw : yaw);
                    Sky.SetTexture("_Photo", sky.Texture);
                    // Forest photos have real trees low in the frame: keep the photo above them.
                    // (The meadow's morning photo had its tree line taken out instead: Tools/clear_skyline.py.)
                    Sky.SetFloat("_PhotoFloor", sky.Id.StartsWith("campfire", System.StringComparison.Ordinal) ? 0.5f : 0f);
                }
                photo = sky.Texture;
                photoMix = snap ? 1f : 0f;
                Trace.Info("Look", "photographed sky", "sky", sky.Id, "hour", r.Hour, "sun", sky.HasSun);
            }
            photoYaw = yaw;
            PhotoId = sky.Id;
            photoTarget = 1f;
            if (snap) photoAmount = 1f;
        }

        void Update()
        {
            if (!has) return;
            if (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.05f, BlendSeconds));
                float k = Mathf.SmoothStep(0f, 1f, t);
                now = Lerp(from, to, k);
            }
            Apply();
        }

        public void Apply()
        {
            Shader.SetGlobalVector(SunDirId, now.SunDir);
            Color sunC = now.SunColor, ambC = now.Ambient;
            if (Alarm > 0.001f)
            {
                // Steady, and a warm amber-red rather than saturated red: a full-field pulsing red is a photosensitivity risk.
                const float pulse = 0.8f;
                ambC = Color.Lerp(ambC, new Color(0.45f, 0.16f, 0.1f) * (0.8f + 0.6f * pulse), Alarm * 0.7f);
            }
            if (Desaturate > 0.001f)
            {
                float gs = sunC.grayscale, ga = ambC.grayscale;
                sunC = Color.Lerp(sunC, new Color(gs, gs, gs), Desaturate);
                ambC = Color.Lerp(ambC, new Color(ga, ga, ga * 1.15f), Desaturate);
            }
            Shader.SetGlobalColor(SunColorId, sunC);
            Shader.SetGlobalColor(AmbientId, ambC);
            Shader.SetGlobalColor(FogColorId, now.Fog);
            Shader.SetGlobalFloat(FogDensityId, now.FogDensity);
            Shader.SetGlobalFloat(ExposureId, now.Exposure * Mathf.Clamp(Power, 0.3f, 1f));
            Shader.SetGlobalFloat(VeilId, Mathf.Clamp01(Veil));
            Vector2 wd = new Vector2(0.8f, 0.6f);
            Shader.SetGlobalVector(WindId, new Vector4(wd.x, wd.y, Mathf.Lerp(0.25f, 1f, now.Wind), 0.12f));
            float flicker = Effects.Calm ? 1f : 1f + 0.12f * (Mathf.PerlinNoise(Time.time * 3f, 0.3f) - 0.5f) * 2f +
                            0.06f * (Mathf.PerlinNoise(Time.time * 7.3f, 4.1f) - 0.5f) * 2f;
            float s = Mathf.Max(0f, FireStrength);
            Shader.SetGlobalVector(FirePosId, new Vector4(FirePosition.x, FirePosition.y, FirePosition.z, s > 0f ? 0.8f + 1.6f * s : 0f));
            Shader.SetGlobalColor(FireColorId, FireColor * (s * flicker * 1.6f));
            float dt = Time.unscaledDeltaTime;
            photoMix = Mathf.MoveTowards(photoMix, 1f, dt / Mathf.Max(0.05f, BlendSeconds));
            photoAmount = Mathf.MoveTowards(photoAmount, photoTarget, dt / Mathf.Max(0.05f, BlendSeconds));
            if (Sky != null)
            {
                Sky.SetFloat("_PhotoAmount", photoAmount);
                Sky.SetFloat("_PhotoMix", Mathf.SmoothStep(0f, 1f, photoMix));
                Sky.SetFloat("_PhotoYaw", photoYaw);
                Sky.SetColor("_PhotoTint", photoTint);
                Sky.SetFloat("_PhotoSat", photoSat * (1f - Desaturate));
                Sky.SetColor("_Top", now.Top);
                Sky.SetColor("_Horizon", now.Horizon);
                Sky.SetColor("_Below", now.Below);
                Sky.SetColor("_SunColor", now.SunColor);
                Sky.SetFloat("_Stars", now.Stars);
                Sky.SetFloat("_Moon", now.Moon);
            }
        }

        static State Lerp(State a, State b, float k) => new State
        {
            Top = Color.Lerp(a.Top, b.Top, k), Horizon = Color.Lerp(a.Horizon, b.Horizon, k), Below = Color.Lerp(a.Below, b.Below, k),
            SunColor = Color.Lerp(a.SunColor, b.SunColor, k), Ambient = Color.Lerp(a.Ambient, b.Ambient, k), Fog = Color.Lerp(a.Fog, b.Fog, k),
            SunDir = Vector3.Slerp(a.SunDir, b.SunDir, k).normalized, FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, k),
            Exposure = Mathf.Lerp(a.Exposure, b.Exposure, k), Stars = Mathf.Lerp(a.Stars, b.Stars, k),
            Moon = Mathf.Lerp(a.Moon, b.Moon, k), Wind = Mathf.Lerp(a.Wind, b.Wind, k)
        };

        // ---------- the model ----------

        static readonly Color NightTop = Hex("#0B1026"), NightHor = Hex("#24325A"), NightAmb = Hex("#2E3A66");
        static readonly Color GoldTop = Hex("#3B4A8A"), GoldHor = Hex("#FF9E6B"), GoldAmb = Hex("#B07A8A");
        static readonly Color DayTop = Hex("#4F8FE0"), DayHor = Hex("#CFE6F5"), DayAmb = Hex("#8FB0D8");
        static Color Hex(string h) => WorldColors.Hex(h, Color.gray);

        /// <summary>The light for a recipe (pure, for tests and captures).</summary>
        public static State Compute(Recipe r)
        {
            var def = r.Def;
            var pal = WorldColors.From(def.Palette);
            var s = new State();
            float sunH = r.SunHeight;

            if (def.Id == "space")
            {
                s.Top = pal.Top; s.Horizon = pal.Horizon; s.Below = pal.Fog;
                s.SunDir = new Vector3(-0.55f, 0.35f, 0.76f).normalized;
                s.SunColor = new Color(1f, 0.97f, 0.92f) * 1.05f;
                s.Ambient = pal.Ambient;
                s.Stars = 1f; s.Moon = 0f;
                s.FogDensity = 0.004f;
            }
            else if (def.Id == "reef")
            {
                s.Top = pal.Top; s.Horizon = pal.Horizon; s.Below = pal.Fog;
                s.SunDir = new Vector3(0.15f, 0.95f, 0.25f).normalized;
                s.SunColor = new Color(0.75f, 0.95f, 1f) * 0.85f;
                s.Ambient = pal.Ambient * 1.1f;
                s.Stars = 0f; s.Moon = 0f;
                s.FogDensity = 0.045f;
            }
            else
            {
                float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.5f, sunH));
                float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, -0.25f, sunH));
                float gold = Mathf.Clamp01(1f - day - night);
                Color gTop = NightTop * night + GoldTop * gold + DayTop * day;
                Color gHor = NightHor * night + GoldHor * gold + DayHor * day;
                Color gAmb = NightAmb * night + GoldAmb * gold + DayAmb * day;
                // The palette is authored for the world's own hour: near it, the palette rules; far, the clock.
                float dh = Mathf.Abs(Mathf.DeltaAngle(r.Hour * 15f, def.DefaultHour * 15f)) / 15f;
                float own = 0.3f + 0.7f * (1f - Mathf.Clamp01(dh / 4f));
                // A composed place (Mars, a volcano) was coloured for itself at its hour: its colours lead.
                if (r.Look != null) own = Mathf.Max(own, 0.85f);
                s.Top = Color.Lerp(gTop, pal.Top, own);
                s.Horizon = Color.Lerp(gHor, pal.Horizon, own);
                s.Ambient = Color.Lerp(gAmb, pal.Ambient, own) * Mathf.Lerp(1f, 0.6f, night);
                s.Below = Color.Lerp(pal.Ground * 0.6f, s.Horizon, 0.35f);

                // Sun across the southern sky (az from hour); the moon opposite at night.
                float az = (r.Hour / 24f) * Mathf.PI * 2f + Mathf.PI;
                float el = Mathf.Asin(Mathf.Clamp(sunH, -1f, 1f)) * 0.85f;
                var sun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
                bool moon = night > 0.5f;
                s.SunDir = moon ? new Vector3(-sun.x, Mathf.Max(0.35f, -sun.y), -sun.z).normalized
                                : new Vector3(sun.x, Mathf.Max(0.06f, sun.y), sun.z).normalized;
                s.Moon = moon ? 1f : 0f;
                Color sunCol = Color.Lerp(new Color(1f, 0.68f, 0.42f), new Color(1f, 0.96f, 0.86f), day);
                s.SunColor = moon ? new Color(0.62f, 0.7f, 0.95f) * 0.32f : sunCol * Mathf.Lerp(0.75f, 1.05f, day);
                // Stars only once the sun is well down: none at sunset, all of them at night.
                s.Stars = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, -0.4f, sunH));
                s.FogDensity = def.Id == "city" ? 0.018f : 0.011f;

                if (r.Precip == Precip.Rain)
                {
                    float storm = r.Wind > 0.7f ? 1f : 0.6f;
                    s.SunColor *= Mathf.Lerp(1f, 0.55f, storm);
                    s.Top = Color.Lerp(s.Top, Gray(s.Top), 0.45f * storm);
                    s.Horizon = Color.Lerp(s.Horizon, Gray(s.Horizon), 0.4f * storm);
                    s.Stars *= 0.25f;
                    s.FogDensity += 0.008f;
                }
                else if (r.Precip == Precip.Snow)
                {
                    s.Ambient *= 1.12f;
                    s.Horizon = Color.Lerp(s.Horizon, Color.white * 0.85f, 0.2f);
                    s.FogDensity += 0.006f;
                }
            }

            if (Mathf.Abs(r.Warmth) > 0.01f)
            {
                Color k = r.Warmth > 0 ? Color.Lerp(Color.white, new Color(1.1f, 0.98f, 0.85f), r.Warmth)
                                       : Color.Lerp(Color.white, new Color(0.88f, 0.97f, 1.12f), -r.Warmth);
                s.Top *= k; s.Horizon *= k; s.Ambient *= k; s.SunColor *= k;
            }
            s.Fog = s.Horizon;
            s.FogDensity += r.Fog * 0.055f;
            s.Exposure = 1f + r.Light * 0.35f;
            s.Wind = r.Wind;
            s.Top.a = s.Horizon.a = s.Below.a = s.Fog.a = s.Ambient.a = s.SunColor.a = 1f;
            return s;
        }

        static Color Gray(Color c) { float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; return new Color(g, g, g, c.a); }
    }
}

// Worldeck — the ground under your feet, photographic: a Poly Haven texture set (CC0) mapped from above in world
// space, two scales blended so it never visibly repeats, its detail laid over the world's own palette (vertex
// colour × photo / photo's average), a normal map for real relief, soft per-pixel light from the sun, the sky and
// the fire. Detail fades out with distance into the fog. Opaque, alpha 1 (passthrough rule).
// Trees, rocks and your furniture cast real shadows on it (SunShadows.cs), and highlights roll off (SwTone).
Shader "Worldeck/Ground"
{
    Properties
    {
        _Albedo ("Albedo", 2D) = "grey" {}
        [Normal] _Normal ("Normal (GL)", 2D) = "bump" {}
        _Mean ("Albedo average (linear)", Color) = (0.5, 0.5, 0.5, 1)
        _Repeat ("Metres per repeat", Float) = 2
        _Detail ("Photo detail", Range(0, 1)) = 0.85
        _Bump ("Relief", Range(0, 2)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _EmissiveBoost ("Emissive boost", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Ground"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Mean, _Tint;
                float _Repeat, _Detail, _Bump, _EmissiveBoost;
            CBUFFER_END
            TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
            TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
            // What stands between the sun and the ground (SunShadows.cs): for each ray from the sun, how far along
            // it the nearest thing is.
            TEXTURE2D(_SwShadowMap); SAMPLER(sampler_SwShadowMap);
            float4x4 _SwShadowVP;     // world to (u, v, distance), each 0..1
            float4 _SwShadowTexel;    // xy: one texel; z: bias; w: sharpness
            float _SwShadowOn;        // 0 none … 1 full

            half SunLit(float2 uv, float z)
            {
                return (half)saturate((SAMPLE_TEXTURE2D(_SwShadowMap, sampler_SwShadowMap, uv).r - z) * _SwShadowTexel.w);
            }

            // 1 in the sun, 0 in the shadow of something: four reads, soft at the edge.
            half SunShadow(float3 lp)
            {
                if (_SwShadowOn <= 0.0) return 1.0h;
                float2 uv = lp.xy;
                float edge = saturate((0.5 - max(abs(uv.x - 0.5), abs(uv.y - 0.5))) * 10.0);
                if (edge <= 0.0) return 1.0h;
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
            #endif
                float z = lp.z - _SwShadowTexel.z;
                // The four texel centres round the point, blended by where it lies between them: an edge is a
                // smooth ramp one texel wide, not steps.
                float2 t = uv / _SwShadowTexel.xy - 0.5;
                float2 f = frac(t);
                float2 c = (floor(t) + 0.5) * _SwShadowTexel.xy;
                float2 o = _SwShadowTexel.xy;
                half l00 = SunLit(c, z), l10 = SunLit(c + float2(o.x, 0.0), z);
                half l01 = SunLit(c + float2(0.0, o.y), z), l11 = SunLit(c + o, z);
                half lit = lerp(lerp(l00, l10, (half)f.x), lerp(l01, l11, (half)f.x), (half)f.y);
                return lerp(1.0h, lit, (half)(edge * _SwShadowOn));
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 n : TEXCOORD1;
                half3 albedo : TEXCOORD2;
                half emissive : TEXCOORD3;
                float3 lp : TEXCOORD4;   // where this point is in the sun's shadow texture (u, v, distance)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.wp = wp;
                OUT.n = TransformObjectToWorldNormal(IN.normalOS);
                OUT.albedo = SwToLinear(IN.color.rgb) * _Tint.rgb;
                OUT.emissive = IN.uv.x;
                OUT.lp = mul(_SwShadowVP, float4(wp, 1.0)).xyz;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.wp;
                float dist = distance(wp, _WorldSpaceCameraPos.xyz);
                float near = saturate(1.0 - dist / 70.0);
                float2 uvA = wp.xz / _Repeat;
                // A second, rotated, larger sample hides the repeat.
                float2 uvB = float2(wp.x * 0.8 - wp.z * 0.6, wp.x * 0.6 + wp.z * 0.8) / (_Repeat * 3.7) + 0.37;
                half3 a = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uvA).rgb;
                half3 b = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uvB).rgb;
                // The photo's detail, as light and dark around its own average; its brightest blades and stones held in.
                half3 photo = clamp(lerp(a, b, 0.35h) / max(_Mean.rgb, 0.02h), 0.55h, 1.4h);
                half k = (half)(_Detail * lerp(0.35, 1.0, near));
                half3 albedo = IN.albedo * lerp(half3(1, 1, 1), photo, k);

                float3 n = normalize(IN.n);
                half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uvA));
                // Planar from above: tangent along +X, bitangent along +Z.
                n = normalize(n + float3(tn.x, 0.0, tn.y) * _Bump * near);

                float ndl = saturate(dot(n, _SwSunDir.xyz));
                half lit = SunShadow(IN.lp);
                // In the shade of something, a quarter of the sky is hidden too: shade reads as shade.
                half3 light = _SwAmbient.rgb * lerp(0.74h, 1.0h, lit) + _SwSunColor.rgb * (saturate(ndl * 1.45) * lit);
                // Sun and sky together never light the ground past 1.05 times its colour: a pale meadow at midday
                // keeps its green, and shade reads as shade. Nights and dusks (dimmer than that) are untouched.
                light *= 1.05h / max(1.05h, dot(_SwAmbient.rgb + _SwSunColor.rgb, half3(0.3h, 0.59h, 0.11h)));
                if (_SwFirePos.w > 0.0)
                {
                    float3 d = wp - _SwFirePos.xyz;
                    float att = 1.0 / (1.0 + dot(d, d) / _SwFirePos.w);
                    light += _SwFireColor.rgb * (att * (0.55 + 0.45 * saturate(dot(n, -normalize(d + 1e-4)))));
                }
                half3 c = albedo * light + albedo * (IN.emissive * _EmissiveBoost);
                c = SwTone(c * _SwExposure);
                half fog = SwFog(wp);
                c = lerp(c, _SwFogColor.rgb, max(fog, (half)_SwVeil));
                return half4(c, 1.0h);
            }
            ENDHLSL
        }
    }
}

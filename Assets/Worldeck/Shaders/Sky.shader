// Worldeck — the sky: a gradient from horizon to top, the ground colour below the horizon, a sun (or a
// moon) with a soft glow, and twinkling stars at night. Drawn on an inside-out sphere around the player,
// behind everything (ZWrite off; real walls still mask it until they dissolve). Writes alpha 1.
Shader "Worldeck/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.04,0.06,0.15,1)
        _Horizon ("Horizon", Color) = (0.16,0.23,0.4,1)
        _Below ("Below horizon", Color) = (0.1,0.12,0.12,1)
        _SunColor ("Sun disc colour", Color) = (1,0.9,0.7,1)
        _SunSize ("Sun size (cos)", Range(0.99, 0.99999)) = 0.9993
        _Moon ("Moon (0 sun, 1 moon)", Range(0,1)) = 0
        _Stars ("Stars", Range(0,1)) = 0
        _HorizonPow ("Gradient curve", Range(0.1, 2)) = 0.45
        _Photo ("Photographed sky (equirect)", 2D) = "black" {}
        _Photo2 ("Previous photo (crossfade)", 2D) = "black" {}
        _PhotoAmount ("Photo amount", Range(0,1)) = 0
        _PhotoMix ("Previous to current", Range(0,1)) = 1
        _PhotoYaw ("Photo yaw (rad)", Float) = 0
        _PhotoYaw2 ("Previous photo yaw (rad)", Float) = 0
        _PhotoTint ("Photo tint (weather)", Color) = (1,1,1,1)
        _PhotoSat ("Photo saturation", Range(0,1.5)) = 1
        _PhotoFloor ("Photo fades out below this height (sin of elevation)", Range(0,0.6)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+500" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Front
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Horizon, _Below, _SunColor;
                float _SunSize, _Moon, _Stars, _HorizonPow;
                float _PhotoAmount, _PhotoMix, _PhotoYaw, _PhotoYaw2, _PhotoSat, _PhotoFloor;
                half4 _PhotoTint;
            CBUFFER_END
            TEXTURE2D(_Photo); SAMPLER(sampler_Photo);
            TEXTURE2D(_Photo2); SAMPLER(sampler_Photo2);

            // Equirectangular lookup: u = 0.5 looks along +Z and grows towards +X; v = 1 is straight up.
            // Always mip 0, so the wrap seam never picks a blurry mip.
            float2 Equirect(float3 d, float yaw)
            {
                float phi = atan2(d.x, d.z) - yaw;
                return float2(frac(phi * 0.15915494 + 0.5), 0.5 + asin(clamp(d.y, -1.0, 1.0)) * 0.31830989);
            }

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.dir = wp - GetObjectToWorldMatrix()._m03_m13_m23;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                half3 c = lerp(_Horizon.rgb, _Top.rgb, pow(saturate(d.y), _HorizonPow));
                c = lerp(c, _Below.rgb, saturate(-d.y * 6.0));

                float s = dot(d, _SwSunDir.xyz);
                float disc = smoothstep(_SunSize, _SunSize + 0.0002, s);
                // A crescent: the disc minus a slightly offset disc.
                float3 off = normalize(_SwSunDir.xyz + float3(0.012, 0.008, 0.0));
                float bite = smoothstep(_SunSize, _SunSize + 0.0002, dot(d, off));
                disc *= lerp(1.0, 1.0 - bite * 0.92, _Moon);
                float glow = 0.25 * pow(saturate(s), 64.0) + 0.12 * pow(saturate(s), 8.0);
                c += _SunColor.rgb * (disc + glow * (1.0 - 0.7 * _Moon));

                // A photographed sky (it has its own sun or moon and stars): crossfades from the previous one.
                if (_PhotoAmount > 0.001)
                {
                    half3 now = SAMPLE_TEXTURE2D_LOD(_Photo, sampler_Photo, Equirect(d, _PhotoYaw), 0).rgb;
                    half3 was = SAMPLE_TEXTURE2D_LOD(_Photo2, sampler_Photo2, Equirect(d, _PhotoYaw2), 0).rgb;
                    half3 ph = lerp(was, now, _PhotoMix);
                    half grey = dot(ph, half3(0.2126h, 0.7152h, 0.0722h));
                    ph = lerp(grey.xxx, ph, _PhotoSat) * _PhotoTint.rgb;
                    // Skies with real trees or hills low down: the photo fades out there, the painted sky shows.
                    half floorK = _PhotoFloor > 0.001 ? smoothstep(_PhotoFloor * 0.62, _PhotoFloor, d.y) : 1.0h;
                    c = lerp(c, ph, _PhotoAmount * floorK);
                }

                if (_Stars * (1.0 - _PhotoAmount) > 0.001 && d.y > 0.0)
                {
                    float3 cell = floor(d * 180.0);
                    float h = Hash(cell);
                    if (h > 0.985)
                    {
                        float3 centre = (cell + 0.5) / 180.0;
                        float k = 1.0 - saturate(length(d - normalize(centre)) * 260.0);
                        float tw = 0.7 + 0.3 * sin(_Time.y * (1.0 + h * 3.0) + h * 40.0);
                        c += k * k * tw * _Stars * (1.0 - _PhotoAmount) * saturate(d.y * 6.0) * (0.6 + 0.8 * frac(h * 91.0));
                    }
                }
                c = lerp(c * _SwExposure, _SwFogColor.rgb, _SwVeil);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}

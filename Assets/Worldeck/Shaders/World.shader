// Worldeck — the one shader for everything solid in a world (terrain, props, furniture skins, builds).
// Flat-shaded "diorama" look: per-face normals, lit per vertex in three tones from one sun, plus ambient,
// plus a flickering fire light, plus emissive (uv0.x); fog in the horizon colour; wind sway on uv0.y;
// an animal's legs step as it walks (uv1 and _Gait, from Gait.cs).
// Vertex colours are authored in sRGB and converted here. Opaque: always writes alpha 1 (passthrough rule).
// Single-Pass Instanced (multiview) safe, SRP Batcher compatible.
Shader "Worldeck/World"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _EmissiveBoost ("Emissive boost", Float) = 1.6
        _Grow ("Grow (0 hidden, 1 shown)", Range(0,1)) = 1
        _Gait ("Walk: facing xz, stride 0-1, foot swing (m)", Vector) = (0,0,0,0)
        _FogScale ("Fog taken (1 all; less for a far landmark)", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "World"
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
                half4 _Tint;
                float _EmissiveBoost;
                float _Grow;
                float _FogScale;
                float4 _Gait;   // xy: the way it faces (world xz); z: where in its stride, 0..1; w: how far a foot swings, metres (0: standing)
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 leg : TEXCOORD1;   // x: how much of a leg this corner is (1 at the foot); y: which pair it steps with (Gait.Rig)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half fog : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                wp = SwWind(wp, IN.uv.y);
                // Walking: each leg swings forward off the ground and back along it, the pairs half a stride apart.
                if (_Gait.w > 0.0 && IN.leg.x > 0.0)
                {
                    float a = (_Gait.z + IN.leg.y) * 6.2831853;
                    float reach = _Gait.w * IN.leg.x;
                    wp.xz += _Gait.xy * (sin(a) * reach);
                    wp.y += max(0.0, cos(a)) * reach * 0.5;
                }
                float3 n = normalize(TransformObjectToWorldNormal(IN.normalOS));
                half3 albedo = SwToLinear(IN.color.rgb) * _Tint.rgb;
                half3 lit = albedo * SwLight(wp, n);
                // Soft sky occlusion: faces turned down or sideways get less of the sky's light.
                lit *= lerp(0.58h, 1.0h, saturate(n.y * 0.5 + 0.5));
                // A little rim of sky colour on faces turned away from the sun, so shapes separate from each other.
                lit += albedo * _SwAmbient.rgb * (0.25h * saturate(-dot(n, _SwSunDir.xyz)));
                lit += albedo * (IN.uv.x * _EmissiveBoost);
                OUT.color = SwTone(lit * _SwExposure);
                OUT.fog = SwFog(wp) * (1.0 - 0.6 * IN.uv.x) * _FogScale;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 c = lerp(IN.color, _SwFogColor.rgb, max(IN.fog, _SwVeil));
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}

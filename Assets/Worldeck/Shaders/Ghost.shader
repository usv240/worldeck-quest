// Worldeck — the coach's see-through hand (and ghost copies of things it carries). A depth-only pass first,
// so only the nearest surface shows (no doubled insides), then soft light that is brightest at the rim:
// it reads as a glowing outline of a hand over your real room or the world. Additive, so the colour adds
// light and the alpha adds a little coverage (passthrough rule, as Glow.shader).
Shader "Worldeck/Ghost"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 0.86, 0.68, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Fill ("Fill", Range(0, 1)) = 0.14
        _RimPower ("Rim power", Float) = 2.2
        _AlphaFromColor ("Alpha coverage", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Alpha, _Fill, _RimPower, _AlphaFromColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend One One, One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Alpha, _Fill, _RimPower, _AlphaFromColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half rim : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float3 v = GetWorldSpaceNormalizeViewDir(wp);
                OUT.rim = (half)pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                OUT.color = IN.color;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half k = lerp((half)_Fill, 1.0h, IN.rim);
                half3 c = _Tint.rgb * IN.color.rgb * IN.color.a * k * _Alpha;
                half a = saturate(max(c.r, max(c.g, c.b)) * _AlphaFromColor);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}

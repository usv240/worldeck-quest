// Worldeck — soft additive light: halos (fire glow, the guide, items that want picking up), particles
// (flames, embers, fireflies, bubbles, rain, snow), aurora ribbons. Vertex colour × tint, a radial
// falloff from uv (0.5, 0.5) when _Radial is 1. Premultiplied so passthrough never leaks through it:
// colour adds, alpha adds a little coverage.
Shader "Worldeck/Glow"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Radial ("Radial falloff", Range(0,1)) = 1
        _Intensity ("Intensity", Float) = 1
        _AlphaFromColor ("Alpha coverage", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One, One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Radial, _Intensity, _AlphaFromColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(wp);
                OUT.color = half4(SwToLinear(IN.color.rgb), IN.color.a) * _Tint;
                OUT.uv = IN.uv;
                OUT.fog = SwFog(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float r = length(IN.uv - 0.5) * 2.0;
                half k = lerp(1.0h, saturate(1.0 - r) * saturate(1.0 - r), _Radial);
                half3 c = IN.color.rgb * IN.color.a * k * _Intensity * (1.0 - IN.fog) * (1.0 - _SwVeil * 0.85);
                half a = saturate(max(c.r, max(c.g, c.b)) * _AlphaFromColor);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}

// Worldeck — soft translucent rounded panel behind text, so words stay readable over any room.
// _Size (metres) and _Radius come per renderer (TextPanel sets them), so corners stay round at any size.
Shader "Worldeck/RoundedPanel"
{
    Properties
    {
        _Color ("Colour", Color) = (0.05, 0.06, 0.11, 0.78)
        _EdgeColor ("Edge light", Color) = (0.6, 0.78, 1, 0.18)
        _Size ("Size (m)", Vector) = (0.2, 0.05, 0, 0)
        _Radius ("Corner radius (m)", Float) = 0.012
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }
        Pass
        {
            Name "Forward"
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON UNITY_SINGLE_PASS_STEREO
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EdgeColor;
                float4 _Size;
                float  _Radius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 size = max(_Size.xy, 1e-4);
                float r = min(_Radius, 0.5 * min(size.x, size.y));
                float2 p = (IN.uv - 0.5) * size;
                float2 q = abs(p) - (0.5 * size - r);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                float aa = max(fwidth(sd), 1e-5);
                float inside = 1.0 - smoothstep(-aa, aa, sd);
                // A hairline of light along the edge, like frosted glass catching the room.
                float edge = (1.0 - smoothstep(0.0, 2.5 * aa + 0.0015, abs(sd))) * _EdgeColor.a;
                float3 col = lerp(_Color.rgb, _EdgeColor.rgb, edge);
                return half4(col, max(_Color.a, edge) * inside);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

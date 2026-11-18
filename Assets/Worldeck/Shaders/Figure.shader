// Worldeck — Mara on your table: a real, coloured figure (her model's colour map), softly lit from above, with
// a warm rim of light from her beacon so she belongs to Ember's world. A depth-only pass first so only her front
// surface shows when she fades (no doubled insides), then the colour, blended so a weak signal makes her
// see-through and a lost one empties her out.
Shader "Worldeck/Figure"
{
    Properties
    {
        _MainTex ("Colour map", 2D) = "white" {}
        _Tint ("Rim tint", Color) = (1, 0.74, 0.42, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Rim ("Rim strength", Range(0, 2)) = 0.7
        _RimPower ("Rim power", Float) = 2.6
        _Light ("Light", Range(0, 2)) = 1.05
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+12" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FigureDepth"
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
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
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
            Name "Figure"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _Alpha, _Rim, _RimPower, _Light;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half light : TEXCOORD1;
                half rim : TEXCOORD2;
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
                // Soft light from above and in front, never black underneath.
                OUT.light = (half)(0.55 + 0.45 * saturate(dot(n, normalize(float3(0.3, 1.0, 0.4)))));
                OUT.rim = (half)pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                half3 c = albedo * IN.light * _Light + _Tint.rgb * IN.rim * _Rim;
                return half4(c, saturate(_Alpha));
            }
            ENDHLSL
        }
    }
}

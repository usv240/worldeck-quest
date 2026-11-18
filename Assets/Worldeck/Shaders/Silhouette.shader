// Worldeck — far horizon layers (mountains, dunes, skyline, hills): one flat tone per layer, lit only by
// ambient and a little sun, mixed towards the live fog colour by a per-layer amount stored in uv0.x, so the
// distance keeps matching the sky as the time of day changes. Emissive windows via uv0.y. Writes alpha 1.
Shader "Worldeck/Silhouette"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

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
                half3 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                half3 albedo = SwToLinear(IN.color.rgb);
                half3 lit = albedo * (_SwAmbient.rgb + _SwSunColor.rgb * 0.45h) + albedo * IN.uv.y * 1.5h;
                OUT.color = lerp(lit, _SwFogColor.rgb, saturate(IN.uv.x) * (1.0h - IN.uv.y)) * _SwExposure;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return half4(lerp(IN.color, _SwFogColor.rgb, _SwVeil), 1.0); }
            ENDHLSL
        }
    }
}

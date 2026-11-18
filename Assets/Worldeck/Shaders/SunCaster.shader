// Worldeck — what stands between the sun and the ground. SunShadows.cs draws every solid thing in the world with
// this, seen from the sun, into one texture: each texel keeps the distance (0 nearest the sun … 1 farthest) of the
// nearest thing along that ray (BlendOp Min; the texture is cleared to 1). The ground shader compares its own
// distance with it. No engine matrices are used: the object and light matrices are set by the script per draw.
Shader "Worldeck/SunCaster"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SunCaster"
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Min
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4x4 _SwCasterM;    // object to world
            float4x4 _SwCasterVP;   // world to (u, v, distance), each 0..1

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float depth : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = mul(_SwCasterM, float4(IN.positionOS.xyz, 1.0)).xyz;
                float3 lp = mul(_SwCasterVP, float4(wp, 1.0)).xyz;
                OUT.positionCS = float4(lp.xy * 2.0 - 1.0, 0.5, 1.0);
                OUT.depth = lp.z;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float d = saturate(IN.depth);
                return float4(d, d, d, d);
            }
            ENDHLSL
        }
    }
}

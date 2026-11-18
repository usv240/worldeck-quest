// Worldeck — real-looking fire: tongues of flame from layered noise rising and licking, coloured by a black-body
// ramp (white-hot core, yellow, orange, deep red at the edges), on a few camera-facing quads. Additive light;
// the frame's alpha is left as it is (the world behind is already solid). _Strength 0 = embers … 1 = roaring.
Shader "Worldeck/Fire"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1.6)) = 1
        _Seed ("Seed", Float) = 0
        _Intensity ("Intensity", Float) = 1.6
        _Speed ("Rise speed", Float) = 1.7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Fire"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One, Zero One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Seed, _Intensity, _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // Black-body-ish: 0 nothing, deep red, orange, yellow, 1 white-hot.
            half3 Ramp(half h)
            {
                half3 c = lerp(half3(0.0h, 0.0h, 0.0h), half3(0.55h, 0.06h, 0.01h), saturate(h * 4.0h));
                c = lerp(c, half3(1.0h, 0.36h, 0.05h), saturate(h * 4.0h - 1.0h));
                c = lerp(c, half3(1.0h, 0.72h, 0.25h), saturate(h * 4.0h - 2.0h));
                c = lerp(c, half3(1.0h, 0.95h, 0.78h), saturate(h * 4.0h - 3.0h));
                return c;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.fog = SwFog(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y * _Speed;
                float s = saturate(_Strength);
                // Big slow sway and fast small licks, both rising.
                float n1 = Fbm(float2(uv.x * 2.6 + _Seed, uv.y * 1.8 - t * 0.9));
                float n2 = Fbm(float2(uv.x * 5.5 - _Seed * 1.7, uv.y * 3.6 - t * 1.6));
                float x = (uv.x - 0.5) * 2.0;
                x += (n1 - 0.5) * 0.9 * uv.y;
                // The body: wide at the base, a tongue towards the top; taller when stronger.
                float top = lerp(0.45, 1.0, s);
                float h = uv.y / top;
                float width = (1.0 - h) * 0.85 + 0.06;
                float body = saturate(1.0 - abs(x) / max(width, 1e-3));
                body *= smoothstep(0.0, 0.07, uv.y);
                float heat = body * saturate(1.12 - h);
                heat *= 0.55 + 0.9 * n2;
                heat = saturate(heat * 1.7 - 0.28);
                half3 c = Ramp((half)heat) * (half)(heat * _Intensity * (0.6 + 0.6 * s));
                c *= (1.0h - (half)IN.fog) * (1.0h - (half)_SwVeil * 0.85h);
                return half4(c, 0.0h);
            }
            ENDHLSL
        }
    }
}

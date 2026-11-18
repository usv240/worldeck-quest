// Worldeck — the real room's walls, floor and ceiling. Drawn before the world, it writes depth and a
// fully transparent pixel, so the passthrough room shows and the world behind it is hidden. When a world
// opens, a dissolve front spreads from the reveal point: inside the front the pixel is clipped (the world
// shows), and a thin warm edge burns along it. Reversing the radius brings the room back.
// Underlay compositing: out = eye.rgb + (1 - eye.a) * passthrough, so (0,0,0,0) = the real room.
Shader "Worldeck/RoomCut"
{
    Properties
    {
        _EdgeColor ("Edge colour", Color) = (1, 0.62, 0.25, 1)
        _Hold ("Hold (ignore the dissolve)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-1" }
        Pass
        {
            Name "RoomCut"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldeckCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                float _Hold;
            CBUFFER_END

            float _SwRoomGlow;        // 0..1: the world's fire light falling on your real room while it opens

            float4 _SwRevealOrigin;   // xyz: where the dissolve starts
            float _SwRevealRadius;    // metres the front has travelled (< 0: nothing dissolved)
            float _SwRevealEdge;      // width of the burning edge, metres

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 wp : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.wp);
                return OUT;
            }

            float Noise(float3 p)
            {
                // Dotted cells (a nod to static): each 4 cm cell dissolves at its own moment.
                float3 c = floor(p * 25.0);
                return frac(sin(dot(c, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            // The fire's light added onto the real room (premultiplied: rgb adds, alpha 0 keeps the room).
            half3 RoomGlow(float3 wp)
            {
                if (_SwRoomGlow <= 0.0 || _SwFirePos.w <= 0.0) return 0;
                float3 n = normalize(cross(ddy(wp), ddx(wp)));
                n *= sign(dot(n, _WorldSpaceCameraPos.xyz - wp));
                float3 L = _SwFirePos.xyz - wp;
                float d2 = max(dot(L, L), 1e-4);
                half att = 1.0 / (1.0 + d2 / _SwFirePos.w);
                half ndl = 0.3 + 0.7 * saturate(dot(n, L * rsqrt(d2)));
                return _SwFireColor.rgb * att * ndl * _SwRoomGlow * 0.6;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_Hold > 0.5) return half4(RoomGlow(IN.wp), 0);   // furniture waiting for its skin
                float d = distance(IN.wp, _SwRevealOrigin.xyz);
                float n = Noise(IN.wp);
                float front = _SwRevealRadius - d - n * 0.45;
                clip(-front);                                   // dissolved: let the world through
                float edge = saturate(1.0 + front / max(_SwRevealEdge, 1e-3));
                if (_SwRevealRadius > 0.0 && edge > 0.0)
                    return half4(_EdgeColor.rgb * edge, edge);  // the burning rim (premultiplied)
                return half4(RoomGlow(IN.wp), 0);                // the real room (lit by the fire as it opens)
            }
            ENDHLSL
        }
    }
}

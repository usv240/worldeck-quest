// Worldeck — globals shared by every world shader, set each frame by WorldLook.cs.
#ifndef WORLDECK_COMMON_INCLUDED
#define WORLDECK_COMMON_INCLUDED

float4 _SwSunDir;        // xyz: direction towards the sun (or moon), normalised
half4 _SwSunColor;       // rgb: sun light colour × intensity (linear)
half4 _SwAmbient;        // rgb: ambient (linear)
half4 _SwFogColor;       // rgb: fog = horizon colour (linear)
float _SwFogDensity;     // exp² fog: 1 - 2^-(d·density)²
float _SwExposure;       // the recipe's light knob
float4 _SwWindDir;       // xy: world xz wind direction; z: strength 0–1; w: amplitude metres
float4 _SwFirePos;       // xyz: fire position; w: falloff radius² (0 = no fire)
half4 _SwFireColor;      // rgb: fire colour × flicker (linear)
float _SwVeil;           // 0 clear … 1 everything is fog (one world turning into the next)

half3 SwToLinear(half3 srgb)
{
    // Close to the sRGB curve, cheap enough per vertex.
    return srgb * (srgb * (srgb * 0.305306011h + 0.682171111h) + 0.012522878h);
}

float3 SwWind(float3 wp, float weight)
{
    if (weight <= 0.0) return wp;
    float p = dot(wp.xz, float2(0.13, 0.07));
    float t = _Time.y;
    float s = sin(t * 1.3 + p) * 0.6 + sin(t * 2.7 + p * 1.7) * 0.25;
    wp.xz += _SwWindDir.xy * (s * _SwWindDir.z * _SwWindDir.w * weight * weight);
    return wp;
}

// Three tones from the sun (art direction rule 2), ambient everywhere, the fire close by.
half3 SwLight(float3 wp, float3 n)
{
    float ndl = saturate(dot(n, _SwSunDir.xyz));
    half tone = ndl > 0.62 ? 1.0h : (ndl > 0.22 ? 0.72h : 0.48h);
    half3 light = _SwAmbient.rgb + _SwSunColor.rgb * tone;
    if (_SwFirePos.w > 0.0)
    {
        float3 d = wp - _SwFirePos.xyz;
        float att = 1.0 / (1.0 + dot(d, d) / _SwFirePos.w);
        float facing = 0.55 + 0.45 * saturate(dot(n, -normalize(d + 1e-4)));
        light += _SwFireColor.rgb * (att * facing);
    }
    return light;
}

// Highlights roll off towards white instead of clipping (the display is not HDR): a sunlit pale meadow keeps its
// green. Unchanged below 0.75; 1.0 comes out 0.875; never above 1.
half3 SwTone(half3 c)
{
    half3 over = max(c - 0.75h, 0.0h);
    return min(c, 0.75h) + over / (1.0h + over * 4.0h);
}

half SwFog(float3 wp)
{
    float d = distance(wp, _WorldSpaceCameraPos.xyz) * _SwFogDensity;
    return saturate(1.0 - exp2(-d * d));
}

#endif

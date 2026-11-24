#ifndef DUSTBORN_POI_SURFACE_WEATHERING_INCLUDED
#define DUSTBORN_POI_SURFACE_WEATHERING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

float PoiWeatherHash(float3 cell)
{
    float3 p = frac(cell * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float PoiWeatherNoise(float3 position)
{
    float3 cell = floor(position);
    float3 blend = frac(position);
    blend = blend * blend * (3.0 - 2.0 * blend);

    float nearLow = lerp(PoiWeatherHash(cell), PoiWeatherHash(cell + float3(1, 0, 0)), blend.x);
    float nearHigh = lerp(PoiWeatherHash(cell + float3(0, 1, 0)), PoiWeatherHash(cell + float3(1, 1, 0)), blend.x);
    float farLow = lerp(PoiWeatherHash(cell + float3(0, 0, 1)), PoiWeatherHash(cell + float3(1, 0, 1)), blend.x);
    float farHigh = lerp(PoiWeatherHash(cell + float3(0, 1, 1)), PoiWeatherHash(cell + float3(1, 1, 1)), blend.x);
    return lerp(lerp(nearLow, nearHigh, blend.y), lerp(farLow, farHigh, blend.y), blend.z);
}

void ApplyPoiWeathering(float3 positionWS, float patchSize, half variation, half dirtAmount, half3 dirtTint,
    inout half3 albedo, inout half smoothness)
{
    if (variation <= 0.0 && dirtAmount <= 0.0)
        return;

    float3 p = positionWS / max(patchSize, 0.25) + float3(11.73, 43.12, 27.61);
    float broad = PoiWeatherNoise(p);
    half tone = 1.0 + (broad * 2.0 - 1.0) * variation;
    half dirt = 0.0;

    if (dirtAmount > 0.0)
    {
        float fine = PoiWeatherNoise(p * 2.17 + float3(4.91, 8.28, 1.39));
        dirt = smoothstep(0.42, 0.79, broad * 0.72 + fine * 0.28) * dirtAmount;
    }

    albedo *= tone * lerp(half3(1.0, 1.0, 1.0), dirtTint, dirt);
    smoothness *= 1.0 - 0.4 * dirt;
}

void ApplyPoiPaintWear(float3 positionWS, half wear, half3 exposedWood, inout half3 albedo, inout half smoothness)
{
    if (wear <= 0.0)
        return;

    float3 position = positionWS * float3(6.6, 10.7, 6.6) + float3(27.59, 13.17, 46.39);
    float3 warped = position + 0.29 * sin(position.zxy * 1.71 + position.yzx * 0.86);
    float patches = PoiWeatherNoise(warped);
    float flecks = PoiWeatherNoise(warped * 2.63 + float3(8.2, 3.6, 14.1));
    float film = patches * 0.63 + flecks * 0.37;
    float threshold = lerp(0.92, 0.53, wear);
    half strength = saturate(wear * 4.0);
    half bare = smoothstep(threshold - 0.046, threshold + 0.046, film) * strength;
    half rim = smoothstep(threshold - 0.082, threshold + 0.006, film) * strength - bare;
    half grain = 0.45 + 0.88 * sqrt(max(0.0, Luminance(albedo)));
    albedo = lerp(albedo * (1.0 - 0.08 * rim), exposedWood * grain, bare * 0.74);
    smoothness *= 1.0 - 0.33 * bare;
}

#endif

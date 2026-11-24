#ifndef DUSTBORN_POI_SURFACE_INPUT_INCLUDED
#define DUSTBORN_POI_SURFACE_INPUT_INCLUDED

#define _NORMALMAP 1
#define _Surface 0.0
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "PoiSurfaceWeathering.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _DirtTint;
    half4 _ExposedWood;
    float _PatchSize;
    half _BumpScale;
    half _Smoothness;
    half _Metallic;
    half _ToneVariation;
    half _DirtAmount;
    half _PaintedWood;
    half _PaintWear;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
#endif

void SamplePoiFinish(float2 uv, float3 positionWS, out half3 albedo, out half smoothness)
{
    albedo = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).rgb * _BaseColor.rgb;
    smoothness = _Smoothness;
    float3 worldPosition = GetAbsolutePositionWS(positionWS);

#if defined(_POI_PAINTED_WOOD)
    ApplyPoiPaintWear(worldPosition, _PaintWear, _ExposedWood.rgb, albedo, smoothness);
#endif

    ApplyPoiWeathering(worldPosition, _PatchSize, _ToneVariation, _DirtAmount, _DirtTint.rgb, albedo, smoothness);
}

void InitializePoiSurfaceData(float2 uv, float3 positionWS, out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;
    SamplePoiFinish(uv, positionWS, surfaceData.albedo, surfaceData.smoothness);
    surfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    surfaceData.metallic = _Metallic;
    surfaceData.occlusion = 1.0;
    surfaceData.alpha = 1.0;
}

#endif

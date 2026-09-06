#ifndef REPETITIONLESSINPUT_INCLUDED
#define REPETITIONLESSINPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Material Properties
CBUFFER_START(UnityPerMaterial)
    float _SurfaceTypeSetting;
    float _UVSpace;
    float _VertexColourBlendMode;
    half  _DebuggingIndex;
    float _LayersCount;
    float4 _NoiseTexture_TexelSize;
CBUFFER_END

// Textures
TEXTURE2D(_NoiseTexture);            SAMPLER(sampler_NoiseTexture);
TEXTURE2D(_PropertiesTexture);       SAMPLER(sampler_PropertiesTexture);
TEXTURE2D(_AssignedTexturesTexture); SAMPLER(sampler_AssignedTexturesTexture);
TEXTURE2D_ARRAY(_AVTextures);        SAMPLER(sampler_AVTextures);
TEXTURE2D_ARRAY(_NSOTextures);       SAMPLER(sampler_NSOTextures);
TEXTURE2D_ARRAY(_EMTextures);        SAMPLER(sampler_EMTextures);
TEXTURE2D_ARRAY(_BMTextures);        SAMPLER(sampler_BMTextures);

#ifdef REPETITIONLESS_LAYERED
TEXTURE2D(_TerrainHolesTexture);     SAMPLER(sampler_TerrainHolesTexture);
TEXTURE2D(_Control0);                SAMPLER(sampler_Control0);
TEXTURE2D(_Control1);                SAMPLER(sampler_Control1);
TEXTURE2D(_Control2);                SAMPLER(sampler_Control2);
TEXTURE2D(_Control3);                SAMPLER(sampler_Control3);
TEXTURE2D(_Control4);                SAMPLER(sampler_Control4);
TEXTURE2D(_Control5);                SAMPLER(sampler_Control5);
TEXTURE2D(_Control6);                SAMPLER(sampler_Control6);
TEXTURE2D(_Control7);                SAMPLER(sampler_Control7);

// Terrain
// https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl

CBUFFER_START(_Terrain)
    float4 _TerrainHeightmapScale;
#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapRecipSize;
#endif
#ifdef SCENESELECTIONPASS
    int _ObjectId;
    int _PassValue;
#endif
CBUFFER_END

#ifdef UNITY_INSTANCING_ENABLED
TEXTURE2D(_TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture); SAMPLER(sampler_TerrainNormalmapTexture);
#endif

UNITY_INSTANCING_BUFFER_START(Terrain)
UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // float4(xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

void ClipHoles(float2 uv)
{
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r;
    clip(hole < 0.0005f ? -1 : 1);
}

void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 patchVertex = positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

    float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z; // (xy + float2(xBase,yBase)) * skipScale
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

    positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    positionOS.y = height * _TerrainHeightmapScale.y;
    normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;

    uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}

void TerrainInstancing(inout float4 positionOS, inout float3 normal)
{
    float2 uv = { 0, 0 };
    TerrainInstancing(positionOS, normal, uv);
}

void TerrainInstancing(inout float4 positionOS)
{
    float3 normal = { 0, 0, 0 };
    TerrainInstancing(positionOS, normal);
}

#endif
#endif
#ifndef REPETITIONLESSHDRPSAMPLETERRAIN_INCLUDED
#define REPETITIONLESSHDRPSAMPLETERRAIN_INCLUDED

#if defined(_ALPHATEST_ON) && !defined(_HOLES_TEXTURE_DEF)
#define _HOLES_TEXTURE_DEF
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);
#endif

#include "../Common/Main/SampleRepetitionlessTerrain.hlsl"

void SampleRepetitionlessTerrain_float(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    float3 WorldPosition, float3 CameraPosition,
    int SurfaceType, int UVSpace, int VertexColourBlendModeIndex, int DebuggingIndex,
    float4 VertexColour,

    // Properties
    int LayersCount,
    UnityTexture2D PropertiesTexture,
    UnityTexture2D AssignedTexturesTexture,

    // Textures
    UnityTexture2DArray AVTextures,
    UnityTexture2DArray NSOTextures,
    UnityTexture2DArray EMTextures,
    UnityTexture2DArray BMTextures,

    UnityTexture2D NoiseTexture,

    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float MetallicOut,
    out float SmoothnessOut,
    out float OcclussionOut,
    out float3 EmissionColorOut
) {
    SampleRepetitionlessTerrain(
        SS, UV, WorldNormalVector,
        WorldPosition, CameraPosition,
        SurfaceType, UVSpace, VertexColourBlendModeIndex, DebuggingIndex,
        VertexColour,
        LayersCount,
        PropertiesTexture.tex,
        AssignedTexturesTexture.tex,
        AVTextures.tex,
        NSOTextures.tex,
        EMTextures.tex,
        BMTextures.tex,
        NoiseTexture.tex,
        AlbedoColorOut,
        NormalVectorOut,
        MetallicOut,
        SmoothnessOut,
        OcclussionOut,
        EmissionColorOut
    );
}

#endif
#ifndef SAMPLEREPETITIONLESSDYNAMIC_INCLUDED
#define SAMPLEREPETITIONLESSDYNAMIC_INCLUDED

#ifdef REPETITIONLESS_LAYERED
#include "SampleRepetitionlessTerrain.hlsl"
#else
#include "SampleRepetitionlessLayer.hlsl"
#endif

#if defined(REPETITIONLESS_BIRP) || UNITY_VERSION < 600000
SamplerState sampler_TrilinearRepeat;
#endif

// Assumed its being used in a shader with the proper variable names
// Either samples the first layer or multiple depending on if REPETITIONLESS_LAYERED is set
void SampleRepetitionless(
    // Inputs
    float2 UV,
    float3 WorldNormalVector,
    float3 WorldPosition,
    float4 VertexColour,
    
    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
)
{
#ifdef REPETITIONLESS_LAYERED
    SampleRepetitionlessTerrain(
        sampler_TrilinearRepeat,
        UV,
        WorldNormalVector,
        WorldPosition,
        _WorldSpaceCameraPos,
        (int)_SurfaceTypeSetting,
        (int)_UVSpace,
        (int)_VertexColourBlendMode,
        (int)_DebuggingIndex,
        VertexColour,

        (int)_LayersCount,
        _PropertiesTexture,
        _AssignedTexturesTexture,

        _AVTextures,
        _NSOTextures,
        _EMTextures,
        _BMTextures,

        _NoiseTexture,

        AlbedoColorOut, NormalVectorOut, MetallicOut, SmoothnessOut, OcclussionOut, EmissionColorOut
    );
#else
    SampleRepetitionlessLayer(
        sampler_TrilinearRepeat,
        UV,
        WorldNormalVector,
        WorldPosition,
        _WorldSpaceCameraPos,
        (int)_SurfaceTypeSetting,
        (int)_UVSpace,
        (int)_VertexColourBlendMode,
        (int)_DebuggingIndex,
        VertexColour,

        0,
        _PropertiesTexture,
        _AssignedTexturesTexture,

        _AVTextures,
        _NSOTextures,
        _EMTextures,
        _BMTextures,

        _NoiseTexture,

        AlbedoColorOut, NormalVectorOut, MetallicOut, SmoothnessOut, OcclussionOut, EmissionColorOut
    );
#endif
}

#endif
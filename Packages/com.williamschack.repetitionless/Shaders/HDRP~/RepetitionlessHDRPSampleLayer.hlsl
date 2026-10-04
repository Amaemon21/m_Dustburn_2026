#ifndef REPETITIONLESSHDRPSAMPLELAYER_INCLUDED
#define REPETITIONLESSHDRPSAMPLELAYER_INCLUDED

#include "../Common/Main/SampleRepetitionlessLayer.hlsl"

void SampleRepetitionlessLayer_float(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    float3 WorldPosition, float3 CameraPosition,
    int SurfaceType, int UVSpace, int VertexColourBlendModeIndex, int DebuggingIndex,
    float4 VertexColour,

    // Properties
    int LayerIndex,
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
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
) {
    SampleRepetitionlessLayer(
        SS, UV, WorldNormalVector,
        WorldPosition, CameraPosition,
        SurfaceType, UVSpace, VertexColourBlendModeIndex, DebuggingIndex,
        VertexColour,
        LayerIndex,
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
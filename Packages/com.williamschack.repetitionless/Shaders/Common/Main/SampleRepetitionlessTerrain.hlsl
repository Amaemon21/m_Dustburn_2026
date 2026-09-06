#ifndef SAMPLEREPETITIONLESSTERRAIN_INCLUDED
#define SAMPLEREPETITIONLESSTERRAIN_INCLUDED

#include "SampleRepetitionlessLayer.hlsl"

// Assume control and terrain holes properties are:
// _TerrainHolesTexture
// _Control{Index}

#ifdef _MAX_LAYERS_4
#define MAX_LAYERS 4
#elif _MAX_LAYERS_8
#define MAX_LAYERS 8
#elif _MAX_LAYERS_12
#define MAX_LAYERS 12
#elif _MAX_LAYERS_16
#define MAX_LAYERS 16
#elif _MAX_LAYERS_20
#define MAX_LAYERS 20
#elif _MAX_LAYERS_24
#define MAX_LAYERS 24
#elif _MAX_LAYERS_28
#define MAX_LAYERS 28
#elif _MAX_LAYERS_32
#define MAX_LAYERS 32
#endif

// Shouldnt happen, but as a fallback incase
#ifndef MAX_LAYERS
#define MAX_LAYERS 4
#endif

#define R_SAMPLE_CONTROL(i, uv) (i * 4) < LayersCount ? SAMPLE_TEXTURE2D(_Control##i, sampler_Control0, uv) : 0

void SampleRepetitionlessTerrain(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    float3 WorldPosition, float3 CameraPosition,
    int SurfaceType, int UVSpace, int VertexColourBlendModeIndex, int DebuggingIndex,
    float4 VertexColour,

    // Properties
    int LayersCount,
    Texture2D PropertiesTexture,
    Texture2D AssignedTexturesTexture,

    // Textures
    Texture2DArray AVTextures,
    Texture2DArray NSOTextures,
    Texture2DArray EMTextures,
    Texture2DArray BMTextures,

    Texture2D NoiseTexture,

    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float MetallicOut,
    out float SmoothnessOut,
    out float OcclussionOut,
    out float3 EmissionColorOut
) {
    float4 albedoColor  = 1;
    float3 normalVector = WorldNormalVector;
    float  metallic     = 0;
    float  smoothness   = 0;
    float  occlussion   = 0;
    float3 emission     = 0;

    // Terrain Holes
#ifdef _ALPHATEST_ON
    float holeColour = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, UV).r;
#else
    float holeColour = 1;
#endif
    clip(albedoColor.a - (1 - (holeColour - 0.01)));

    if (holeColour == 0) {
        AlbedoColorOut   = albedoColor;
        NormalVectorOut  = normalVector;
        MetallicOut      = metallic;
        SmoothnessOut    = smoothness;
        OcclussionOut    = occlussion;
        EmissionColorOut = emission;
        return;
    }

    // Sample control textures
    half4 controlColours[8] = {
        R_SAMPLE_CONTROL(0, UV),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0),
        half4(0, 0, 0, 0)
    };

#if MAX_LAYERS > 4
    controlColours[1] = R_SAMPLE_CONTROL(1, UV);
#endif
#if MAX_LAYERS > 8
    controlColours[2] = R_SAMPLE_CONTROL(2, UV);
#endif
#if MAX_LAYERS > 12
    controlColours[3] = R_SAMPLE_CONTROL(3, UV);
#endif
#if MAX_LAYERS > 16
    controlColours[4] = R_SAMPLE_CONTROL(4, UV);
#endif
#if MAX_LAYERS > 20
    controlColours[5] = R_SAMPLE_CONTROL(5, UV);
#endif
#if MAX_LAYERS > 24
    controlColours[6] = R_SAMPLE_CONTROL(6, UV);
#endif
#if MAX_LAYERS > 28
    controlColours[7] = R_SAMPLE_CONTROL(7, UV);
#endif
    
    // Get individual weights and sum
    half controlWeights[32];
    half controlSum = 0;

    [unroll]
    for (int controlLayer = 0; controlLayer < 8; controlLayer++) {
        controlWeights[controlLayer * 4 + 0] = controlColours[controlLayer].x;
        controlWeights[controlLayer * 4 + 1] = controlColours[controlLayer].y;
        controlWeights[controlLayer * 4 + 2] = controlColours[controlLayer].z;
        controlWeights[controlLayer * 4 + 3] = controlColours[controlLayer].w;

        controlSum += dot(controlColours[controlLayer], 1.0);
    }

    half backgroundControl = saturate(1 - controlSum);

    // Normalize weights for additive blending
    if (controlSum > 1) {
        [unroll]
        for (int controlWeightIndex = 0; controlWeightIndex < 32; controlWeightIndex++) {
            controlWeights[controlWeightIndex] /= controlSum;
        }
    }

    // Read array assigned textures
    int assignedAVTextures[3];
    int assignedNSOTextures[3];
    int assignedEVTextures[3];
    int assignedBMTextures;

    GetArrayAssignedTextures(AssignedTexturesTexture, assignedAVTextures, assignedNSOTextures, assignedEVTextures, assignedBMTextures);

    // Variables
    albedoColor  = backgroundControl;
    normalVector = WorldNormalVector * backgroundControl;
    metallic     = 0;
    smoothness   = 0;
    occlussion   = backgroundControl;
    emission     = 0;

    // Sample Layers
    [loop]
    for (int i = 0; i < MAX_LAYERS; i++) {
        half layerControl = controlWeights[i];

        [branch]
        if (layerControl == 0)
            continue;

        float4 layerAlbedo    = albedoColor;
        float3 layerNormal    = normalVector;
        float layerMetallic   = metallic;
        float layerSmoothness = smoothness;
        float layerOcclussion = occlussion;
        float3 layerEmission  = emission;

        SampleRepetitionlessLayer(
            SS, UV, WorldNormalVector,
            WorldPosition, CameraPosition,
            SurfaceType, UVSpace, VertexColourBlendModeIndex, DebuggingIndex,
            VertexColour,
            i,
            PropertiesTexture,
            assignedAVTextures[0], assignedAVTextures[1], assignedAVTextures[2],
            assignedNSOTextures[0], assignedNSOTextures[1], assignedNSOTextures[2],
            assignedEVTextures[0], assignedEVTextures[1], assignedEVTextures[2],
            assignedBMTextures,
            AVTextures,
            NSOTextures,
            EMTextures,
            BMTextures,
            NoiseTexture,
            layerAlbedo, layerNormal, layerMetallic, layerSmoothness, layerOcclussion, layerEmission
        );

        albedoColor  += layerAlbedo     * layerControl;
        normalVector += layerNormal     * layerControl;
        metallic     += layerMetallic   * layerControl;
        smoothness   += layerSmoothness * layerControl;
        occlussion   += layerOcclussion * layerControl;
        emission     += layerEmission   * layerControl;
    }

    AlbedoColorOut   = albedoColor;
    NormalVectorOut  = normalize(normalVector);
    MetallicOut      = metallic;
    SmoothnessOut    = smoothness;
    OcclussionOut    = occlussion;
    EmissionColorOut = emission;
}

#endif
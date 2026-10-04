#ifndef SAMPLEREPETITIONLESSMATERIAL_INCLUDED
#define SAMPLEREPETITIONLESSMATERIAL_INCLUDED

#include "../Structs/RepetitionlessMaterialData.hlsl"

#include "../RepetitionlessHelpers/RepetitionlessNoise.hlsl"
#include "../RepetitionlessHelpers/RepetitionlessTextureUtilities.hlsl"
#include "../RepetitionlessHelpers/MacroMicroVariation.hlsl"
#include "../RepetitionlessHelpers/GetArrayAssignedTextures.hlsl"

#include "../Noise/Keijiro/SimplexNoise2D.hlsl"
#include "../Noise/Keijiro/ClassicNoise2D.hlsl"

#include "../Utilities/TextureUtilities.hlsl"

void SampleRepetitionlessMaterial(
    // General Settings
    SamplerState SS, float2 UV, float2 DdxUV, float2 DdyUV, float3 WorldNormalVector,
    int SurfaceType, int DebuggingIndex,

    // Textures
    int ArrayLayerIndex,
    Texture2DArray AVTextures,
    Texture2DArray NSOTextures,
    Texture2DArray EMTextures,
    int AssignedAVTextures[3],
    int AssignedNSOTextures[3],
    int AssignedEMTextures[3],

    Texture2D NoiseTexture,

    // Material Data
    in RepetitionlessMaterialData MaterialData,

    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
){
    // Default values
    AlbedoColorOut   = 1;
    NormalVectorOut  = WorldNormalVector;
    MetallicOut      = 0;
    SmoothnessOut    = 0;
    OcclussionOut    = 1;
    EmissionColorOut = 0;

    // Setup UVs
    float2 tiling = MaterialData.TilingOffset.xy;
    float2 offset = MaterialData.TilingOffset.zw;
    
    float2 oriUV = UV;
    UV = UV * tiling + offset;

    DdxUV *= tiling;
    DdyUV *= tiling;
    
    // Change UVs & Get Edge Mask
    float voronoiCells = 1;
    float edgeMask = 0;
    float2 edgeUV = UV;
    float2 edgeDdxUV = DdxUV;
    float2 edgeDdyUV = DdyUV;
    float2 transformedUV = UV;
    if (MaterialData.NoiseEnabled) {
#ifdef _REPETITIONLESS_NOISE_TEXTURE
        int noiseTextureResolution = _NoiseTexture_TexelSize.z; // width height are the same

        // Make the scale uniform across resolutions and similar to dynamic noise
        int textureNoiseScale = MaterialData.NoiseScale * (noiseTextureResolution / 1000);
        textureNoiseScale *= 16;

        GetRepetitionlessNoiseUVs(UV, DdxUV, DdyUV, textureNoiseScale, MaterialData.RandomiseNoiseScaling, MaterialData.NoiseScalingMinMax, MaterialData.RandomiseRotation, MaterialData.NoiseRandomiseRotationMinMax, NoiseTexture, noiseTextureResolution, voronoiCells, edgeMask, edgeUV, edgeDdxUV, edgeDdyUV, transformedUV);
#else
        GetRepetitionlessNoiseUVs(UV, DdxUV, DdyUV, MaterialData.NoiseAngleOffset, MaterialData.NoiseScale, MaterialData.RandomiseNoiseScaling, MaterialData.NoiseScalingMinMax, MaterialData.RandomiseRotation, MaterialData.NoiseRandomiseRotationMinMax, voronoiCells, edgeMask, edgeUV, edgeDdxUV, edgeDdyUV, transformedUV);
#endif
    }
    
    bool sampleEdges = edgeMask > 0;

    // Get Macro/Micro Variation Multiplier
#ifdef _REPETITIONLESS_VARIATION
    float variationColor = 0;
    [branch]
    if (MaterialData.VariationEnabled && MaterialData.VariationOpacity > 0) {
        switch (MaterialData.VariationMode) {
            case 0: // Perlin Noise
                variationColor = MacroMicroVariationPerlinNoise(MaterialData.VariationSmallScale, MaterialData.VariationMediumScale, MaterialData.VariationLargeScale, MaterialData.VariationBrightness, MaterialData.VariationNoiseStrength, oriUV, MaterialData.VariationTO.x, MaterialData.VariationTO.zw);
                break;
            case 1: // Simplex Noise
                variationColor = MacroMicroVariationSimplexNoise(MaterialData.VariationSmallScale, MaterialData.VariationMediumScale, MaterialData.VariationLargeScale, MaterialData.VariationBrightness, MaterialData.VariationNoiseStrength, oriUV, MaterialData.VariationTO.x, MaterialData.VariationTO.zw);
                break;
            case 2: // Custom Texture
                variationColor = MacroMicroVariationTextureArray(MaterialData.VariationSmallScale, MaterialData.VariationMediumScale, MaterialData.VariationLargeScale, MaterialData.VariationBrightness, AVTextures, AssignedAVTextures, ArrayLayerIndex, 3, SS, oriUV, MaterialData.VariationTO.xy, MaterialData.VariationTO.zw);
                break;
        }
    }
#endif

    // Debugging
    if (DebuggingIndex != -1) {
        switch (DebuggingIndex) {
            case 0: // Voronoi Cells
                AlbedoColorOut = voronoiCells;
                break;
            case 1: // Edge Mask
                AlbedoColorOut = edgeMask;
                break;
#ifdef _REPETITIONLESS_VARIATION
            case 4: // Variation Colour
                AlbedoColorOut = variationColor;
                break;
#endif
            default:
                AlbedoColorOut = 0;
                break;
        }
        
        return;
    }

    // Sample textures
    bool samplingAV  = MaterialData.AlbedoAssigned; // Variation is sampled later
    bool samplingNSO = MaterialData.NormalAssigned || MaterialData.SmoothnessAssigned || MaterialData.OcclussionAssigned;
    bool samplingEM  = MaterialData.EmissionAssigned || MaterialData.MetallicAssigned;

    float4 avTexture = 0;
    float4 nsoTexture = 0;
    float4 emTexture = 0;

    [branch]
    if (samplingAV) 
        avTexture  = SampleRepetitionlessArrayTexture(AVTextures,  AssignedAVTextures,  ArrayLayerIndex, SS, DdxUV, DdyUV, edgeDdxUV, edgeDdyUV, edgeMask, edgeUV, transformedUV, sampleEdges);

    [branch]
    if (samplingNSO)
        nsoTexture = SampleRepetitionlessArrayTexture(NSOTextures, AssignedNSOTextures, ArrayLayerIndex, SS, DdxUV, DdyUV, edgeDdxUV, edgeDdyUV, edgeMask, edgeUV, transformedUV, sampleEdges);

    [branch]
    if (samplingEM)
        emTexture  = SampleRepetitionlessArrayTexture(EMTextures,  AssignedEMTextures,  ArrayLayerIndex, SS, DdxUV, DdyUV, edgeDdxUV, edgeDdyUV, edgeMask, edgeUV, transformedUV, sampleEdges);

    // Albedo
    AlbedoColorOut = samplingAV ? float4(avTexture.rgb, 1) : 1;
    AlbedoColorOut *= float4(MaterialData.AlbedoTint, 1);

    // Doesnt do anything at the moment since alpha is forced to 1
    // Still here incase the alpha is readded
    if (SurfaceType == 1)
        clip(AlbedoColorOut.a - MaterialData.AlphaClipping);
    
#ifdef _REPETITIONLESS_VARIATION
    // Macro/Micro Variation
    if (MaterialData.VariationEnabled && MaterialData.VariationOpacity > 0)
        AlbedoColorOut = lerp(AlbedoColorOut, variationColor * AlbedoColorOut, MaterialData.VariationOpacity);
#endif

    // Normal Map (Tangent Space)
    NormalVectorOut = MaterialData.NormalAssigned ? UnpackNormalMap(float4(nsoTexture.rg, 1, 1), MaterialData.NormalScale) : float3(0, 0, 1);
    
    if (MaterialData.PackedTexture) {
        float4 packedTextureColor = MaterialData.PackedTextureAssigned ? float4(emTexture.a, nsoTexture.a, 0, nsoTexture.b) : float4(0, 1, 0, 0);

        MetallicOut = packedTextureColor.r;
        OcclussionOut = lerp(1, packedTextureColor.g, MaterialData.OcclussionStrength);
        if (MaterialData.SmoothnessEnabled) SmoothnessOut = packedTextureColor.a;
        else                   SmoothnessOut = 1 - packedTextureColor.a;
    } else {
        // Metallic
        MetallicOut = MaterialData.MetallicAssigned ? emTexture.a : MaterialData.Metallic;

        // Smoothness
        SmoothnessOut = MaterialData.SmoothnessAssigned ? nsoTexture.b : MaterialData.SmoothnessRoughness;
        if (!MaterialData.SmoothnessEnabled) SmoothnessOut = 1 - SmoothnessOut; // Roughness
            
        // Occlussion
        if (MaterialData.OcclussionAssigned) {
            OcclussionOut = nsoTexture.a;
            OcclussionOut = lerp(1, OcclussionOut, MaterialData.OcclussionStrength);
        } else {
            OcclussionOut = 1;
        }
    }

    // Emission
    EmissionColorOut = 0;
    if(MaterialData.EmissionEnabled) {
        EmissionColorOut = MaterialData.EmissionAssigned ? emTexture.rgb : 1;
        EmissionColorOut *= MaterialData.EmissionColor;
    }
}

void SampleRepetitionlessMaterial(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    int SurfaceType, int DebuggingIndex,

    // Textures
    int ArrayLayerIndex,
    Texture2DArray AVTextures,
    Texture2DArray NSOTextures,
    Texture2DArray EMTextures,
    int AssignedAVTextures[3],
    int AssignedNSOTextures[3],
    int AssignedEMTextures[3],

    Texture2D NoiseTexture,

    // Material Data
    in RepetitionlessMaterialData MaterialData,

    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
){
    float2 ddxUV = ddx(UV);
    float2 ddyUV = ddy(UV);

    SampleRepetitionlessMaterial(
        SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
        ArrayLayerIndex, AVTextures, NSOTextures, EMTextures, AssignedAVTextures, AssignedNSOTextures, AssignedEMTextures,
        NoiseTexture,
        MaterialData,
        AlbedoColorOut, NormalVectorOut, MetallicOut, SmoothnessOut, OcclussionOut, EmissionColorOut
    );
}

#endif
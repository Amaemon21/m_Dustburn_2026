#ifndef SAMPLEREPETITIONLESSLAYER_INCLUDED
#define SAMPLEREPETITIONLESSLAYER_INCLUDED

#include "../Structs/RepetitionlessMaterialData.hlsl"

#include "../RepetitionlessHelpers/PropertiesTextureUtilities.hlsl"
#include "../RepetitionlessHelpers/GetArrayAssignedTextures.hlsl"

#include "../Noise/VoronoiNoise2D.hlsl"
#include "../Noise/Keijiro/ClassicNoise2D.hlsl"
#include "../Noise/Keijiro/SimplexNoise2D.hlsl"

#include "../Utilities/TextureUtilities.hlsl"

#include "../TextureArrayEssentials/TextureArrayUtilities.hlsl"

#include "SampleRepetitionlessMaterial.hlsl"

// Uses assigned array properties variables
void SampleRepetitionlessLayer(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    float3 WorldPosition, float3 CameraPosition,
    int SurfaceType, int UVSpace, int VertexColourBlendModeIndex, int DebuggingIndex,
    float4 VertexColour,

    // Properties
    int LayerIndex,
    Texture2D PropertiesTexture,
    int AssignedAVTextures0,
    int AssignedAVTextures1,
    int AssignedAVTextures2,
    int AssignedNSOTextures0,
    int AssignedNSOTextures1,
    int AssignedNSOTextures2,
    int AssignedEMTextures0,
    int AssignedEMTextures1,
    int AssignedEMTextures2,
    int AssignedBMTextures0,

    // Textures
    Texture2DArray AVTextures,
    Texture2DArray NSOTextures,
    Texture2DArray EMTextures,
    Texture2DArray BMTextures,

    Texture2D NoiseTexture,

    // Outputs
    out float4 AlbedoColorOut,
    out float3 NormalVectorOut,
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
) {
    // ----------------------- Load Variables From Textures ------------------------- //

    RepetitionlessMaterialData baseMaterialData;
    RepetitionlessMaterialData farMaterialData;
    RepetitionlessMaterialData blendMaterialData;
    RepetitionlessLayerData    layerData;
    UnpackPropertiesTexture(PropertiesTexture, LayerIndex, baseMaterialData, farMaterialData, blendMaterialData, layerData);

    // Construct array assigned textures
    int assignedAVTexturesArray[]  = { AssignedAVTextures0,  AssignedAVTextures1,  AssignedAVTextures2  };
    int assignedNSOTexturesArray[] = { AssignedNSOTextures0, AssignedNSOTextures1, AssignedNSOTextures2 };
    int assignedEMTexturesArray[]  = { AssignedEMTextures0,  AssignedEMTextures1,  AssignedEMTextures2  };
    int assignedBMTexturesArray[]  = { AssignedBMTextures0, 0, 0, 0 };

    // ----------------------- Setup ------------------------- //

    // Variables
    float4 albedoColor   = 1;
    float3 normalVector  = WorldNormalVector;
    float  metallic      = 0;
    float  smoothness    = 0;
    float  occlussion    = 0;
    float3 emissionColor = 0;

    float materialMask = 0;
    float farDistance  = 0;

    // Use world space UVs if enabled
    if (UVSpace == 1) {
        // This makes the tiling the same as the standard terrain tiling
        // Dont ask me why
        UV = WorldPosition.xz / 1000;
    }

    float2 ddxUV = ddx(UV);
    float2 ddyUV = ddy(UV);

#ifdef _REPETITIONLESS_MATERIAL_BLEND
    if (layerData.MaterialBlendEnabled) {
        // Get mask of blended material
        switch (layerData.BlendMaskType) {
            case 0: // Perlin Noise
                materialMask = ClassicNoise(UV * layerData.MaterialBlendMaskTO.x + layerData.MaterialBlendMaskTO.zw) * 3;
                break;
            case 1: // Simplex Noise
                materialMask = SimplexNoise(UV * layerData.MaterialBlendMaskTO.x + layerData.MaterialBlendMaskTO.zw) * 2;
                break;
            case 2: // Custom Texture
                float4 bmTextureSample = SampleArrayAtConstantIndex(BMTextures, assignedBMTexturesArray, LayerIndex, UV * layerData.MaterialBlendMaskTO.xy + layerData.MaterialBlendMaskTO.zw, 0, SS);
                materialMask = bmTextureSample.r;
                break;
            case 3: // Vertex Colour
                float colourSimilarity = distance(VertexColour.rgb, layerData.MaterialBlendMaskTO.rgb);
                materialMask = 1.0 - smoothstep(layerData.BlendMaskVertexColourThreshold.x, layerData.BlendMaskVertexColourThreshold.y, colourSimilarity);

                break;
        }
        
        materialMask *= layerData.BlendMaskStrength;
        materialMask = clamp(materialMask, 0, 1);
        materialMask *= layerData.BlendMaskOpacity;
    }
#endif

    // ----------------------- Get Materials To Sample ------------------------- //

    int baseLayerIndex  = LayerIndex * 3 + 0;
    int farLayerIndex   = LayerIndex * 3 + 1;
    int blendLayerIndex = LayerIndex * 3 + 2;

    // At most two materials will be sampled when blending between
    // Get the material(s) to be sampled
    bool samplingBase = false;
    bool samplingBlend = false;
    bool samplingDistance = false;
    bool samplingDistanceBlend = false;

#ifdef _REPETITIONLESS_DISTANCE_BLEND
    // Check distance blend
    if (layerData.DistanceBlendEnabled) {
        // Distance Mask
        farDistance = distance(WorldPosition, CameraPosition);
        farDistance = Remap(farDistance, layerData.DistanceBlendMinMax, float2(0, 1));
        farDistance = clamp(farDistance, 0, 1);

        samplingDistance = farDistance > 0 && (materialMask != 1 || (layerData.MaterialBlendEnabled && !layerData.OverrideDistanceBlend));
    }
#endif

    // Check material blend
#ifdef _REPETITIONLESS_MATERIAL_BLEND
    if (layerData.MaterialBlendEnabled) {
        samplingBlend = materialMask > 0;
        if (layerData.DistanceBlendEnabled && layerData.OverrideDistanceBlend && layerData.OverrideDistanceBlendTO && farDistance > 0)
            samplingDistanceBlend = samplingBlend;

        if (samplingDistanceBlend != 0 && farDistance >= 1)
            samplingBlend = false;
    }
#endif

    // Check base material
    samplingBase = farDistance != 1 && materialMask != 1;

    // ----------------------- UVs / Triplanar ------------------------- // 
    
#ifdef _REPETITIONLESS_TRIPLANAR
    float3 triplanarWeights = pow(abs(WorldNormalVector), 8);
    triplanarWeights /= dot(triplanarWeights, 1.0);

    float2 triplanarUVs[3] = {
        WorldPosition.yz / 1000,
        WorldPosition.xz / 1000,
        WorldPosition.xy / 1000
    };

    float2 triplanarDdx[3] = {
        ddx(triplanarUVs[0]),
        ddx(triplanarUVs[1]),
        ddx(triplanarUVs[2])
    };
    
    float2 triplanarDdy[3] = {
        ddy(triplanarUVs[0]),
        ddy(triplanarUVs[1]),
        ddy(triplanarUVs[2])
    };

    float triplanarWeightsArray[3] = {
        triplanarWeights.x,
        triplanarWeights.y,
        triplanarWeights.z
    };

    float4 triplanarAlbedo = 0;

    [unroll]
    for (int i = 0; i < 3; i++) {
        // Dont sample this side if not used
        if (triplanarWeightsArray[i] < 0.01)
            continue;

            
        UV = triplanarUVs[i];
        ddxUV = triplanarDdx[i];
        ddyUV = triplanarDdy[i];
#endif

    // ----------------------- Base Material ------------------------- //
    [branch]
    if (samplingBase) {
        SampleRepetitionlessMaterial(
            SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
            baseLayerIndex, AVTextures, NSOTextures, EMTextures, assignedAVTexturesArray, assignedNSOTexturesArray, assignedEMTexturesArray,
            NoiseTexture,
            baseMaterialData,
            albedoColor, normalVector, metallic, smoothness, occlussion, emissionColor
        );
    }

#ifdef _REPETITIONLESS_DISTANCE_BLEND
    // ----------------------- Distance Material ------------------------- //
    [branch]
    if (samplingDistance) {
        float4 farAlbedoColor = 1;
        float3 farNormalVector = WorldNormalVector;
        float farMetallic = 0;
        float farSmoothness = 0;
        float farOcclussion = 0;
        float3 farEmissionColor = 0;
    
        switch (layerData.DistanceBlendMode)
        {
            case 0: // Tiling & Offset
                // Sample Base Material
                // Set far TO, no need to change back it wont be used again
                
                baseMaterialData.TilingOffset = farMaterialData.TilingOffset;
                
                SampleRepetitionlessMaterial(
                    SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
                    baseLayerIndex, AVTextures, NSOTextures, EMTextures, assignedAVTexturesArray, assignedNSOTexturesArray, assignedEMTexturesArray,
                    NoiseTexture,
                    baseMaterialData,
                    farAlbedoColor, farNormalVector, farMetallic, farSmoothness, farOcclussion, farEmissionColor
                );
                break;
            case 1: // Material
                // Sample Far Material
                SampleRepetitionlessMaterial(
                    SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
                    farLayerIndex, AVTextures, NSOTextures, EMTextures, assignedAVTexturesArray, assignedNSOTexturesArray, assignedEMTexturesArray,
                    NoiseTexture,
                    farMaterialData,
                    farAlbedoColor, farNormalVector, farMetallic, farSmoothness, farOcclussion, farEmissionColor
                );
                break;
        }

        // Combine Far with Base
        albedoColor = lerp(albedoColor, farAlbedoColor, farDistance);
        normalVector = lerp(normalVector, farNormalVector, farDistance);
        metallic = lerp(metallic, farMetallic, farDistance);
        smoothness = lerp(smoothness, farSmoothness, farDistance);
        occlussion = lerp(occlussion, farOcclussion, farDistance);
        emissionColor = lerp(emissionColor, farEmissionColor, farDistance);
    }
#endif

#ifdef _REPETITIONLESS_MATERIAL_BLEND
    // ----------------------- Blend Material ------------------------- //
    [branch]
    if (samplingBlend) {
        float4 blendAlbedoColor = 1;
        float3 blendNormalVector = WorldNormalVector;
        float blendMetallic = 0;
        float blendSmoothness = 0;
        float blendOcclussion = 0;
        float3 blendEmissionColor = 0;

        SampleRepetitionlessMaterial(
            SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
            blendLayerIndex, AVTextures, NSOTextures, EMTextures, assignedAVTexturesArray, assignedNSOTexturesArray, assignedEMTexturesArray,
            NoiseTexture,
            blendMaterialData,
            blendAlbedoColor, blendNormalVector, blendMetallic, blendSmoothness, blendOcclussion, blendEmissionColor
        );
        
        // Combine Blend with Base
        albedoColor = lerp(albedoColor, blendAlbedoColor, materialMask);
        normalVector = lerp(normalVector, blendNormalVector, materialMask);
        metallic = lerp(metallic, blendMetallic, materialMask);
        smoothness = lerp(smoothness, blendSmoothness, materialMask);
        occlussion = lerp(occlussion, blendOcclussion, materialMask);
        emissionColor = lerp(emissionColor, blendEmissionColor, materialMask);
    }

    // ----------------------- Distance Blend Material ------------------------- //
    // Only used when the blend tiling offset is changed at a distance

    [branch]
    if (samplingDistanceBlend) {
        float4 blendAlbedoColor = 1;
        float3 blendNormalVector = WorldNormalVector;
        float blendMetallic = 0;
        float blendSmoothness = 0;
        float blendOcclussion = 0;
        float3 blendEmissionColor = 0;
        
        float4 tilingOffset = float4(layerData.BlendMaskDistanceTO.xy, layerData.BlendMaskDistanceTO.zw);
        
        // Sample Blend Material
        // Set blend TO, no need to change back it wont be used again

        blendMaterialData.TilingOffset = tilingOffset;
        
        SampleRepetitionlessMaterial(
            SS, UV, ddxUV, ddyUV, WorldNormalVector, SurfaceType, DebuggingIndex,
            blendLayerIndex, AVTextures, NSOTextures, EMTextures, assignedAVTexturesArray, assignedNSOTexturesArray, assignedEMTexturesArray,
            NoiseTexture,
            blendMaterialData,
            blendAlbedoColor, blendNormalVector, blendMetallic, blendSmoothness, blendOcclussion, blendEmissionColor
        );
        
        // Combine Far Blend with Base 
        float lerpFactor = farDistance * materialMask;
        albedoColor = lerp(albedoColor, blendAlbedoColor, lerpFactor);
        normalVector = lerp(normalVector, blendNormalVector, lerpFactor);
        metallic = lerp(metallic, blendMetallic, lerpFactor);
        smoothness = lerp(smoothness, blendSmoothness, lerpFactor);
        occlussion = lerp(occlussion, blendOcclussion, lerpFactor);
        emissionColor = lerp(emissionColor, blendEmissionColor, lerpFactor);
    }
#endif

#ifdef _REPETITIONLESS_TRIPLANAR
        triplanarAlbedo += albedoColor * triplanarWeightsArray[i];
        albedoColor = 1;
    } // End loop

    albedoColor = triplanarAlbedo;
#endif

    // ----------------------- Output ------------------------- //

    // Vertex Colour
    switch (VertexColourBlendModeIndex) {
        case 1: albedoColor *= VertexColour;                        break; // Multiply
        case 2: albedoColor = saturate(albedoColor + VertexColour); break; // Additive
        case 3: albedoColor = saturate(albedoColor - VertexColour); break; // Subtractive
        case 4: albedoColor = VertexColour;                         break; // Overwrite
    }

    // Debugging
    switch (DebuggingIndex) {
        case 2: albedoColor = farDistance; break;
        case 3: albedoColor = materialMask; break;
    }  
    
    // If Transparency Disabled
    if (SurfaceType == 0 || DebuggingIndex != -1)
        albedoColor.a = 1;

    // Output
    AlbedoColorOut = albedoColor;
    NormalVectorOut = normalize(normalVector);
    MetallicOut = metallic;
    SmoothnessOut = smoothness;
    OcclussionOut = occlussion;
    EmissionColorOut = emissionColor;
}

// Uses assigned array properties texture
void SampleRepetitionlessLayer(
    // General Settings
    SamplerState SS, float2 UV, float3 WorldNormalVector,
    float3 WorldPosition, float3 CameraPosition,
    int SurfaceType, int UVSpace, int VertexColourBlendModeIndex, int DebuggingIndex,
    float4 VertexColour,

    // Properties
    int LayerIndex,
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
    out float  MetallicOut,
    out float  SmoothnessOut,
    out float  OcclussionOut,
    out float3 EmissionColorOut
){
    int assignedAVTextures[3];
    int assignedNSOTextures[3];
    int assignedEVTextures[3];
    int assignedBMTextures;

    GetArrayAssignedTextures(AssignedTexturesTexture, assignedAVTextures, assignedNSOTextures, assignedEVTextures, assignedBMTextures);

    SampleRepetitionlessLayer(
        SS, UV, WorldNormalVector,
        WorldPosition, CameraPosition,
        SurfaceType, UVSpace, VertexColourBlendModeIndex, DebuggingIndex,
        VertexColour,
        LayerIndex,
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
        AlbedoColorOut, NormalVectorOut, MetallicOut, SmoothnessOut, OcclussionOut, EmissionColorOut
    );
}

#endif
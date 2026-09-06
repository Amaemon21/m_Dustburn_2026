#ifndef PROPERTIESTEXTUREUTILITIES_INCLUDED
#define PROPERTIESTEXTUREUTILITIES_INCLUDED

#include "../Structs/RepetitionlessMaterialData.hlsl"
#include "../Structs/RepetitionlessLayerData.hlsl"

#include "../Utilities/BooleanCompression.hlsl"

#define REPETITIONLESS_MATERIAL_PACKED_VARIABLE_COUNT 9
#define REPETITIONLESS_LAYER_DATA_OFFSET REPETITIONLESS_MATERIAL_PACKED_VARIABLE_COUNT * 3

RepetitionlessMaterialData UnpackMaterialData(Texture2D propertiesTexture, int layerIndex, int indexOffset)
{
    RepetitionlessMaterialData materialData;

    // Load from texture
    half4 settings1            = propertiesTexture.Load(int3(0 + indexOffset, layerIndex, 0));
    half4 settings2            = propertiesTexture.Load(int3(1 + indexOffset, layerIndex, 0));
    half4 settings3            = propertiesTexture.Load(int3(2 + indexOffset, layerIndex, 0));
    half4 settings4            = propertiesTexture.Load(int3(3 + indexOffset, layerIndex, 0));
    half4 settings5            = propertiesTexture.Load(int3(4 + indexOffset, layerIndex, 0));
    materialData.AlbedoTint    = propertiesTexture.Load(int3(5 + indexOffset, layerIndex, 0)).rgb;
    materialData.EmissionColor = propertiesTexture.Load(int3(6 + indexOffset, layerIndex, 0)).rgb;
    materialData.TilingOffset  = propertiesTexture.Load(int3(7 + indexOffset, layerIndex, 0));
    materialData.VariationTO   = propertiesTexture.Load(int3(8 + indexOffset, layerIndex, 0));

    // Unpack
    int  settingToggles                = (int)settings1.x;
    materialData.NoiseEnabled          = GetCompressedValue(settingToggles, 0);
    materialData.RandomiseNoiseScaling = GetCompressedValue(settingToggles, 1);
    materialData.RandomiseRotation     = GetCompressedValue(settingToggles, 2);
    materialData.SmoothnessEnabled     = GetCompressedValue(settingToggles, 3);
    materialData.VariationEnabled      = GetCompressedValue(settingToggles, 4);
    materialData.PackedTexture         = GetCompressedValue(settingToggles, 5);
    materialData.EmissionEnabled       = GetCompressedValue(settingToggles, 6);

    int  assignedTextures              = (int)settings1.y;
    materialData.AlbedoAssigned        = GetCompressedValue(assignedTextures, 0);
    materialData.MetallicAssigned      = GetCompressedValue(assignedTextures, 1);
    materialData.SmoothnessAssigned    = GetCompressedValue(assignedTextures, 2);
    materialData.NormalAssigned        = GetCompressedValue(assignedTextures, 3);
    materialData.OcclussionAssigned    = GetCompressedValue(assignedTextures, 4);
    materialData.EmissionAssigned      = GetCompressedValue(assignedTextures, 5);
    materialData.VariationAssigned     = GetCompressedValue(assignedTextures, 6);
    materialData.PackedTextureAssigned = GetCompressedValue(assignedTextures, 7);

    materialData.Metallic            = settings1.z;
    materialData.SmoothnessRoughness = settings1.w;
    materialData.NormalScale         = settings2.x;
    materialData.OcclussionStrength  = settings2.y;
    materialData.AlphaClipping       = settings2.z;

    materialData.NoiseAngleOffset             = settings2.w;
    materialData.NoiseScale                   = settings3.x;
    materialData.NoiseScalingMinMax           = settings5.xy;
    materialData.NoiseRandomiseRotationMinMax = settings5.zw;

    materialData.VariationMode          = (int)settings3.y;
    materialData.VariationOpacity       = settings3.z;
    materialData.VariationBrightness    = settings3.w;
    materialData.VariationSmallScale    = settings4.x;
    materialData.VariationMediumScale   = settings4.y;
    materialData.VariationLargeScale    = settings4.z;
    materialData.VariationNoiseStrength = settings4.w;

    return materialData;
}

RepetitionlessLayerData UnpackLayerData(Texture2D propertiesTexture, int layerIndex)
{
    RepetitionlessLayerData layerData;

    // Load from texture
    half4 distanceBlendSettings          = propertiesTexture.Load(int3(0 + REPETITIONLESS_LAYER_DATA_OFFSET, layerIndex, 0));
    layerData.BlendMaskDistanceTO        = propertiesTexture.Load(int3(1 + REPETITIONLESS_LAYER_DATA_OFFSET, layerIndex, 0));
    half4 materialBlendSettings          = propertiesTexture.Load(int3(2 + REPETITIONLESS_LAYER_DATA_OFFSET, layerIndex, 0));
    layerData.MaterialBlendMaskTO        = propertiesTexture.Load(int3(3 + REPETITIONLESS_LAYER_DATA_OFFSET, layerIndex, 0));
    half4 materialBlendMaskExtraSettings = propertiesTexture.Load(int3(4 + REPETITIONLESS_LAYER_DATA_OFFSET, layerIndex, 0));

    // Unpack
    layerData.DistanceBlendEnabled = distanceBlendSettings.x > 0.99 ? true : false;
    layerData.DistanceBlendMode    = distanceBlendSettings.y;
    layerData.DistanceBlendMinMax  = distanceBlendSettings.zw;

    int materialBlendSettingsUnpacked = (int)materialBlendSettings.x;
    layerData.MaterialBlendEnabled    = GetCompressedValue(materialBlendSettingsUnpacked, 0);
    layerData.BlendMaskAssigned       = GetCompressedValue(materialBlendSettingsUnpacked, 1);
    layerData.OverrideDistanceBlend   = GetCompressedValue(materialBlendSettingsUnpacked, 2);
    layerData.OverrideDistanceBlendTO = GetCompressedValue(materialBlendSettingsUnpacked, 3);
    
    layerData.BlendMaskType     = materialBlendSettings.y;
    layerData.BlendMaskOpacity  = materialBlendSettings.z;
    layerData.BlendMaskStrength = materialBlendSettings.w;

    layerData.BlendMaskVertexColourThreshold = materialBlendMaskExtraSettings.xy;

    return layerData;
}

void UnpackPropertiesTexture(
    Texture2D propertiesTexture, int layerIndex,
    out RepetitionlessMaterialData baseMaterialData,
    out RepetitionlessMaterialData farMaterialData,
    out RepetitionlessMaterialData blendMaterialData,
    out RepetitionlessLayerData layerData
){
    baseMaterialData  = UnpackMaterialData(propertiesTexture, layerIndex, 0);
    farMaterialData   = UnpackMaterialData(propertiesTexture, layerIndex, REPETITIONLESS_MATERIAL_PACKED_VARIABLE_COUNT);
    blendMaterialData = UnpackMaterialData(propertiesTexture, layerIndex, REPETITIONLESS_MATERIAL_PACKED_VARIABLE_COUNT*2);
    layerData         = UnpackLayerData(propertiesTexture, layerIndex);
}

#endif
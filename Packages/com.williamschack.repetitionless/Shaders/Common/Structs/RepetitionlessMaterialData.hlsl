#ifndef REPETITIONLESSMATERIALDATA_INCLUDED
#define REPETITIONLESSMATERIALDATA_INCLUDED

struct RepetitionlessMaterialData
{
    // Settings1
    bool NoiseEnabled;
    bool RandomiseNoiseScaling;
    bool RandomiseRotation;
    bool SmoothnessEnabled;
    bool VariationEnabled;
    bool PackedTexture;
    bool EmissionEnabled;

    bool AlbedoAssigned;
    bool MetallicAssigned;
    bool SmoothnessAssigned;
    bool NormalAssigned;
    bool OcclussionAssigned;
    bool EmissionAssigned;
    bool VariationAssigned;
    bool PackedTextureAssigned;

    half Metallic;
    half SmoothnessRoughness;

    // Settings2
    half NormalScale;
    half OcclussionStrength;
    half AlphaClipping;
    half NoiseAngleOffset;

    // Settings3
    half NoiseScale;
    int  VariationMode;
    half VariationOpacity;
    half VariationBrightness;

    // Settings4
    half VariationSmallScale;
    half VariationMediumScale;
    half VariationLargeScale;
    half VariationNoiseStrength;

    // Settings5
    half2 NoiseScalingMinMax;
    half2 NoiseRandomiseRotationMinMax;

    // Other
    half3 AlbedoTint;
    half3 EmissionColor;

    half4 TilingOffset;
    half4 VariationTO;
};

#endif
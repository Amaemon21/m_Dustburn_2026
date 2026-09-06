#ifndef REPETITIONLESSLAYERDATA_INCLUDED
#define REPETITIONLESSLAYERDATA_INCLUDED

struct RepetitionlessLayerData
{
    // DistanceBlendSettings
    bool  DistanceBlendEnabled;
    int   DistanceBlendMode;
    half2 DistanceBlendMinMax;

    // MaterialBlendSettings
    bool MaterialBlendEnabled;
    bool BlendMaskAssigned;
    bool OverrideDistanceBlend;
    bool OverrideDistanceBlendTO;

    int  BlendMaskType;
    half BlendMaskOpacity;
    half BlendMaskStrength;

    // MaterialBlendMaskExtraSettings
    half2 BlendMaskVertexColourThreshold;

    // Others
    half4 BlendMaskDistanceTO;
    half4 MaterialBlendMaskTO;
};

#endif
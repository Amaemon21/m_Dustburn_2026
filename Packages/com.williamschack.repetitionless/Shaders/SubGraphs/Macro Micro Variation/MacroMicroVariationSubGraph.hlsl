#ifndef MACROMICROVARIATIONSUBGRAPH_INCLUDED
#define MACROMICROVARIATIONSUBGRAPH_INCLUDED

#include "../../Common/RepetitionlessHelpers/MacroMicroVariation.hlsl"

// Outputs a variation colour based on an inputted texture
void MacroMicroVariationTexture_float(
    float4 InputColor,

    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,
    float VariationOpacity,
    UnityTexture2D VariationTexture,

    SamplerState SS,
    float2 UV,
    float2 Tiling,
    float2 Offset,

    out float4 OutputColor
){
    VariationOpacity = clamp(VariationOpacity, 0, 1);
    VariationBrightness = clamp(VariationBrightness, 0, 1);
    
    float variationMultiplier = MacroMicroVariationTexture(SmallScale, MediumScale, LargeScale, VariationBrightness, VariationTexture.tex, SS, UV, Tiling, Offset);
    OutputColor = lerp(InputColor, variationMultiplier * InputColor, VariationOpacity);
}

// Outputs a variation colour based on perlin noise
void MacroMicroVariationPerlinNoise_float(
    float4 InputColor,
    
    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,
    float VariationOpacity,
    
    float NoiseStrength,
    float NoiseScale,
    float2 NoiseOffset,
    float2 UV,
    
    out float4 OutputColor
){
    VariationOpacity = clamp(VariationOpacity, 0, 1);
    VariationBrightness = clamp(VariationBrightness, 0, 1);
    
    float variationMultiplier = MacroMicroVariationPerlinNoise(SmallScale, MediumScale, LargeScale, VariationBrightness, NoiseStrength, UV, NoiseScale, NoiseOffset);
    OutputColor = lerp(InputColor, variationMultiplier * InputColor, VariationOpacity);
}

// Outputs a variation colour based on simplex noise
void MacroMicroVariationSimplexNoise_float(
    float4 InputColor,

    float SmallScale,
    float MediumScale,
    float LargeScale,
    
    float VariationBrightness,
    float VariationOpacity,
    
    float NoiseStrength,
    float NoiseScale,
    float2 NoiseOffset,
    float2 UV,
    
    out float4 OutputColor
){
    VariationOpacity = clamp(VariationOpacity, 0, 1);
    VariationBrightness = clamp(VariationBrightness, 0, 1);
    
    float variationMultiplier = MacroMicroVariationSimplexNoise(SmallScale, MediumScale, LargeScale, VariationBrightness, NoiseStrength, UV, NoiseScale, NoiseOffset);
    OutputColor = lerp(InputColor, variationMultiplier * InputColor, VariationOpacity);
}

#endif
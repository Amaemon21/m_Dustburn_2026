#ifndef MACROMICROVARIATION_INCLUDED
#define MACROMICROVARIATION_INCLUDED

// NOTE:
// Tiling & Offset must be done along with size scaling, would love to support pre-scaled UVs but it would break the offset otherwise
// eg. If a UV has been offset before inputting to this function, the offset will be multiplied by the Small, Medium, and Large scales

#include "../Noise/Keijiro/SimplexNoise2D.hlsl"
#include "../Noise/Keijiro/ClassicNoise2D.hlsl"

#include "../TextureArrayEssentials/TextureArrayUtilities.hlsl"

// Samples a given texture and turns it into a variation multiplier
float MacroMicroVariationTexture(
    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,
    Texture2D Texture,
    SamplerState SS,

    float2 UV,
    float2 Tiling = float2(1, 1),
    float2 Offset = float2(0, 0)
){
    // Get UVs
    float2 smallUV  = UV * Tiling * SmallScale + Offset;
    float2 mediumUV = UV * Tiling * MediumScale + Offset;
    float2 largeUV  = UV * Tiling * LargeScale + Offset;
    
    // Sample Texture
    float smallColor  = SAMPLE_TEXTURE2D(Texture, SS, smallUV).r;
    float mediumColor = SAMPLE_TEXTURE2D(Texture, SS, mediumUV).r;
    float largeColor  = SAMPLE_TEXTURE2D(Texture, SS, largeUV).r;
    
    // Add Brightness
    smallColor  += VariationBrightness;
    mediumColor += VariationBrightness;
    largeColor  += VariationBrightness;
        
    return smallColor * mediumColor * largeColor;
}

float MacroMicroVariationTextureArray(
    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,
    Texture2DArray TextureArray,
    int AssignedTextures[3],
    int ConstantIndex,
    int ChannelIndex,
    SamplerState SS,

    float2 UV,
    float2 Tiling = float2(1, 1),
    float2 Offset = float2(0, 0)
){
    // Get UVs
    float2 smallUV  = UV * Tiling * SmallScale + Offset;
    float2 mediumUV = UV * Tiling * MediumScale + Offset;
    float2 largeUV  = UV * Tiling * LargeScale + Offset;
    
    // Sample Texture
    int assignedTexturesPadded[BOOLEAN_COMPRESSION_MAX_CHUNKS] = {
        AssignedTextures[0],
        AssignedTextures[1],
        AssignedTextures[2],
        0
    };

    float4 smallColorSample  = SampleArrayAtConstantIndex(TextureArray, assignedTexturesPadded, ConstantIndex, smallUV, 0, SS);
    float4 mediumColorSample = SampleArrayAtConstantIndex(TextureArray, assignedTexturesPadded, ConstantIndex, mediumUV, 0, SS);
    float4 largeColorSample  = SampleArrayAtConstantIndex(TextureArray, assignedTexturesPadded, ConstantIndex, largeUV, 0, SS);

    // Add Brightness
    float smallColor  = smallColorSample[ChannelIndex];
    float mediumColor = mediumColorSample[ChannelIndex];
    float largeColor  = largeColorSample[ChannelIndex];
    smallColor  += VariationBrightness;
    mediumColor += VariationBrightness;
    largeColor  += VariationBrightness;
        
    return smallColor * mediumColor * largeColor;
}

// Samples perlin noise and turns it into a variation multiplier
float MacroMicroVariationPerlinNoise(
    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,

    float NoiseStrength,
    float2 UV,
    float NoiseScale = 1,
    float2 NoiseOffset = float2(0, 0)
){
    // Get UVs
    float2 smallUV  = UV * NoiseScale * SmallScale + NoiseOffset;
    float2 mediumUV = UV * NoiseScale * MediumScale + NoiseOffset;
    float2 largeUV  = UV * NoiseScale * LargeScale + NoiseOffset;
    
    // Sample Noise
    float smallColor  = ClassicNoise(smallUV) * 2 * NoiseStrength;
    float mediumColor = ClassicNoise(mediumUV) * 2 * NoiseStrength;
    float largeColor  = ClassicNoise(largeUV) * 2 * NoiseStrength;
    
    // Remap to more suitable size
    smallColor  = lerp(0.75, 1, smallColor);
    mediumColor = lerp(0.75, 1, mediumColor);
    largeColor  = lerp(0.75, 1, largeColor);
    
    // Add Brightness
    smallColor  += VariationBrightness;
    mediumColor += VariationBrightness;
    largeColor  += VariationBrightness;
        
    return smallColor * mediumColor * largeColor;
}

// Samples simplex noise and turns it into a variation multiplier
float MacroMicroVariationSimplexNoise(
    float SmallScale,
    float MediumScale,
    float LargeScale,

    float VariationBrightness,

    float NoiseStrength,
    float2 UV,
    float NoiseScale = 1,
    float2 NoiseOffset = float2(0, 0)
){
    // Get UVs
    float2 smallUV = UV * NoiseScale * SmallScale + NoiseOffset;
    float2 mediumUV = UV * NoiseScale * MediumScale + NoiseOffset;
    float2 largeUV = UV * NoiseScale * LargeScale + NoiseOffset;
    
    // Sample Noise
    float smallColor = SimplexNoise(smallUV) * 2 * NoiseStrength;
    float mediumColor = SimplexNoise(mediumUV) * 2 * NoiseStrength;
    float largeColor = SimplexNoise(largeUV) * 2 * NoiseStrength;
    
    // Remap to more suitable size
    smallColor = lerp(0.75, 1, smallColor);
    mediumColor = lerp(0.75, 1, mediumColor);
    largeColor = lerp(0.75, 1, largeColor);
    
    // Add Brightness
    smallColor += VariationBrightness;
    mediumColor += VariationBrightness;
    largeColor += VariationBrightness;
        
    return smallColor * mediumColor * largeColor;
}

#endif
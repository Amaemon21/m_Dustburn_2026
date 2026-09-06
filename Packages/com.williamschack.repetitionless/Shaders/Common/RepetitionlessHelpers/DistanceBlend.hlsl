#ifndef DISTANCEBLEND_INCLUDE
#define DISTANCEBLEND_INCLUDE

#include "../Utilities/TextureUtilities.hlsl"

// Calculates a normalized distance between the inputted min max based on the camera position
float CalculateFarDistance(float3 WorldPosition, float3 CameraPosition, float2 DistanceBlendMinMax)
{
    float farDistance = distance(WorldPosition, CameraPosition);
    farDistance = Remap(farDistance, DistanceBlendMinMax, float2(0, 1));
    farDistance = clamp(farDistance, 0, 1);
    return farDistance;
}

// Blends between a texture with differing tiling offset based on the camera position
void DistanceBlendTilingOffset_float(
    // Settings
    float3 WorldPosition, float3 CameraPosition,
    float2 DistanceBlendMinMax,
    
    // Visual Properties
    SamplerState SS, float2 UV,
    float4 BaseTilingOffset, float4 FarTilingOffset,
    UnityTexture2D Texture,
    
    // Output
    out float4 ColourOut
){
    // Calculate Far Distance
    float farDistance = CalculateFarDistance(WorldPosition, CameraPosition, DistanceBlendMinMax);
    
    // Default
    ColourOut = SAMPLE_TEXTURE2D(Texture.tex, SS, UV * BaseTilingOffset.xy + BaseTilingOffset.zw);
    
    // Only calculate if required
    if (farDistance == 0)
        return;
    
    // Resample base texture with far tiling & offset
    float4 farColour = SAMPLE_TEXTURE2D(Texture.tex, SS, UV * FarTilingOffset.xy + FarTilingOffset.zw);
    
    // Combine far with base
    ColourOut = lerp(ColourOut, farColour, farDistance);
}

// Blends between two inputted colours based on the camera position 
void DistanceBlendColour_float(
    // Settings
    float3 WorldPosition, float3 CameraPosition,
    float2 DistanceBlendMinMax,

    // Base/Far Colour
    float4 BaseColour, float4 FarColour,

    // Output
    out float4 ColourOut
){
    // Calculate Far Distance
    float farDistance = CalculateFarDistance(WorldPosition, CameraPosition, DistanceBlendMinMax);
    
    // Default
    ColourOut = BaseColour;
    
    // Only calculate if required
    if (farDistance == 0)
        return;

    // Combine far with base
    ColourOut = lerp(BaseColour, FarColour, farDistance);
}

#endif
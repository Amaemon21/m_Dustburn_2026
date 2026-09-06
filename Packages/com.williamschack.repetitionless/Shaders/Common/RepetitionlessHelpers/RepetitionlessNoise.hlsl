#ifndef REPETITIONLESSNOISE_INCLUDED
#define REPETITIONLESSNOISE_INCLUDED

#include "RepetitionlessTextureUtilities.hlsl"

#include "../Noise/VoronoiNoise2D.hlsl"

// Gets UVs based on voronoi noise
void GetRepetitionlessNoiseUVs(
    float2 UV, float2 DdxUV, float2 DdyUV,

    float NoiseAngleOffset,
    float NoiseScale,
    bool RandomiseNoiseScaling,
    float2 NoiseScalingMinMax,

    bool RandomiseRotation,
    float2 RandomiseRotationMinMax,

    out float VoronoiCells,
    out float EdgeMask,
    out float2 EdgeUV, out float2 EdgeDdxUV, out float2 EdgeDdyUV,
    out float2 TransformedUV
){
    // Generate Noise
    float VoronoiDistFromCenter;
    float VoronoiDistFromEdge;
    VoronoiNoise(UV, NoiseAngleOffset, NoiseScale, VoronoiDistFromCenter, VoronoiDistFromEdge, VoronoiCells);
    
    // Scale Edge UVs
    EdgeUV = UV;
    EdgeDdxUV = DdxUV;
    EdgeDdyUV = DdyUV;
    if (RandomiseNoiseScaling) {
        float minMaxAverage = (NoiseScalingMinMax.x + NoiseScalingMinMax.y) / 2;
        EdgeUV *= minMaxAverage;
        EdgeDdxUV *= minMaxAverage;
        EdgeDdyUV *= minMaxAverage;
    }
    
    // Generate Edge Mask, replicating a Sample Gradient Node
    EdgeMask = lerp(0.23, -1.5, VoronoiDistFromEdge) * 5;
    EdgeMask = clamp(EdgeMask, 0, 1);
    
    // Randomise UV Scaling
    TransformedUV = UV;
    if (RandomiseNoiseScaling) {
        float newUVTiling = Remap(VoronoiCells, float2(0, 1), NoiseScalingMinMax);
        TransformedUV *= newUVTiling;
    }
    
    // Rotate UVs
    if (RandomiseRotation) {
        float randomCellDegrees = Remap(VoronoiCells, float2(0, 1), RandomiseRotationMinMax);
        TransformedUV = RotateUVDegrees(TransformedUV, 0.0, randomCellDegrees);
    }
}

// Gets UVs based on a voronoi texture
void GetRepetitionlessNoiseUVs(
    float2 UV, float2 DdxUV, float2 DdyUV,

    float NoiseScale,
    bool RandomiseNoiseScaling,
    float2 NoiseScalingMinMax,

    bool RandomiseRotation,
    float2 RandomiseRotationMinMax,

    Texture2D NoiseTexture,
    int TextureResolution,

    out float VoronoiCells,
    out float EdgeMask,
    out float2 EdgeUV, out float2 EdgeDdxUV, out float2 EdgeDdyUV,
    out float2 TransformedUV
){
    // Load data from the noise texture
    float2 textureUV = frac(UV * NoiseScale / TextureResolution) * TextureResolution;
    float2 noiseTextureData = NoiseTexture.Load(int3(textureUV, 0)).rg;
    VoronoiCells = noiseTextureData.x;
    EdgeMask = noiseTextureData.y;

    // Scale Edge UVs
    EdgeUV = UV;
    EdgeDdxUV = DdxUV;
    EdgeDdyUV = DdyUV;
    if (RandomiseNoiseScaling) {
        float minMaxAverage = (NoiseScalingMinMax.x + NoiseScalingMinMax.y) / 2;
        EdgeUV *= minMaxAverage;
        EdgeDdxUV *= minMaxAverage;
        EdgeDdyUV *= minMaxAverage;
    }

    // Randomise UV Scaling
    TransformedUV = UV;
    if (RandomiseNoiseScaling) {
        float newUVTiling = Remap(VoronoiCells, float2(0, 1), NoiseScalingMinMax);
        TransformedUV *= newUVTiling;
    }
    
    // Rotate UVs
    if (RandomiseRotation) {
        float randomCellDegrees = Remap(VoronoiCells, float2(0, 1), RandomiseRotationMinMax);
        TransformedUV = RotateUVDegrees(TransformedUV, 0.0, randomCellDegrees);
    }
}

#endif
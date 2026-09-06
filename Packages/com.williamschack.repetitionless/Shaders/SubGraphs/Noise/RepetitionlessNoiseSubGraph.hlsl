#ifndef REPETITIONLESSNOISESUBGRAPH_INCLUDED
#define REPETITIONLESSNOISESUBGRAPH_INCLUDED

#include "../../Common/RepetitionlessHelpers/RepetitionlessNoise.hlsl"

// Samples the given texture using modified UVs based on voronoi noise
// Samples the voronoi cells base and edge colour if required and lerps them together
void AddRepetitionlessNoise_float(
    UnityTexture2D InputTexture,
    SamplerState SS,
    float2 UV,

    float NoiseAngleOffset,
    float NoiseScale,
    bool RandomiseNoiseScaling,
    float2 NoiseScalingMinMax,
    
    bool RandomiseRotation, 
    float2 RandomiseRotationMinMax,

    out float4 OutputColor
){
    float2 DdxUV = ddx(UV);
    float2 DdyUV = ddy(UV);
    
    float VoronoiCells = 1;
    float EdgeMask = 0;
    float2 EdgeUV = UV;
    float2 EdgeDdxUV = DdxUV;
    float2 EdgeDdyUV = DdyUV;
    float2 TransformedUV = UV;
    GetRepetitionlessNoiseUVs(UV, DdxUV, DdyUV, NoiseAngleOffset, NoiseScale, RandomiseNoiseScaling, NoiseScalingMinMax, RandomiseRotation, RandomiseRotationMinMax, VoronoiCells, EdgeMask, EdgeUV, EdgeDdxUV, EdgeDdyUV, TransformedUV);
    
    OutputColor = SampleRepetitionlessTexture(InputTexture.tex, SS, DdxUV, DdyUV, EdgeDdxUV, EdgeDdyUV, EdgeMask, EdgeUV, TransformedUV, true);
}

#endif
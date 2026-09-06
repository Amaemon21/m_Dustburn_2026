#ifndef REPETITIONLESSTERRAINMETAPASS_INCLUDED
#define REPETITIONLESSTERRAINMETAPASS_INCLUDED

#include "MetaPass.hlsl"

Varyings TerrainVertexMeta(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    TerrainInstancing(input.positionOS, input.normalOS, input.uv0);
    // For some reason, uv1 and uv2 are not populated for instanced terrain. Use uv0.
    input.uv1 = input.uv2 = input.uv0;
    output = VertexMeta(input);
    return output;
}

half4 TerrainFragmentMeta(Varyings input) : SV_Target
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.uv);
#endif
    return FragmentMetaLit(input);
}

#endif
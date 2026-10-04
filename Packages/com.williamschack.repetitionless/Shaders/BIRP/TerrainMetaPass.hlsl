#ifndef REPETITIONLESSTERRAINMETAPASS_INCLUDED
#define REPETITIONLESSTERRAINMETAPASS_INCLUDED

#include "MetaPass.hlsl"

v2f TerrainVertexMeta(VertexInput input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    TerrainInstancing(input.positionOS, input.normalOS, input.uv0);

    input.uv1 = input.uv2 = input.uv0;

    return VertexMeta(input);
}

half4 TerrainFragmentMeta(Varyings input) : SV_Target
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.uv);
#endif

    return FragmentMeta(input);
}

#endif
#ifndef REPETITIONLESSTERRAINSHADOWCASTERPASS_INCLUDED
#define REPETITIONLESSTERRAINSHADOWCASTERPASS_INCLUDED

#include "UnityCG.cginc"

struct Attributes
{
    float4 vertex   : POSITION;
    float3 normal   : NORMAL;
    float2 texcoord : TEXCOORD0;

    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float2 texcoord : TEXCOORD0;
    V2F_SHADOW_CASTER;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

v2f ShadowPassVertex(Attributes v)
{
    v2f output;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, output);
    TerrainInstancing(v.vertex, v.normal, v.texcoord);

    TRANSFER_SHADOW_CASTER_NORMALOFFSET(output)

    output.texcoord = v.texcoord;

    return output;
}

half4 ShadowPassFragment(v2f input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

#ifdef _ALPHATEST_ON
    ClipHoles(input.texcoord);
#endif

    SHADOW_CASTER_FRAGMENT(input);
}

#endif
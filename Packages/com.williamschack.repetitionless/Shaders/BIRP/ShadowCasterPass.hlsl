#ifndef REPETITIONLESSSHADOWCASTERPASS_INCLUDED
#define REPETITIONLESSSHADOWCASTERPASS_INCLUDED

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

    TRANSFER_SHADOW_CASTER_NORMALOFFSET(output)

    output.texcoord = v.texcoord;
    return output;
}

half4 ShadowPassFragment(v2f input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

#ifdef LOD_FADE_CROSSFADE
    #ifdef _LOD_FADE_ON_ALPHA
        #undef _LOD_FADE_ON_ALPHA
    #else
        UnityApplyDitherCrossFade(input.pos.xy);
    #endif
#endif

#ifdef _ALPHATEST_ON
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, input.texcoord).r;
    clip(hole < 0.0005f ? -1 : 1);
#endif

    SHADOW_CASTER_FRAGMENT(input);
}

#endif
#ifndef REPETITIONLESSTERRAINSHADOWCASTERPASS_INCLUDED
#define REPETITIONLESSTERRAINSHADOWCASTERPASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" // Fixes a LerpWhiteTo bug in older unity versions
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
// For Directional lights, _LightDirection is used when applying shadow Normal Bias.
// For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
float3 _LightDirection;
float3 _LightPosition;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 clipPos  : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings ShadowPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    TerrainInstancing(input.positionOS, input.normalOS, input.texcoord);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 clipPos = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_VERSION >= 600000
    clipPos = ApplyShadowClamping(clipPos);
#else
    #if UNITY_REVERSED_Z
        clipPos.z = min(clipPos.z, UNITY_NEAR_CLIP_VALUE);
    #else
        clipPos.z = max(clipPos.z, UNITY_NEAR_CLIP_VALUE);
    #endif
#endif

    output.clipPos = clipPos;

    output.texcoord = input.texcoord;

    return output;
}

half4 ShadowPassFragment(Varyings input) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.texcoord);
#endif
    return 0;
}

#endif
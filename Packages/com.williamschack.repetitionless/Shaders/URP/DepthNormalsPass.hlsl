#ifndef REPETITIONLESSDEPTHNORMALSPASS_INCLUDED
#define REPETITIONLESSDEPTHNORMALSPASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#include "../Common/Main/SampleRepetitionlessDynamic.hlsl"

struct Attributes
{
    float4 positionOS   : POSITION;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float3 normal       : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS  : SV_POSITION;
    float2 uv          : TEXCOORD1;
    half3 normalWS     : TEXCOORD2;
    float3 positionWS  : TEXCOORD3;
    half4 tangentWS    : TEXCOORD4;    // xyz: tangent, w: sign
    half3 viewDirWS    : TEXCOORD5;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};


Varyings DepthNormalsVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.uv = input.texcoord;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normal, input.tangentOS);

    output.normalWS = half3(normalInput.normalWS);
    output.positionWS = vertexInput.positionWS;
    float sign = input.tangentOS.w * float(GetOddNegativeScale());
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);

    return output;
}

half4 DepthNormalsFragment(Varyings input) : SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    #if _SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF_KEYWORD_DECLARED
        if (_SCREENSPACEREFLECTIONSCONTRIBUTETRANSPARENT_OFF)
            discard;
    #endif

    half4 outNormalWS = 0;

    #if defined(_GBUFFER_NORMALS_OCT)
        float3 normalWS = normalize(input.normalWS);
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
        half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
        outNormalWS = half4(packedNormalWS, 0.0);
    #else
        // Sample material for the normals
        float4 albedo;
        float3 normalTS;
        float  metallic;
        float  smoothness;
        float  occlusion;
        float3 emission;
        SampleRepetitionless(
            input.uv, input.normalWS, input.positionWS, 0,
            albedo, normalTS, metallic, smoothness, occlusion, emission
        );

        float sgn = input.tangentWS.w;
        float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
        float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
        outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
    #endif

    return outNormalWS;
}

#endif
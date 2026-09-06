#ifndef REPETITIONLESSTERRAINPASSES_INCLUDED
#define REPETITIONLESSTERRAINPASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#ifdef REPETITIONLESS_GBUFFER
#if UNITY_VERSION >= 600010
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#else
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#endif
#endif

#include "../Common/Main/SampleRepetitionlessDynamic.hlsl"

// Structs
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4 colour      : COLOR;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 uvMainAndLM : TEXCOORD0; // xy: control, zw: lightmap
    half4 normal       : TEXCOORD1;
    half4 tangent      : TEXCOORD2;
    half4 bitangent    : TEXCOORD3;
    
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD4; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor : TEXCOORD4;
#endif
    float3 positionWS : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif
#if defined(DYNAMICLIGHTMAP_ON)
    float2 dynamicLightmapUV : TEXCOORD7;
#endif
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD8;
#endif

    half4 colour  : COLOR;
    float4 clipPos : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

// Helpers
void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.clipPos;

    // Convert normals from tangent to world space
    inputData.tangentToWorld = half3x3(-input.tangent.xyz, input.bitangent.xyz, input.normal.xyz);
    inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentToWorld);
    inputData.normalWS = normalize(inputData.normalWS);
        
    inputData.viewDirectionWS = half3(input.normal.w, input.tangent.w, input.bitangent.w);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
    
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.clipPos);

#if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
        inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
        inputData.staticLightmapUV = input.uvMainAndLM.zw;
    #else
        inputData.vertexSH = 0;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
        inputData.probeOcclusion = input.probeOcclusion;
    #endif
#endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    half3 SH = 0;

#if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.uvMainAndLM.zw, input.dynamicLightmapUV, SH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.uvMainAndLM.zw);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(SH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.uvMainAndLM.zw, SH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.uvMainAndLM.zw);
#endif
}

// Lit
Varyings Vert(Attributes input)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(input.positionOS, input.normalOS, input.texcoord);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    o.uvMainAndLM.xy = input.texcoord;
    o.uvMainAndLM.zw = input.texcoord * unity_LightmapST.xy + unity_LightmapST.zw;

#if defined(DYNAMICLIGHTMAP_ON)
    o.dynamicLightmapUV = input.texcoord * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
    float4 vertexTangent = float4(cross(float3(0, 0, 1), input.normalOS), 1.0);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, vertexTangent);

    o.normal = half4(normalInput.normalWS, viewDirWS.x);
    o.tangent = half4(normalInput.tangentWS, viewDirWS.y);
    o.bitangent = half4(normalInput.bitangentWS, viewDirWS.z);

    half fogFactor = 0;
#if !defined(_FOG_FRAGMENT)
    fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    o.fogFactorAndVertexLight.x = fogFactor;
    o.fogFactorAndVertexLight.yzw = VertexLighting(vertexInput.positionWS, o.normal.xyz);
#else
    o.fogFactor = fogFactor;
#endif

    o.positionWS = vertexInput.positionWS;
    o.clipPos = vertexInput.positionCS;
    o.colour = input.colour;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    o.shadowCoord = GetShadowCoord(vertexInput);
#endif

    return o;
}

#ifdef REPETITIONLESS_GBUFFER
#if UNITY_VERSION >= 600010
GBufferFragOutput Frag(Varyings input)
#else
FragmentOutput Frag(Varyings input)
#endif
#else
half4 Frag(Varyings input) : SV_TARGET
#endif
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.uvMainAndLM.xy;

    // Main function
    float4 albedo;
    float3 normalTS;
    float  metallic;
    float  smoothness;
    float  occlusion;
    float3 emission;
    SampleRepetitionless(
        uv, input.normal.xyz, input.positionWS, input.colour,
        albedo, normalTS, metallic, smoothness, occlusion, emission
    );

    InputData inputData;
    InitializeInputData(input, normalTS, inputData);

#if defined(_DBUFFER)
    half3 specular = half3(0.0h, 0.0h, 0.0h);
    ApplyDecal(input.clipPos,
        albedo.rgb,
        specular,
        inputData.normalWS,
        metallic,
        occlusion,
        smoothness);
#endif

    InitializeBakedGIData(input, inputData);

#ifdef REPETITIONLESS_GBUFFER
    BRDFData brdfData;
    InitializeBRDFData(albedo, metallic, half3(0.0h, 0.0h, 0.0h), smoothness, albedo.a, brdfData);

    half4 colour;
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
    colour.rgb = GlobalIllumination(brdfData, (BRDFData)0, 0, inputData.bakedGI, occlusion, inputData.positionWS,
                                   inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    colour.a = albedo.a;
    colour.rgb *= colour.a;
    brdfData.albedo.rgb *= albedo.a;
    brdfData.diffuse.rgb *= albedo.a;
    brdfData.specular.rgb *= albedo.a;
    brdfData.reflectivity *= albedo.a;
    inputData.normalWS = inputData.normalWS * albedo.a;
    smoothness *= albedo.a;

#if UNITY_VERSION >= 600010
    return PackGBuffersBRDFData(
#else
    return BRDFDataToGbuffer(
#endif
        brdfData, inputData, smoothness, colour.rgb, occlusion);
#else
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo      = albedo.rgb;
    surfaceData.metallic    = metallic;
    surfaceData.smoothness  = smoothness;
    surfaceData.occlusion   = occlusion;
    surfaceData.emission    = emission;
    surfaceData.alpha       = 1;

    half4 colour = UniversalFragmentPBR(inputData, surfaceData);
    colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
#endif

    return colour;
}

#endif
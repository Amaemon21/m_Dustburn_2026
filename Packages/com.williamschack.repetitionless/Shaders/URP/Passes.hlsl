#ifndef REPETITIONLESSTERRAINPASSES_INCLUDED
#define REPETITIONLESSTERRAINPASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#ifdef LOD_FADE_CROSSFADE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

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
    float4 tangentOS  : TANGENT;
    half4 colour      : COLOR;
    float2 texcoord   : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;

#ifdef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
    float3 positionWS               : TEXCOORD1;
#endif

    float3 normalWS : TEXCOORD2;
    
    half4 tangentWS : TEXCOORD3;    // xyz: tangent, w: sign

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD5; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor               : TEXCOORD5;
#endif

#ifdef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
    float4 shadowCoord : TEXCOORD6;
#endif

#ifdef REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
    half3 viewDirTS : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif

    float4 positionCS : SV_POSITION;
    half4 colour      : COLOR;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Helpers
void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#ifdef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
    inputData.positionWS = input.positionWS;
#endif

#ifdef DEBUG_DISPLAY
    inputData.positionCS = input.positionCS;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    inputData.tangentToWorld = tangentToWorld;
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#ifdef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
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

#ifdef UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION
    float2 preRotatedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    switch (UNITY_DISPLAY_ORIENTATION_PRETRANSFORM) {
        default:
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_0: inputData.normalizedScreenSpaceUV = preRotatedScreenSpaceUV; break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_90: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.y, preRotatedScreenSpaceUV.x); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_180: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.x, 1 - preRotatedScreenSpaceUV.y); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_270: inputData.normalizedScreenSpaceUV = float2(preRotatedScreenSpaceUV.y, 1 - preRotatedScreenSpaceUV.x); break;
    }
#else
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#endif

#ifdef DEBUG_DISPLAY
    #ifdef DYNAMICLIGHTMAP_ON
        inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #ifdef LIGHTMAP_ON
        inputData.staticLightmapUV = input.staticLightmapUV;
    #else
        inputData.vertexSH = input.vertexSH;
    #endif
    #ifdef USE_APV_PROBE_OCCLUSION
        inputData.probeOcclusion = input.probeOcclusion;
    #endif
#endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
#ifdef _SCREEN_SPACE_IRRADIANCE
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
#elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
}

// Lit
Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
#ifndef _FOG_FRAGMENT
    fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif

    output.uv = input.texcoord;

    output.normalWS = normalInput.normalWS;

    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif

#if UNITY_VERSION >= 600000
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#elif UNITY_VERSION >= 202310
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH);
#else
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#ifdef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
    output.positionWS = vertexInput.positionWS;
#endif

#ifdef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;

    return output;
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
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    // Main Function
    float4 albedo;
    float3 normalTS;
    float  metallic;
    float  smoothness;
    float  occlusion;
    float3 emission;
    SampleRepetitionless(
        input.uv, input.normalWS, input.positionWS, input.colour,
        albedo, normalTS, metallic, smoothness, occlusion, emission
    );

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo      = albedo.rgb;
    surfaceData.metallic    = metallic;
    surfaceData.smoothness  = smoothness;
    surfaceData.occlusion   = occlusion;
    surfaceData.emission    = emission;
    surfaceData.alpha       = 1;

    // Input Data
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, normalTS, inputData);

#ifdef _DBUFFER
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

#ifdef REPETITIONLESS_GBUFFER
    BRDFData brdfData;
    InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);

#if UNITY_VERSION >= 600010
    half3 color = GlobalIllumination(brdfData,
                                        (BRDFData)0, 0,
                                        inputData.bakedGI, surfaceData.occlusion, inputData.positionWS,
                                        inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#else
    half3 color = GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS);
#endif

#if UNITY_VERSION >= 600010
    return PackGBuffersBRDFData(
#else
    return BRDFDataToGbuffer(
#endif    
        brdfData, inputData, surfaceData.smoothness, surfaceData.emission + color, surfaceData.occlusion);
#else
    // Output
    half4 colour = UniversalFragmentPBR(inputData, surfaceData);
    colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
    colour.a = 1;

    return colour;
#endif
}

#endif
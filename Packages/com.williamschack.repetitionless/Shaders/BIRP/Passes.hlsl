#ifndef REPETITIONLESSPASSES_INCLUDED
#define REPETITIONLESSPASSES_INCLUDED

#ifndef UNITY_SETUP_BRDF_INPUT
#define UNITY_SETUP_BRDF_INPUT MetallicSetup
#endif

#include "UnityStandardCore.cginc"

#include "../Common/Main/SampleRepetitionlessDynamic.hlsl"

struct Attributes
{
    float4 vertex     : POSITION; // positionOS, named for UNITY_TRANSFER_LIGHTING
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    half4 colour      : COLOR;

    float2 texcoord   : TEXCOORD0;
    float2 uv1        : TEXCOORD1;
#if defined(DYNAMICLIGHTMAP_ON)
    float2 uv2      : TEXCOORD2;
#endif

    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float2 uv                 : TEXCOORD0;
    float4 eyeVec             : TEXCOORD1;
    float3 positionWS         : TEXCOORD2;
    float3 normalWS           : TEXCOORD3;
    half4 tangentWS           : TEXCOORD4;
#ifdef ADD_PASS
    half3 lightDir            : TEXCOORD5;
#else
    half4 ambientOrLightmapUV : TEXCOORD5;
#endif
    UNITY_LIGHTING_COORDS(6,7)

    float4 pos        : SV_POSITION; // positionCS, named for UNITY_TRANSFER_LIGHTING
    half4 colour      : COLOR;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Same as UnityStandardCore but using my Attributes
inline half4 VertexGIForwardCustom(Attributes v, float3 posWorld, half3 normalWorld)
{
    half4 ambientOrLightmapUV = 0;
    // Static lightmaps
    #ifdef LIGHTMAP_ON
        ambientOrLightmapUV.xy = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
        ambientOrLightmapUV.zw = 0;
    // Sample light probe for Dynamic objects only (no static or dynamic lightmaps)
    #elif defined(UNITY_SHOULD_SAMPLE_SH)
        #ifdef VERTEXLIGHT_ON
            // Approximated illumination from non-important point lights
            ambientOrLightmapUV.rgb = Shade4PointLights (
                unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                unity_4LightAtten0, posWorld, normalWorld);
        #endif

        ambientOrLightmapUV.rgb = ShadeSHPerVertex (normalWorld, ambientOrLightmapUV.rgb);
    #endif

    #ifdef DYNAMICLIGHTMAP_ON
        ambientOrLightmapUV.zw = v.uv2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif

    return ambientOrLightmapUV;
}

FragmentCommonData ConstructFragData(v2f input, float4 albedo, float3 normalTS, float metallic, float smoothness)
{
    // Convert normal from tangent space to world space
    half sign = input.tangentWS.w;
    float3 bitangent = sign * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    half3 normalWS = mul(normalTS, tangentToWorld);

    // Get colour
    half oneMinusReflectivity;
    half3 specColour;
    half3 diffColour = DiffuseAndSpecularFromMetallic(albedo.rgb, metallic, specColour, oneMinusReflectivity);

    // Construct data
    FragmentCommonData data = (FragmentCommonData)0;
    data.diffColor = diffColour;
    data.specColor = specColour;
    data.oneMinusReflectivity = oneMinusReflectivity;
    data.smoothness = smoothness;
    data.normalWorld = normalWS;
    data.eyeVec = NormalizePerPixelNormal(input.eyeVec.xyz);
    data.posWorld = input.positionWS;

    return data;
}

v2f Vert(Attributes v)
{
    v2f output = (v2f)0;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
    output.positionWS = posWorld.xyz;
    output.pos = UnityObjectToClipPos(v.vertex);

    output.uv = v.texcoord;

    float3 normalWorld = UnityObjectToWorldNormal(v.normalOS);
    output.normalWS = normalWorld;

    half sign = half(v.tangentOS.w) * unity_WorldTransformParams.w;
    half3 tangentWS = UnityObjectToWorldDir(v.tangentOS.xyz);
    output.tangentWS = half4(tangentWS.xyz, sign);

    output.eyeVec.xyz = NormalizePerVertexNormal(posWorld.xyz - _WorldSpaceCameraPos);

    UNITY_TRANSFER_LIGHTING(output, v.uv1);

#ifdef ADD_PASS
    float3 lightDir = _WorldSpaceLightPos0.xyz - posWorld.xyz * _WorldSpaceLightPos0.w;
    #ifndef USING_DIRECTIONAL_LIGHT
        lightDir = NormalizePerVertexNormal(lightDir);
    #endif

    output.lightDir = lightDir;
#elif defined(DEFERRED_PASS)
    output.ambientOrLightmapUV = 0;
    #ifdef LIGHTMAP_ON
        output.ambientOrLightmapUV.xy = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    #elif defined(UNITY_SHOULD_SAMPLE_SH)
        output.ambientOrLightmapUV.rgb = ShadeSHPerVertex (normalWorld, output.ambientOrLightmapUV.rgb);
    #endif
    #ifdef DYNAMICLIGHTMAP_ON
        output.ambientOrLightmapUV.zw = v.uv2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
#else
    output.ambientOrLightmapUV = VertexGIForwardCustom(v, posWorld, normalWorld);
#endif

#ifndef DEFERRED_PASS
    UNITY_TRANSFER_FOG_COMBINED_WITH_EYE_VEC(output, output.pos);
#endif

    return output;
}

half4 Frag(v2f i) : SV_TARGET
{
#ifdef ADD_PASS
    UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);
#else
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);
#endif
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    // Main Function
    float4 albedo;
    float3 normalTS;
    float  metallic;
    float  smoothness;
    float  occlusion;
    float3 emission;
    SampleRepetitionless(
        i.uv, i.normalWS, i.positionWS, i.colour,
        albedo, normalTS, metallic, smoothness, occlusion, emission
    );

    FragmentCommonData data = ConstructFragData(i, albedo, normalTS, metallic, smoothness);

    UNITY_LIGHT_ATTENUATION(atten, i, i.positionWS);

#ifdef ADD_PASS
    UnityLight light = AdditiveLight(i.lightDir, atten);
    UnityIndirect indirect = ZeroIndirect();
#else
    UnityLight mainLight = MainLight();
    UnityGI gi = FragmentGI(data, occlusion, i.ambientOrLightmapUV, atten, mainLight);
    UnityLight light = gi.light;
    UnityIndirect indirect = gi.indirect;
#endif

    half4 colour = UNITY_BRDF_PBS (data.diffColor, data.specColor, data.oneMinusReflectivity, data.smoothness, data.normalWorld, -data.eyeVec, light, indirect);
    colour.rgb += emission;
    colour.a = albedo.a;

    UNITY_EXTRACT_FOG_FROM_EYE_VEC(i);
    UNITY_APPLY_FOG(_unity_fogCoord, colour.rgb);

    return colour;
}

#ifdef DEFERRED_PASS
void FragDeferred (
    v2f i,
    out half4 outGBuffer0 : SV_Target0,
    out half4 outGBuffer1 : SV_Target1,
    out half4 outGBuffer2 : SV_Target2,
    out half4 outEmission : SV_Target3          // RT3: emission (rgb), --unused-- (a)
#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
    ,out half4 outShadowMask : SV_Target4       // RT4: shadowmask (rgba)
#endif
)
{
    #if (SHADER_TARGET < 30)
        outGBuffer0 = 1;
        outGBuffer1 = 1;
        outGBuffer2 = 0;
        outEmission = 0;
        #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
            outShadowMask = 1;
        #endif
        return;
    #endif

    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);

    // Main Function
    float4 albedo;
    float3 normalTS;
    float  metallic;
    float  smoothness;
    float  occlusion;
    float3 emission;
    SampleRepetitionless(
        i.uv, i.normalWS, i.positionWS, i.colour,
        albedo, normalTS, metallic, smoothness, occlusion, emission
    );

    FragmentCommonData s = ConstructFragData(i, albedo, normalTS, metallic, smoothness);

    // no analytic lights in this pass
    UnityLight dummyLight = DummyLight ();
    half atten = 1;

    // only GI
#if UNITY_ENABLE_REFLECTION_BUFFERS
    bool sampleReflectionsInDeferred = false;
#else
    bool sampleReflectionsInDeferred = true;
#endif

    UnityGI gi = FragmentGI(s, occlusion, i.ambientOrLightmapUV, atten, dummyLight, sampleReflectionsInDeferred);

    half3 emissiveColor = UNITY_BRDF_PBS (s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness, s.normalWorld, -s.eyeVec, gi.light, gi.indirect).rgb;
    emissiveColor.rgb += emission;

    #ifndef UNITY_HDR_ON
        emissiveColor.rgb = exp2(-emissiveColor.rgb);
    #endif

    UnityStandardData data;
    data.diffuseColor   = s.diffColor;
    data.occlusion      = occlusion;
    data.specularColor  = s.specColor;
    data.smoothness     = s.smoothness;
    data.normalWorld    = s.normalWorld;

    UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

    // Emissive lighting buffer
    outEmission = half4(emissiveColor, 1);

    // Baked direct lighting occlusion if any
    #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
        outShadowMask = UnityGetRawBakedOcclusions(i.ambientOrLightmapUV.xy, IN_WORLDPOS(i));
    #endif
}
#endif

#endif
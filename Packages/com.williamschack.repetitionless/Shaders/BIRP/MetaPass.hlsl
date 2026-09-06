#ifndef REPETITIONLESSMETAPASS_INCLUDED
#define REPETITIONLESSMETAPASS_INCLUDED

#include "UnityCG.cginc"
#include "UnityStandardInput.cginc"
#include "UnityMetaPass.cginc"
#include "UnityStandardCore.cginc"

#include "../Common/Main/SampleRepetitionlessDynamic.hlsl"

struct v2f
{
    float4 pos        : SV_POSITION;
    float4 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
#ifdef EDITOR_VISUALIZATION
    float2 vizUV      : TEXCOORD3;
    float4 lightCoord : TEXCOORD4;
#endif
};

FragmentCommonData ConstructFragData(v2f input, float4 albedo, float3 normalTS, float metallic, float smoothness)
{
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

    return data;
}

v2f VertexMeta(VertexInput v)
{
    v2f o = (v2f)0;
    o.pos = UnityMetaVertexPosition(v.vertex, v.uv1.xy, v.uv2.xy, unity_LightmapST, unity_DynamicLightmapST);
    o.uv = TexCoords(v);
#ifdef EDITOR_VISUALIZATION
    o.vizUV = 0;
    o.lightCoord = 0;
    if (unity_VisualizationMode == EDITORVIZ_TEXTURE)
        o.vizUV = UnityMetaVizUV(unity_EditorViz_UVIndex, v.uv0.xy, v.uv1.xy, v.uv2.xy, unity_EditorViz_Texture_ST);
    else if (unity_VisualizationMode == EDITORVIZ_SHOWLIGHTMASK)
    {
        o.vizUV = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
        o.lightCoord = mul(unity_EditorViz_WorldToLight, mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)));
    }
#endif

    float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
    o.positionWS = posWorld.xyz;

    float3 normalWorld = UnityObjectToWorldNormal(v.normal);
    o.normalWS = normalWorld;

    return o;
};

float4 FragmentMeta(v2f i) : SV_TARGET
{
    // Main Function
    float4 albedo;
    float3 normalTS;
    float  metallic;
    float  smoothness;
    float  occlusion;
    float3 emission;
    SampleRepetitionless(
        i.uv, i.normalWS, i.positionWS, 0,
        albedo, normalTS, metallic, smoothness, occlusion, emission
    );

    FragmentCommonData data = ConstructFragData(i, albedo, normalTS, metallic, smoothness);

    UnityMetaInput o = (UnityMetaInput)0;

#ifdef EDITOR_VISUALIZATION
    o.Albedo = data.diffColor;
    o.VizUV = i.vizUV;
    o.LightCoord = i.lightCoord;
#else
    half roughness = SmoothnessToRoughness(smoothness);
    o.Albedo = data.diffColor + data.specColor * roughness * 0.5;
#endif
    o.SpecularColor = data.specColor;
    o.Emission = emission;

    return UnityMetaFragment(o);
}

#endif
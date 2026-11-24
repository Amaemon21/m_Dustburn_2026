#ifndef DUSTBORN_POI_SURFACE_META_INCLUDED
#define DUSTBORN_POI_SURFACE_META_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

struct PoiMetaVaryings
{
    Varyings meta;
    float3 positionWS : TEXCOORD3;
};

PoiMetaVaryings PoiVertexMeta(Attributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    PoiMetaVaryings output;
    output.meta = UniversalVertexMeta(input);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    return output;
}

half4 PoiFragmentMeta(PoiMetaVaryings input) : SV_Target
{
    half alpha = 1.0;
    half smoothness;
    half3 albedo;
    SamplePoiFinish(input.meta.uv, input.positionWS, albedo, smoothness);
    BRDFData brdfData;
    InitializeBRDFData(albedo, _Metallic, half3(0, 0, 0), smoothness, alpha, brdfData);
    MetaInput metaInput = (MetaInput)0;
    metaInput.Albedo = brdfData.diffuse + brdfData.specular * brdfData.roughness * 0.5;
    return UniversalFragmentMeta(input.meta, metaInput);
}

#endif

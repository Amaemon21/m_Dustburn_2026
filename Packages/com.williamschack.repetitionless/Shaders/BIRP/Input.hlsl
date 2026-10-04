#ifndef REPETITIONLESSINPUT_INCLUDED
#define REPETITIONLESSINPUT_INCLUDED

#include "UnityCG.cginc"

// Compatibility with URP/HDRP conventions
#define TEXTURE2D(tex) Texture2D tex
#define SAMPLER(tex) SamplerState tex
#define TEXTURE2D_ARRAY(tex) Texture2DArray tex
#define SAMPLE_TEXTURE2D(tex, ss, uv) tex.Sample(ss, uv)
#define SAMPLE_TEXTURE2D_ARRAY(tex, ss, uv, i) tex.Sample(ss, float3(uv, i))

// Material Properties
float _SurfaceTypeSetting;
float _UVSpace;
float _VertexColourBlendMode;
half  _DebuggingIndex;
float _LayersCount;
float4 _NoiseTexture_TexelSize;

// Textures
TEXTURE2D(_NoiseTexture);            SAMPLER(sampler_NoiseTexture);
TEXTURE2D(_PropertiesTexture);       SAMPLER(sampler_PropertiesTexture);
TEXTURE2D(_AssignedTexturesTexture); SAMPLER(sampler_AssignedTexturesTexture);
TEXTURE2D_ARRAY(_AVTextures);        SAMPLER(sampler_AVTextures);
TEXTURE2D_ARRAY(_NSOTextures);       SAMPLER(sampler_NSOTextures);
TEXTURE2D_ARRAY(_EMTextures);        SAMPLER(sampler_EMTextures);
TEXTURE2D_ARRAY(_BMTextures);        SAMPLER(sampler_BMTextures);

#ifdef REPETITIONLESS_LAYERED
TEXTURE2D(_TerrainHolesTexture);     SAMPLER(sampler_TerrainHolesTexture);
TEXTURE2D(_Control0);                SAMPLER(sampler_Control0);
TEXTURE2D(_Control1);                SAMPLER(sampler_Control1);
TEXTURE2D(_Control2);                SAMPLER(sampler_Control2);
TEXTURE2D(_Control3);                SAMPLER(sampler_Control3);
TEXTURE2D(_Control4);                SAMPLER(sampler_Control4);
TEXTURE2D(_Control5);                SAMPLER(sampler_Control5);
TEXTURE2D(_Control6);                SAMPLER(sampler_Control6);
TEXTURE2D(_Control7);                SAMPLER(sampler_Control7);

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLES)
    // GL doesn't support sperating the samplers from the texture object
    #undef TERRAIN_USE_SEPARATE_VERTEX_SAMPLER
#else
    #define TERRAIN_USE_SEPARATE_VERTEX_SAMPLER
#endif

#ifdef UNITY_INSTANCING_ENABLED
    #if defined(TERRAIN_USE_SEPARATE_VERTEX_SAMPLER)
        UNITY_DECLARE_TEX2D(_TerrainHeightmapTexture);
        UNITY_DECLARE_TEX2D(_TerrainNormalmapTexture);
        SamplerState sampler__TerrainNormalmapTexture;
        SamplerState vertex_linear_clamp_sampler;
    #else
        sampler2D _TerrainHeightmapTexture;
        sampler2D _TerrainNormalmapTexture;
    #endif

    float4    _TerrainHeightmapRecipSize;   // float4(1.0f/width, 1.0f/height, 1.0f/(width-1), 1.0f/(height-1))
    float4    _TerrainHeightmapScale;       // float4(hmScale.x, hmScale.y / (float)(kMaxHeight), hmScale.z, 0.0f)
#endif

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData) // float4(xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

void ClipHoles(float2 uv)
{
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r;
    clip(hole < 0.0005f ? -1 : 1);
}

void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
    float2 patchVertex = positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

    float4 uvscale = instanceData.z * _TerrainHeightmapRecipSize;
    float4 uvoffset = instanceData.xyxy * uvscale;
    uvoffset.xy += 0.5f * _TerrainHeightmapRecipSize.xy;
    float2 sampleCoords = (patchVertex.xy * uvscale.xy + uvoffset.xy);

    #if defined(TERRAIN_USE_SEPARATE_VERTEX_SAMPLER)
        float hm = UnpackHeightmap(_TerrainHeightmapTexture.SampleLevel(vertex_linear_clamp_sampler, sampleCoords, 0));
    #else
        float hm = UnpackHeightmap(tex2Dlod(_TerrainHeightmapTexture, float4(sampleCoords, 0, 0)));
    #endif

    positionOS.xz = (patchVertex.xy + instanceData.xy) * _TerrainHeightmapScale.xz * instanceData.z;  //(x + xBase) * hmScale.x * skipScale;
    positionOS.y = hm * _TerrainHeightmapScale.y;
    positionOS.w = 1.0f;

    uv.xy = (patchVertex.xy * uvscale.zw + uvoffset.zw);

    #if defined(TERRAIN_USE_SEPARATE_VERTEX_SAMPLER)
        float3 nor = _TerrainNormalmapTexture.SampleLevel(vertex_linear_clamp_sampler, sampleCoords, 0).xyz;
    #else
        float3 nor = tex2Dlod(_TerrainNormalmapTexture, float4(sampleCoords, 0, 0)).xyz;
    #endif
    normal = 2.0f * nor - 1.0f;
#endif
}

#endif
#endif
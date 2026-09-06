Shader "Repetitionless/URP/RepetitionlessLayeredTerrain"
{
    Properties
    {
        _SurfaceTypeSetting("Surface Type", Int) = 0
        _UVSpace("UV Space", Int) = 0
        _VertexColourBlendMode("Vertex Colour Blend Mode", Int) = 0
        _DebuggingIndex("Debugging Index", Int) = -1
        _LayersCount("Layers Count", Int) = 1

        [NoScaleOffset] _PropertiesTexture("Properties Texture", 2D) = "white" {}
        [NoScaleOffset] _AssignedTexturesTexture("Assigned Textures Texture", 2D) = "white" {}
        [NoScaleOffset] _AVTextures("AVTextures", 2DArray) = "white" {}   // Albedo, Variation
        [NoScaleOffset] _NSOTextures("NSOTextures", 2DArray) = "white" {} // Normal, Smoothness/Roughness, Occlussion
        [NoScaleOffset] _EMTextures("EMTextures", 2DArray) = "white" {}   // Emission, Metallic
        [NoScaleOffset] _BMTextures("BMTextures", 2DArray) = "white" {}   // Blend Mask
        [NoScaleOffset] _NoiseTexture("Noise Texture", 2D) = "white" {}

        _TerrainHolesTexture("Terran Holes", 2D) = "white" {}
        _Control0("Control 0", 2D) = "white" {}
        _Control1("Control 1", 2D) = "black" {}
        _Control2("Control 2", 2D) = "black" {}
        _Control3("Control 3", 2D) = "black" {}
        _Control4("Control 4", 2D) = "black" {}
        _Control5("Control 5", 2D) = "black" {}
        _Control6("Control 6", 2D) = "black" {}
        _Control7("Control 7", 2D) = "black" {}

        // Blending
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    HLSLINCLUDE
    #pragma multi_compile_fragment __ _ALPHATEST_ON

    #ifndef REPETITIONLESS_LAYERED
    #define REPETITIONLESS_LAYERED
    #endif
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "False"
            "Queue" = "Geometry-100"
            "TerrainCompatible" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex Vert
            #pragma fragment Frag

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _MAX_LAYERS_4 _MAX_LAYERS_8 _MAX_LAYERS_12 _MAX_LAYERS_16 _MAX_LAYERS_20 _MAX_LAYERS_24 _MAX_LAYERS_28 _MAX_LAYERS_32

            // URP Keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

#if UNITY_VERSION >= 202220
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
#endif

#if UNITY_VERSION >= 202320
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
#endif


#if UNITY_VERSION >= 600010
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#elif UNITY_VERSION >= 202220
            #pragma multi_compile _ _FORWARD_PLUS
#else
            #pragma multi_compile _ _CLUSTERED_RENDERING
#endif

#ifdef UNITY_PLATFORM_META_QUEST
            #pragma multi_compile _ META_QUEST_ORTHO_PROJ
            #pragma multi_compile _ META_QUEST_NO_SPOTLIGHTS_LIGHT_LOOP
#endif

            // Unity defined
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ DEBUG_DISPLAY

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

#if UNITY_VERSION >= 202300
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
#endif

#if UNITY_VERSION >= 600010
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#else
            #pragma multi_compile_fog
#endif

            #if UNITY_VERSION >= 600010 && USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
                #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #include "Input.hlsl"
            #include "TerrainPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Input.hlsl"
            #include "TerrainShadowCasterPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }

            HLSLPROGRAM
            #pragma target 4.5

            #pragma exclude_renderers gles3 glcore

            #pragma vertex Vert
            #pragma fragment Frag

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _MAX_LAYERS_4 _MAX_LAYERS_8 _MAX_LAYERS_12 _MAX_LAYERS_16 _MAX_LAYERS_20 _MAX_LAYERS_24 _MAX_LAYERS_28 _MAX_LAYERS_32

            // URP Keywords 
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

#if UNITY_VERSION >= 202220
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
#endif

#if UNITY_VERSION >= 202320
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
#endif

#if UNITY_VERSION >= 600010
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#elif UNITY_VERSION >= 202220
            #pragma multi_compile _ _FORWARD_PLUS
#else
            #pragma multi_compile _ _CLUSTERED_RENDERING
#endif

            // Unity defined
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

#if UNITY_VERSION >= 202300
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
#endif

            #if UNITY_VERSION >= 600010 && USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
                #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #define REPETITIONLESS_GBUFFER
            #include "Input.hlsl"
            #include "TerrainPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Input.hlsl"
            #include "TerrainDepthOnlyPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment DepthNormalOnlyFragment

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _MAX_LAYERS_4 _MAX_LAYERS_8 _MAX_LAYERS_12 _MAX_LAYERS_16 _MAX_LAYERS_20 _MAX_LAYERS_24 _MAX_LAYERS_28 _MAX_LAYERS_32

#if UNITY_VERSION >= 202220
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
#endif

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Input.hlsl"
            #include "TerrainDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #define SCENESELECTIONPASS
            #include "Input.hlsl"
            #include "TerrainDepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex TerrainVertexMeta
            #pragma fragment TerrainFragmentMeta

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _MAX_LAYERS_4 _MAX_LAYERS_8 _MAX_LAYERS_12 _MAX_LAYERS_16 _MAX_LAYERS_20 _MAX_LAYERS_24 _MAX_LAYERS_28 _MAX_LAYERS_32

            // Unity defined
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Input.hlsl"
            #include "TerrainMetaPass.hlsl"

            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    CustomEditor "Repetitionless.Editor.Inspectors.RepetitionlessMaterialEditorTerrain"
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
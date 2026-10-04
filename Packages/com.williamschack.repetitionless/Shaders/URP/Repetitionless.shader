Shader "Repetitionless/URP/Repetitionless"
{
    Properties
    {
        _SurfaceTypeSetting("Surface Type", Int) = 0
        _UVSpace("UV Space", Int) = 0
        _VertexColourBlendMode("Vertex Colour Blend Mode", Int) = 0
        _DebuggingIndex("Debugging Index", Int) = -1

        [NoScaleOffset] _PropertiesTexture("Properties Texture", 2D) = "white" {}
        [NoScaleOffset] _AssignedTexturesTexture("Assigned Textures Texture", 2D) = "white" {}
        [NoScaleOffset] _AVTextures("AVTextures", 2DArray) = "white" {}   // Albedo, Variation
        [NoScaleOffset] _NSOTextures("NSOTextures", 2DArray) = "white" {} // Normal, Smoothness/Roughness, Occlussion
        [NoScaleOffset] _EMTextures("EMTextures", 2DArray) = "white" {}   // Emission, Metallic
        [NoScaleOffset] _BMTextures("BMTextures", 2DArray) = "white" {}   // Blend Mask
        [NoScaleOffset] _NoiseTexture("Noise Texture", 2D) = "white" {}

        // Blending
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

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

            // URP Keywords 
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
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
            #pragma multi_compile _ META_QUEST_LIGHTUNROLL
            #pragma multi_compile _ META_QUEST_ORTHO_PROJ
            #pragma multi_compile _ META_QUEST_NO_SPOTLIGHTS_LIGHT_LOOP
#endif

            // Unity defined
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
      
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

#if UNITY_VERSION >= 202300
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
#endif

#if UNITY_VERSION >= 600010
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#else
            #pragma multi_compile_fog
#endif

            #include "Input.hlsl"
            #include "Passes.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            #pragma multi_compile_instancing

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #include "Input.hlsl"
            #include "ShadowCasterPass.hlsl"

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

            // URP Keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
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
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

#if UNITY_VERSION >= 202300
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
#endif

            #define REPETITIONLESS_GBUFFER
            #include "Input.hlsl"
            #include "Passes.hlsl"

            ENDHLSL
        }
        
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #pragma multi_compile_instancing

            #include "Input.hlsl"
            #include "DepthOnlyPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

#if UNITY_VERSION >= 202220
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
#endif

            #pragma multi_compile_instancing

            #include "Input.hlsl"
            #include "DepthNormalsPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM 
#if UNITY_VERSION >= 202220
            #pragma target 2.0
#else
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
#endif

            #pragma vertex VertexMeta
            #pragma fragment FragmentMetaLit

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF

            // Unity defined
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Input.hlsl"
            #include "MetaPass.hlsl"

            ENDHLSL
        }

        // UNITY_VERSION >= 202320
        Pass
        { 
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Input.hlsl"
            #include_with_pragmas "ObjectMotionVectors.hlsl"
            ENDHLSL
        }

        // UNITY_VERSION >= 600000
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode" = "XRMotionVectors" }
            ColorMask RGBA

            Stencil
            {
                WriteMask 1
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ APPLICATION_SPACE_WARP_MOTION_TRANSPARENT
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #define APPLICATION_SPACE_WARP_MOTION 1

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Input.hlsl"
            #include_with_pragmas "ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "Repetitionless.Editor.Inspectors.RepetitionlessMaterialEditorMaster"
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
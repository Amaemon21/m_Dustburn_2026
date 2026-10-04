Shader "Repetitionless/BIRP/Repetitionless"
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
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    HLSLINCLUDE
    #ifndef REPETITIONLESS_BIRP
    #define REPETITIONLESS_BIRP
    #endif
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]

            HLSLPROGRAM
            #include "HLSLSupport.cginc"
            #include "UnityShaderVariables.cginc"

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

            // Unity defined
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #include "Input.hlsl"
            #include "Passes.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "FORWARD_DELTA"
            Tags { "LightMode" = "ForwardAdd" }
            Blend [_SrcBlend] One
            Fog { Color (0,0,0,0) }
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #include "HLSLSupport.cginc"
            #include "UnityShaderVariables.cginc"

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

            // Unity defined
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #define ADD_PASS
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

            HLSLPROGRAM
            #include "HLSLSupport.cginc"
            #include "UnityShaderVariables.cginc"

            #pragma target 3.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #include "Input.hlsl"
            #include "ShadowCasterPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DEFERRED"
            Tags { "LightMode" = "Deferred" }

            HLSLPROGRAM
            #include "HLSLSupport.cginc"
            #include "UnityShaderVariables.cginc"

            #pragma target 3.0
            #pragma exclude_renderers nomrt

            #pragma vertex Vert
            #pragma fragment FragDeferred

            // Material Keywords
            #pragma shader_feature_local _ _REPETITIONLESS_DISTANCE_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_MATERIAL_BLEND
            #pragma shader_feature_local _ _REPETITIONLESS_TRIPLANAR
            #pragma shader_feature_local _ _REPETITIONLESS_NOISE_TEXTURE
            #pragma shader_feature_local _ _REPETITIONLESS_VARIATION

            #pragma shader_feature_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF

            // Unity defined
            #pragma multi_compile_prepassfinal
            #pragma multi_compile_instancing

#if UNITY_VERSION >= 202220
            #pragma multi_compile _ LOD_FADE_CROSSFADE
#endif

            #define DEFERRED_PASS
            #include "Input.hlsl"
            #include "Passes.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "META"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #include "HLSLSupport.cginc"
            #include "UnityShaderVariables.cginc"

            #pragma vertex VertexMeta
            #pragma fragment FragmentMeta

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
    }

    CustomEditor "Repetitionless.Editor.Inspectors.RepetitionlessMaterialEditorMaster"
    FallBack "Diffuse"
}
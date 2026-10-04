#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using Repetitionless.Runtime.Variables;
using Repetitionless.Runtime.Utilities;
using Repetitionless.Editor.Data;

namespace Repetitionless.Editor.Materials
{
    internal static class RepetitionlessMaterialUtilities
    {
        private const string NOISE_TEXTURE_PROP_NAME = "_NoiseTexture";
        private const string SURFACE_TYPE_PROP_NAME = "_SurfaceTypeSetting";

        // Enables "prefix{(int)value}"
        // Disables all other enum values
        // If enumReference is set, enumIndex must be set. The enum will then have an int set beside it so it can save properly
        public static void SetEnumKeywordInt<T>(Material mat, string keywordPrefix, T value, string enumReference = "", int enumIndex = 0) where T : Enum
        {
            int intValue = Convert.ToInt32(value);

            EditorApplication.delayCall += () => {
                foreach (T currentEnumValue in Enum.GetValues(typeof(T))) {
                    int currentEnumIntValue = Convert.ToInt32(currentEnumValue);

                    string keyword = $"{keywordPrefix}{currentEnumIntValue}";

                    // Disable others
                    if (currentEnumIntValue != intValue) {
                        mat.DisableKeyword(keyword);
                        continue;
                    }

                    mat.EnableKeyword(keyword);

                    if (enumReference != "")
                        mat.SetInt(enumReference, enumIndex);
                }

                EditorUtility.SetDirty(mat);
            };
        }

        // Enables "prefix{(string)value}"
        // Disables all other enum values
        public static void SetEnumKeywordString<T>(Material mat, string keywordPrefix, T value) where T : Enum
        {
            string stringValue = value.ToString().ToUpper();

            EditorApplication.delayCall += () => {
                foreach (T currentEnumValue in Enum.GetValues(typeof(T))) {
                    string currentEnumStringValue = currentEnumValue.ToString().ToUpper();

                    string keyword = $"{keywordPrefix}{currentEnumStringValue}";

                    // Disable others
                    if (currentEnumStringValue != stringValue) {
                        mat.DisableKeyword(keyword);
                        continue;
                    }

                    mat.EnableKeyword(keyword);
                }

                EditorUtility.SetDirty(mat);
            };
        }

        public static void SetBoolKeyword(Material mat, string keyword, bool enabled)
        {
            // Delay call to prevent recursive warnings, this will take a while if variant not cached
            EditorApplication.delayCall += () => {
                // Using a keyword variable with SetKeyword sometimes gives errors
                if (enabled) mat.EnableKeyword(keyword);
                else         mat.DisableKeyword(keyword);

                mat.SetInt(keyword, enabled ? 1 : 0); // Required to save for some reason
                EditorUtility.SetDirty(mat);
            };
        }

        public static void UpdateNoiseQualityTexture(Material mat, ENoiseQuality noiseQuality)
        {
            switch (noiseQuality) {
                case ENoiseQuality.High:
                    mat.SetTexture(NOISE_TEXTURE_PROP_NAME, null);
                    break;
                case ENoiseQuality.Medium: {
                    Texture2D texture = Resources.Load<Texture2D>(Constants.NOISE_TEXTURE_NAME_4K);
                    mat.SetTexture(NOISE_TEXTURE_PROP_NAME, texture);
                    break;
                } case ENoiseQuality.Low: {
                    Texture2D texture = Resources.Load<Texture2D>(Constants.NOISE_TEXTURE_NAME_1K);
                    mat.SetTexture(NOISE_TEXTURE_PROP_NAME, texture);
                    break;
                }
            }
        }
        public static void UpdateDistanceBlendKeyword(Material mat, RepetitionlessMaterialDataSO data)
        {
            // If its enabled at all, enable the keyword, otherwise disable
            bool enabled = false;
            foreach (RepetitionlessLayerData currentLayerData in data.Data) {
                if (currentLayerData.DistanceBlendEnabled) {
                    enabled = true;
                    break;                    
                }
            }

            SetBoolKeyword(mat, Constants.DISTANCE_BLEND_KEYWORD, enabled);
        }
        public static void UpdateMaterialBlendKeyword(Material mat, RepetitionlessMaterialDataSO data)
        {
            // If its enabled at all, enable the keyword, otherwise disable
            bool enabled = false;
            foreach (RepetitionlessLayerData currentLayerData in data.Data) {
                if (currentLayerData.MaterialBlendEnabled) {
                    enabled = true;
                    break;                    
                }
            }

            SetBoolKeyword(mat, Constants.MATERIAL_BLEND_KEYWORD, enabled);
        }

        public static void UpdateVariationKeyword(Material mat, int maxLayers, RepetitionlessMaterialDataSO data)
        {
            // If its enabled at all, enable the keyword, otherwise disable
            bool enabled = false;
            for (int layer = 0; layer < maxLayers; layer++) {
                for (int section = 0; section < 3; section++) {
                    RepetitionlessMaterialData materialData = data.GetMaterialData(layer, section);
                    if (materialData.VariationEnabled) {
                        enabled = true;
                        break;
                    }
                }

                if (enabled)
                    break;
            }

            SetBoolKeyword(mat, Constants.VARIATION_KEYWORD, enabled);
        }

        public static void UpdateMaxLayersKeyword(Material mat, EMaxLayers value)
        {
            // If in HDRP, the shader graphs have the max layers set weird and require the int to be set aswell to save
            ERenderPipeline renderPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();

            if (renderPipeline == ERenderPipeline.HDRP) {
                int enumIndex = Convert.ToInt32(value) / 4 - 1;
                SetEnumKeywordInt(mat, Constants.MAX_LAYERS_KEYWORD_PREFIX, value, "_MAX", enumIndex);
            } else
                SetEnumKeywordInt(mat, Constants.MAX_LAYERS_KEYWORD_PREFIX, value);
        }

        public static void SetNoiseQuality(Material mat, ENoiseQuality noiseQuality)
        {
            SetBoolKeyword(mat, Constants.NOISE_TEXTURE_KEYWORD, noiseQuality != ENoiseQuality.High);
            UpdateNoiseQualityTexture(mat, noiseQuality);
        }

        public static void SetTriplanarEnabled(Material mat, bool enabled)
        {
            SetBoolKeyword(mat, Constants.TRIPLANAR_KEYWORD, enabled);
        }

        public static void SetSpecularHighlightsEnabled(Material mat, bool enabled)
        {
            SetBoolKeyword(mat, Constants.SPECULAR_HIGHLIGHTS_OFF_KEYWORD, !enabled);
        }

        public static void SetEnvironmentReflectionsEnabled(Material mat, bool enabled)
        {
            SetBoolKeyword(mat, Constants.ENVIRONMENT_REFLECTIONS_OFF_KEYWORD, !enabled);
        }

        public static void SetSurface(Material mat, ESurfaceType surfaceType, ERenderPipeline pipeline)
        {
            mat.SetFloat(SURFACE_TYPE_PROP_NAME, (int)surfaceType);

            switch (surfaceType) {
                case ESurfaceType.Opaque:
                    mat.renderQueue = (int)RenderQueue.Geometry;
                    mat.SetOverrideTag("RenderType", "Opaque");

                    if (pipeline == ERenderPipeline.HDRP) {
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");

                        mat.SetInt("_SurfaceType", 0);
                        mat.SetInt("_AlphaSrcBlend", (int)BlendMode.One);
                        mat.SetInt("_AlphaDstBlend", (int)BlendMode.Zero);
                        mat.SetInt("_AlphaCutoffEnable", 0);
                        mat.SetInt("_ZWrite", 1);

                        mat.renderQueue = 2475;
                    } else {
                        mat.DisableKeyword("_BUILTIN_SURFACE_TYPE_TRANSPARENT");

                        mat.SetInt("_BUILTIN_Surface", 0);
                        mat.SetInt("_BUILTIN_SrcBlend", (int)BlendMode.One);
                        mat.SetInt("_BUILTIN_DstBlend", (int)BlendMode.Zero);
                        mat.SetInt("_BUILTIN_ZWrite", 1);
                    }
                    break;
                case ESurfaceType.Cutout:
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    mat.SetOverrideTag("RenderType", "TransparentCutout");

                    if (pipeline == ERenderPipeline.HDRP) {
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

                        mat.SetInt("_SurfaceType", 0);
                        mat.SetInt("_AlphaSrcBlend", (int)BlendMode.One);
                        mat.SetInt("_AlphaDstBlend", (int)BlendMode.Zero);
                        mat.SetInt("_AlphaCutoffEnable", 1);
                        mat.SetInt("_ZWrite", 1);
                    } else {
                        mat.DisableKeyword("_BUILTIN_SURFACE_TYPE_TRANSPARENT");
                        mat.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");

                        mat.SetInt("_BUILTIN_Surface", 0);
                        mat.SetInt("_BUILTIN_SrcBlend", (int)BlendMode.One);
                        mat.SetInt("_BUILTIN_DstBlend", (int)BlendMode.Zero);
                        mat.SetInt("_BUILTIN_ZWrite", 1); 
                    }
                    break;
                case ESurfaceType.Transparent:
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    mat.SetOverrideTag("RenderType", "Transparent");

                    if (pipeline == ERenderPipeline.HDRP) {
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");

                        mat.SetInt("_SurfaceType", 1);
                        mat.SetInt("_AlphaSrcBlend", (int)BlendMode.One);
                        mat.SetInt("_AlphaDstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_AlphaCutoffEnable", 0);
                        mat.SetInt("_ZWrite", 0);
                    } else {
                        mat.EnableKeyword("_BUILTIN_SURFACE_TYPE_TRANSPARENT");

                        mat.SetInt("_BUILTIN_Surface", 1);
                        mat.SetInt("_BUILTIN_SrcBlend", (int)BlendMode.SrcAlpha);
                        mat.SetInt("_BUILTIN_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_BUILTIN_ZWrite", 0);
                    }
                    break;
            }
        }

        public static void SetSurface(Material mat, ESurfaceType surfaceType)
        {
            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            SetSurface(mat, surfaceType, currentPipeline);
        }
        
        // Should move more utility functions here from the inspector but they arent globally needed so for now this is it
    }
}
#endif
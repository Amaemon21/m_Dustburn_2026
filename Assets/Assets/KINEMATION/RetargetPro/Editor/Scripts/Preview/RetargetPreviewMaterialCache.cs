// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KINEMATION.RetargetPro.Editor.Scripts.Preview
{
    internal sealed class RetargetPreviewMaterialCache
    {
        private static readonly string[] BaseColorPropertyNames =
        {
            "_BaseColor",
            "_Color",
            "_MainColor",
            "_TintColor",
            "_UnlitColor"
        };

        private static readonly string[] BaseTexturePropertyNames =
        {
            "_BaseColorMap",
            "_BaseMap",
            "_MainTex",
            "_UnlitColorMap"
        };

        private static readonly string[] CutoffPropertyNames =
        {
            "_AlphaCutoff",
            "_AlphaClipThreshold",
            "_Cutoff"
        };

        private static readonly string[] AlphaClipTogglePropertyNames =
        {
            "_AlphaClip",
            "_AlphaCutoffEnable"
        };

        private static readonly string[] CullPropertyNames =
        {
            "_Cull",
            "_CullMode",
            "_RenderFace"
        };

        private static readonly string[] SurfacePropertyNames =
        {
            "_Surface",
            "_SurfaceType"
        };

        private static readonly string[] UniversalTemplateShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit"
        };

        private static readonly string[] HighDefinitionTemplateShaderNames =
        {
            "HDRP/Lit",
            "HDRP/Unlit"
        };

        private static readonly string[] BuiltInTemplateShaderNames =
        {
            "Standard",
            "Autodesk Interactive",
            "Unlit/Texture",
            "Unlit/Color"
        };

        private readonly Dictionary<Material, Material> _replacementMaterials = new Dictionary<Material, Material>();
        private Material _defaultMaterial;
        private Material _templateMaterial;

        public void EnsureSupportedRendererMaterials(GameObject root, string materialNamePrefix)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                Material[] sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    Material fallback = GetDefaultMaterial();
                    if (fallback != null)
                    {
                        renderer.sharedMaterial = fallback;
                    }
                    continue;
                }

                var previewMaterials = new Material[sourceMaterials.Length];
                bool changed = false;
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    Material sourceMaterial = sourceMaterials[i];
                    if (!NeedsReplacement(sourceMaterial))
                    {
                        previewMaterials[i] = sourceMaterial;
                        continue;
                    }

                    previewMaterials[i] = GetReplacement(sourceMaterial, materialNamePrefix) ?? sourceMaterial;
                    changed |= previewMaterials[i] != sourceMaterial;
                }

                if (changed)
                {
                    renderer.sharedMaterials = previewMaterials;
                }
            }
        }

        public void Cleanup()
        {
            var destroyedMaterials = new HashSet<Material>();
            foreach (KeyValuePair<Material, Material> pair in _replacementMaterials)
            {
                if (pair.Value == null || !destroyedMaterials.Add(pair.Value))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(pair.Value);
            }

            _replacementMaterials.Clear();

            if (_defaultMaterial != null && destroyedMaterials.Add(_defaultMaterial))
            {
                UnityEngine.Object.DestroyImmediate(_defaultMaterial);
            }

            _defaultMaterial = null;

            if (_templateMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_templateMaterial);
                _templateMaterial = null;
            }
        }

        private Material GetReplacement(Material source, string materialNamePrefix)
        {
            if (source == null)
            {
                return GetDefaultMaterial();
            }

            if (_replacementMaterials.TryGetValue(source, out Material cached) && cached != null)
            {
                return cached;
            }

            Material replacement = CreateReplacementMaterial(source, materialNamePrefix) ?? GetDefaultMaterial();
            _replacementMaterials[source] = replacement;
            return replacement;
        }

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null)
            {
                return _defaultMaterial;
            }

            Material template = GetTemplateMaterial();
            if (template == null)
            {
                return null;
            }

            _defaultMaterial = new Material(template)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "RetargetPreviewDefaultMaterial"
            };

            ApplyOpaqueDefaults(_defaultMaterial);
            SetColor(_defaultMaterial, new Color(0.72f, 0.72f, 0.72f, 1f));
            SetBaseTexture(_defaultMaterial, null, Vector2.one, Vector2.zero);

            return _defaultMaterial;
        }

        private Material CreateReplacementMaterial(Material source, string materialNamePrefix)
        {
            Material template = GetTemplateMaterial();
            if (template == null)
            {
                return null;
            }

            string materialName = string.IsNullOrEmpty(materialNamePrefix)
                ? $"{source.name} Preview"
                : $"{materialNamePrefix} {source.name}";

            var material = new Material(template)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = materialName
            };

            ApplyOpaqueDefaults(material);

            Color baseColor = GetFirstColor(source, BaseColorPropertyNames, Color.white);
            Texture baseTexture = GetFirstTexture(source, BaseTexturePropertyNames, out string texturePropertyName);
            Vector2 textureScale = texturePropertyName != null ? source.GetTextureScale(texturePropertyName) : Vector2.one;
            Vector2 textureOffset = texturePropertyName != null ? source.GetTextureOffset(texturePropertyName) : Vector2.zero;
            float alphaCutoff = GetFirstFloat(source, CutoffPropertyNames, 0f);
            bool alphaClipped = alphaCutoff > 0.0001f ||
                               source.IsKeywordEnabled("_ALPHATEST_ON") ||
                               source.IsKeywordEnabled("_ALPHACLIP_ON");
            bool transparent = source.renderQueue >= (int)RenderQueue.Transparent ||
                               source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                               source.IsKeywordEnabled("_ALPHABLEND_ON") ||
                               source.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                               source.GetTag("RenderType", false, string.Empty) == "Transparent" ||
                               (!alphaClipped && baseColor.a < 0.999f);

            SetColor(material, baseColor);
            SetBaseTexture(material, baseTexture, textureScale, textureOffset);
            SetFloat(material, CutoffPropertyNames, alphaCutoff);
            SetFloat(material, AlphaClipTogglePropertyNames, alphaClipped ? 1f : 0f);
            SetCullMode(material, GetCullMode(source));
            ApplySurfaceMode(material, transparent, alphaClipped);

            return material;
        }

        private Material GetTemplateMaterial()
        {
            if (_templateMaterial != null)
            {
                return _templateMaterial;
            }

            foreach (string shaderName in GetCandidateShaderNames())
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null || !shader.isSupported || shader.name == "Hidden/InternalErrorShader")
                {
                    continue;
                }

                _templateMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "RetargetPreviewFallbackTemplate"
                };
                ApplyOpaqueDefaults(_templateMaterial);
                return _templateMaterial;
            }

            Material builtinDefault = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (!NeedsReplacement(builtinDefault))
            {
                _templateMaterial = new Material(builtinDefault)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "RetargetPreviewFallbackTemplate"
                };
                ApplyOpaqueDefaults(_templateMaterial);
            }

            return _templateMaterial;
        }

        private static IEnumerable<string> GetCandidateShaderNames()
        {
            string pipelineTypeName = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.GetType().FullName
                : string.Empty;

            if (!string.IsNullOrEmpty(pipelineTypeName))
            {
                if (pipelineTypeName.IndexOf("HDRenderPipelineAsset", StringComparison.Ordinal) >= 0 ||
                    pipelineTypeName.IndexOf("HighDefinitionRenderPipelineAsset", StringComparison.Ordinal) >= 0)
                {
                    foreach (string shaderName in HighDefinitionTemplateShaderNames)
                    {
                        yield return shaderName;
                    }
                }
                else if (pipelineTypeName.IndexOf("UniversalRenderPipelineAsset", StringComparison.Ordinal) >= 0)
                {
                    foreach (string shaderName in UniversalTemplateShaderNames)
                    {
                        yield return shaderName;
                    }
                }
            }

            foreach (string shaderName in BuiltInTemplateShaderNames)
            {
                yield return shaderName;
            }
        }

        private static Color GetFirstColor(Material material, IEnumerable<string> propertyNames, Color fallback)
        {
            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    return material.GetColor(propertyName);
                }
            }

            return fallback;
        }

        private static Texture GetFirstTexture(Material material, IEnumerable<string> propertyNames, out string propertyName)
        {
            propertyName = null;

            foreach (string candidate in propertyNames)
            {
                if (!material.HasProperty(candidate))
                {
                    continue;
                }

                propertyName = candidate;
                Texture texture = material.GetTexture(candidate);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static float GetFirstFloat(Material material, IEnumerable<string> propertyNames, float fallback)
        {
            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    return material.GetFloat(propertyName);
                }
            }

            return fallback;
        }

        private static void ApplyOpaqueDefaults(Material material)
        {
            if (material == null)
            {
                return;
            }

            ApplySurfaceMode(material, false, false);
            SetCullMode(material, (float)CullMode.Back);
        }

        private static void ApplySurfaceMode(Material material, bool transparent, bool alphaClipped)
        {
            if (material == null)
            {
                return;
            }

            SetFloat(material, SurfacePropertyNames, transparent ? 1f : 0f);
            SetFloat(material, AlphaClipTogglePropertyNames, alphaClipped ? 1f : 0f);
            SetFloat(material, CutoffPropertyNames, alphaClipped ? Mathf.Max(GetFirstFloat(material, CutoffPropertyNames, 0.5f), 0.0001f) : 0f);
            SetFloat(material, new[] { "_SrcBlend" }, transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            SetFloat(material, new[] { "_DstBlend" }, transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            SetFloat(material, new[] { "_ZWrite" }, transparent ? 0f : 1f);

            material.renderQueue = transparent ? (int)RenderQueue.Transparent :
                alphaClipped ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", transparent ? "Transparent" :
                alphaClipped ? "TransparentCutout" : "Opaque");

            SetKeyword(material, "_ALPHATEST_ON", alphaClipped);
            SetKeyword(material, "_ALPHACLIP_ON", alphaClipped);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(material, "_ALPHABLEND_ON", transparent);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
        }

        private static void SetColor(Material material, Color color)
        {
            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_MainColor", color);
            SetColorIfPresent(material, "_TintColor", color);
            SetColorIfPresent(material, "_UnlitColor", color);
        }

        private static void SetBaseTexture(Material material, Texture texture, Vector2 scale, Vector2 offset)
        {
            SetTextureIfPresent(material, "_BaseColorMap", texture, scale, offset);
            SetTextureIfPresent(material, "_BaseMap", texture, scale, offset);
            SetTextureIfPresent(material, "_MainTex", texture, scale, offset);
            SetTextureIfPresent(material, "_UnlitColorMap", texture, scale, offset);
        }

        private static void SetTextureIfPresent(Material material, string propertyName, Texture texture, Vector2 scale,
            Vector2 offset)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            material.SetTexture(propertyName, texture);
            material.SetTextureScale(propertyName, scale);
            material.SetTextureOffset(propertyName, offset);
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloat(Material material, IEnumerable<string> propertyNames, float value)
        {
            if (material == null)
            {
                return;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    material.SetFloat(propertyName, value);
                }
            }
        }

        private static void SetCullMode(Material material, float cullMode)
        {
            SetFloat(material, CullPropertyNames, cullMode);

            if (material != null && material.HasProperty("_DoubleSidedEnable"))
            {
                material.SetFloat("_DoubleSidedEnable", Mathf.Approximately(cullMode, (float)CullMode.Off) ? 1f : 0f);
            }
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (material == null || string.IsNullOrEmpty(keyword))
            {
                return;
            }

            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static bool NeedsReplacement(Material material)
        {
            if (material == null)
            {
                return true;
            }

            Shader shader = material.shader;
            if (shader == null)
            {
                return true;
            }

            return !shader.isSupported || shader.name == "Hidden/InternalErrorShader";
        }

        private static float GetCullMode(Material material)
        {
            if (material.HasProperty("_DoubleSidedEnable") && material.GetFloat("_DoubleSidedEnable") > 0.5f)
            {
                return (float)CullMode.Off;
            }

            foreach (string propertyName in CullPropertyNames)
            {
                if (!material.HasProperty(propertyName))
                {
                    continue;
                }

                float value = material.GetFloat(propertyName);
                if (value >= 0f && value <= 2f)
                {
                    return value;
                }
            }

            return (float)CullMode.Back;
        }
    }
}
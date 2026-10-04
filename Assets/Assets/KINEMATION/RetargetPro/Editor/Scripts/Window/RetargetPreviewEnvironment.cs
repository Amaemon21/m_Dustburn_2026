// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    internal sealed class RetargetPreviewEnvironment
    {
        private const float DefaultGridExtent = 8f;
        private const float DefaultGridStep = 1f;
        private const float DefaultPreviewGridExtent = 48f;
        private const float MinPreviewGridExtent = 32f;
        private const float MaxPreviewGridExtent = 1200f;
        private const float BoundsGridExtentFactor = 8f;
        private const float CameraGridExtentFactor = 12f;
        private const string PreviewGridShaderName = "KINEMATION/Retarget Pro/Preview Grid";
        private const string PreviewGridMaterialPath =
            "Assets/KINEMATION/RetargetPro/Editor/Materials/M_RetargetPreviewGrid.mat";

        private static readonly Color DefaultPreviewBackground = new Color(0.75f, 0.72f, 0.74f, 1f);
        private static readonly Color DefaultGroundColor = new Color(0.5f, 0.47f, 0.5f, 1f);
        private static readonly Color DefaultGridColor = new Color(0.69f, 0.66f, 0.69f, 1f);

        private GameObject _groundObject;
        private GameObject _gridObject;

        private Mesh _groundMesh;
        private Mesh _gridMesh;
        private Material _groundMaterial;
        private Material _gridMaterial;
        private Material _previewGridMaterialTemplate;
        private float _gridExtent = DefaultGridExtent;
        private float _gridStep = DefaultGridStep;

        public float GridExtent => _gridExtent;

        public float GetDefaultExtentForCamera(float cameraDistance)
        {
            return ClampPreviewGridExtent(Mathf.Max(DefaultPreviewGridExtent,
                Mathf.Ceil(cameraDistance * CameraGridExtentFactor)));
        }

        public float GetExtentForBounds(float boundsRadius, float cameraDistance)
        {
            return ClampPreviewGridExtent(Mathf.Ceil(Mathf.Max(boundsRadius * BoundsGridExtentFactor,
                cameraDistance * CameraGridExtentFactor)));
        }

        private static float ClampPreviewGridExtent(float gridExtent)
        {
            return Mathf.Clamp(gridExtent, MinPreviewGridExtent, MaxPreviewGridExtent);
        }

        public static float SnapDownToChunkMagnitude(float value, float chunkSize, float minMagnitude = 0f)
        {
            if (chunkSize <= 0f)
            {
                return value;
            }

            float snappedMagnitude = Mathf.Floor(Mathf.Abs(value) / chunkSize) * chunkSize;
            if (minMagnitude > 0f && Mathf.Abs(value) > 0f)
            {
                snappedMagnitude = Mathf.Max(minMagnitude, snappedMagnitude);
            }

            return Mathf.Sign(value) * snappedMagnitude;
        }

        public void EnsureResources(Scene previewScene, Vector3 previewPivot)
        {
            EnsureEnvironmentMeshResources();
            EnsureEnvironmentObjects(previewScene, previewPivot);
        }

        public void UpdateSizing(float desiredGridExtent, float desiredGridStep, Vector3 previewPivot)
        {
            desiredGridExtent = Mathf.Max(0.001f, desiredGridExtent);
            desiredGridStep = Mathf.Max(0.001f, desiredGridStep);

            bool extentChanged = !Mathf.Approximately(desiredGridExtent, _gridExtent);
            bool stepChanged = !Mathf.Approximately(desiredGridStep, _gridStep);

            if (!extentChanged && !stepChanged)
            {
                UpdateEnvironmentTransforms(previewPivot);
                UpdateProceduralGridMaterialProperties();
                return;
            }

            _gridExtent = desiredGridExtent;
            _gridStep = desiredGridStep;

            if (!IsProceduralGridMaterial(_groundMaterial))
            {
                BuildGridMesh(_gridExtent, _gridStep);
            }

            UpdateEnvironmentTransforms(previewPivot);
            UpdateProceduralGridMaterialProperties();
        }

        public Color GetBackgroundColor()
        {
            if (IsProceduralGridMaterial(_groundMaterial) && _groundMaterial.HasProperty("_FogColor"))
            {
                return _groundMaterial.GetColor("_FogColor");
            }

            return GetConfiguredGridColor("_FogColor", DefaultPreviewBackground);
        }

        public float GetConfiguredChunkSize()
        {
            return Mathf.Max(0.001f, GetConfiguredGridFloat("_ChunkSize", 1f));
        }

        public float GetConfiguredSubChunkStep()
        {
            float chunkSize = GetConfiguredChunkSize();
            int subChunkCount = Mathf.Max(1, GetConfiguredGridInt("_SubChunkCount", 10));
            return chunkSize / subChunkCount;
        }

        public float GetConfiguredFogStart()
        {
            return Mathf.Max(0f, GetConfiguredGridFloat("_FadeStart", 20f));
        }

        public float GetConfiguredFogEnd()
        {
            return Mathf.Max(GetConfiguredFogStart() + 0.01f, GetConfiguredGridFloat("_FadeEnd", 60f));
        }

        public void CleanupObjects()
        {
            DestroyGameObject(ref _groundObject);
            DestroyGameObject(ref _gridObject);
        }

        public void CleanupResources()
        {
            CleanupObjects();

            if (_groundMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_groundMesh);
                _groundMesh = null;
            }

            if (_gridMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_gridMesh);
                _gridMesh = null;
            }

            DestroyRuntimeMaterial(ref _groundMaterial);
            DestroyRuntimeMaterial(ref _gridMaterial);
            _previewGridMaterialTemplate = null;
        }

        private void EnsureEnvironmentMeshResources()
        {
            ValidateEnvironmentMaterials();

            if (_groundMesh == null)
            {
                BuildGroundMesh();
            }

            if (_gridMesh == null)
            {
                BuildGridMesh(_gridExtent, _gridStep);
            }

            Shader coloredShader = Shader.Find("Hidden/Internal-Colored");
            Material previewGridTemplate = LoadPreviewGridMaterialTemplate();

            if (CanUseConfiguredPreviewGridMaterial(previewGridTemplate))
            {
                EnsureProceduralGridMaterialInstance(previewGridTemplate);
            }
            else
            {
                Color groundColor = GetConfiguredGridColor("_BaseColor", DefaultGroundColor);

                if (_groundMaterial != null && HasProceduralGridShader(_groundMaterial))
                {
                    DestroyRuntimeMaterial(ref _groundMaterial);
                }

                if (_groundMaterial == null)
                {
                    _groundMaterial = coloredShader != null
                        ? CreateColoredMaterial(coloredShader, groundColor, true)
                        : CreateFallbackMaterial(groundColor);
                }
                else
                {
                    ApplyMaterialColor(_groundMaterial, groundColor);
                }
            }

            if (_gridMaterial == null)
            {
                Color gridColor = GetConfiguredGridColor("_MinorLineColor", DefaultGridColor);
                _gridMaterial = coloredShader != null
                    ? CreateColoredMaterial(coloredShader, gridColor, false)
                    : null;

                if (_gridMaterial == null)
                {
                    _gridMaterial = CreateFallbackMaterial(gridColor);
                }
            }
            else
            {
                ApplyMaterialColor(_gridMaterial, GetConfiguredGridColor("_MinorLineColor", DefaultGridColor));
            }

            UpdateProceduralGridMaterialProperties();
        }

        private void ValidateEnvironmentMaterials()
        {
            if (_groundMaterial != null && NeedsMaterialRebuild(_groundMaterial))
            {
                DestroyRuntimeMaterial(ref _groundMaterial);
            }

            if (_gridMaterial != null && NeedsMaterialRebuild(_gridMaterial))
            {
                DestroyRuntimeMaterial(ref _gridMaterial);
            }
        }

        private void EnsureEnvironmentObjects(Scene previewScene, Vector3 previewPivot)
        {
            if (!previewScene.IsValid() || _groundMesh == null || _groundMaterial == null)
            {
                return;
            }

            if (_groundObject == null || _groundObject.scene != previewScene)
            {
                DestroyGameObject(ref _groundObject);
                _groundObject = CreateHiddenGameObject("RetargetPreviewGround", previewScene);
                if (_groundObject != null)
                {
                    _groundObject.AddComponent<MeshFilter>();
                    _groundObject.AddComponent<MeshRenderer>();
                }
            }

            if (_groundObject != null)
            {
                MeshFilter filter = _groundObject.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = _groundMesh;
                }

                MeshRenderer renderer = _groundObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = _groundMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            bool useFallbackLineGrid = !IsProceduralGridMaterial(_groundMaterial);
            if (_gridMesh != null && _gridMaterial != null)
            {
                if (_gridObject == null || _gridObject.scene != previewScene)
                {
                    DestroyGameObject(ref _gridObject);
                    _gridObject = CreateHiddenGameObject("RetargetPreviewGrid", previewScene);
                    if (_gridObject != null)
                    {
                        _gridObject.AddComponent<MeshFilter>();
                        _gridObject.AddComponent<MeshRenderer>();
                    }
                }

                if (_gridObject != null)
                {
                    MeshFilter filter = _gridObject.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        filter.sharedMesh = _gridMesh;
                    }

                    MeshRenderer renderer = _gridObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = _gridMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.enabled = useFallbackLineGrid;
                    }
                }
            }
            else if (_gridObject != null)
            {
                DestroyGameObject(ref _gridObject);
            }

            UpdateEnvironmentTransforms(previewPivot);
        }

        private void BuildGroundMesh()
        {
            if (_groundMesh != null)
            {
                return;
            }

            _groundMesh = new Mesh
            {
                name = "RetargetPreviewGroundMesh",
                hideFlags = HideFlags.HideAndDontSave
            };

            _groundMesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            });

            _groundMesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0);
            _groundMesh.RecalculateBounds();
        }

        private void BuildGridMesh(float extent, float step)
        {
            if (extent <= 0f || step <= 0f)
            {
                return;
            }

            if (_gridMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_gridMesh);
            }

            _gridMesh = new Mesh
            {
                name = "RetargetPreviewGridMesh",
                hideFlags = HideFlags.HideAndDontSave
            };

            var vertices = new List<Vector3>();
            var indices = new List<int>();

            int lineLimit = Mathf.RoundToInt(extent / step);
            int vertexIndex = 0;
            float y = 0.001f;

            for (int i = -lineLimit; i <= lineLimit; i++)
            {
                float coordinate = i * step;

                vertices.Add(new Vector3(coordinate, y, -extent));
                vertices.Add(new Vector3(coordinate, y, extent));
                indices.Add(vertexIndex++);
                indices.Add(vertexIndex++);

                vertices.Add(new Vector3(-extent, y, coordinate));
                vertices.Add(new Vector3(extent, y, coordinate));
                indices.Add(vertexIndex++);
                indices.Add(vertexIndex++);
            }

            _gridMesh.SetVertices(vertices);
            _gridMesh.SetIndices(indices, MeshTopology.Lines, 0);
            _gridMesh.RecalculateBounds();

            if (_gridObject != null)
            {
                MeshFilter filter = _gridObject.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = _gridMesh;
                }
            }
        }

        private void UpdateEnvironmentTransforms(Vector3 previewPivot)
        {
            Vector3 environmentPosition = new Vector3(previewPivot.x, 0f, previewPivot.z);

            if (_groundObject != null)
            {
                _groundObject.transform.SetPositionAndRotation(environmentPosition, Quaternion.identity);
                _groundObject.transform.localScale = new Vector3(_gridExtent * 2f, 1f, _gridExtent * 2f);
            }

            if (_gridObject != null)
            {
                _gridObject.transform.SetPositionAndRotation(environmentPosition, Quaternion.identity);
                _gridObject.transform.localScale = Vector3.one;
            }
        }

        private void UpdateProceduralGridMaterialProperties()
        {
            if (!IsProceduralGridMaterial(_groundMaterial))
            {
                return;
            }

            Material previewGridTemplate = LoadPreviewGridMaterialTemplate();
            if (previewGridTemplate == null)
            {
                return;
            }

            _groundMaterial.CopyPropertiesFromMaterial(previewGridTemplate);
            _groundMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        private Material LoadPreviewGridMaterialTemplate()
        {
            if (_previewGridMaterialTemplate == null)
            {
                _previewGridMaterialTemplate = AssetDatabase.LoadAssetAtPath<Material>(PreviewGridMaterialPath);
            }

            return _previewGridMaterialTemplate;
        }

        private void EnsureProceduralGridMaterialInstance(Material previewGridTemplate)
        {
            if (!CanUseConfiguredPreviewGridMaterial(previewGridTemplate))
            {
                DestroyRuntimeMaterial(ref _groundMaterial);
                return;
            }

            bool needsNewInstance = _groundMaterial == null ||
                                    !HasProceduralGridShader(_groundMaterial) ||
                                    _groundMaterial.shader != previewGridTemplate.shader ||
                                    AssetDatabase.Contains(_groundMaterial);

            if (needsNewInstance)
            {
                DestroyRuntimeMaterial(ref _groundMaterial);
                _groundMaterial = CreateProceduralGridMaterial(previewGridTemplate);
            }

            UpdateProceduralGridMaterialProperties();
        }

        private Color GetConfiguredGridColor(string propertyName, Color fallback)
        {
            Material previewGridTemplate = LoadPreviewGridMaterialTemplate();
            if (previewGridTemplate != null && previewGridTemplate.HasProperty(propertyName))
            {
                return previewGridTemplate.GetColor(propertyName);
            }

            return fallback;
        }

        private float GetConfiguredGridFloat(string propertyName, float fallback)
        {
            Material previewGridTemplate = LoadPreviewGridMaterialTemplate();
            if (previewGridTemplate != null && previewGridTemplate.HasProperty(propertyName))
            {
                return previewGridTemplate.GetFloat(propertyName);
            }

            return fallback;
        }

        private int GetConfiguredGridInt(string propertyName, int fallback)
        {
            Material previewGridTemplate = LoadPreviewGridMaterialTemplate();
            if (previewGridTemplate != null && previewGridTemplate.HasProperty(propertyName))
            {
                return previewGridTemplate.GetInt(propertyName);
            }

            return fallback;
        }

        private static bool CanUseConfiguredPreviewGridMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.name == PreviewGridShaderName &&
                   CanUseProceduralGridShader(material.shader);
        }

        private static bool HasProceduralGridShader(Material material)
        {
            return material != null && material.shader != null && material.shader.name == PreviewGridShaderName;
        }

        private static bool CanUseProceduralGridShader(Shader shader)
        {
            if (shader == null || !shader.isSupported)
            {
                return false;
            }

            RenderPipelineAsset currentPipeline = GraphicsSettings.currentRenderPipeline;
            if (currentPipeline == null)
            {
                return true;
            }

            string typeName = currentPipeline.GetType().FullName;
            return !string.IsNullOrEmpty(typeName) &&
                   (typeName.IndexOf("UniversalRenderPipelineAsset", StringComparison.Ordinal) >= 0 ||
                    typeName.IndexOf("HDRenderPipelineAsset", StringComparison.Ordinal) >= 0 ||
                    typeName.IndexOf("HighDefinitionRenderPipelineAsset", StringComparison.Ordinal) >= 0);
        }

        private static bool IsProceduralGridMaterial(Material material)
        {
            return material != null && HasProceduralGridShader(material) &&
                   CanUseProceduralGridShader(material.shader) && material.passCount > 0;
        }

        private static bool NeedsMaterialRebuild(Material material)
        {
            if (material == null)
            {
                return false;
            }

            if (material.shader == null || material.passCount <= 0)
            {
                return true;
            }

            if (!material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
            {
                return true;
            }

            return HasProceduralGridShader(material) && !CanUseProceduralGridShader(material.shader);
        }

        private static Material CreateProceduralGridMaterial(Material template)
        {
            if (!CanUseConfiguredPreviewGridMaterial(template))
            {
                return null;
            }

            var material = new Material(template)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (material.passCount <= 0)
            {
                UnityEngine.Object.DestroyImmediate(material);
                return null;
            }

            return material;
        }

        private static Material CreateColoredMaterial(Shader shader, Color color, bool writeDepth)
        {
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            material.SetColor("_Color", color);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", writeDepth ? 1 : 0);

            return material;
        }

        private static Material CreateFallbackMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            ApplyMaterialColor(material, color);
            return material;
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void DestroyRuntimeMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (!AssetDatabase.Contains(material))
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            material = null;
        }

        private static GameObject CreateHiddenGameObject(string name, Scene scene)
        {
            if (!scene.IsValid())
            {
                return null;
            }

            var gameObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            gameObject.layer = RetargetAnimBaker.PreviewLayer;

            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static void DestroyGameObject(ref GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(gameObject);
            gameObject = null;
        }
    }
}
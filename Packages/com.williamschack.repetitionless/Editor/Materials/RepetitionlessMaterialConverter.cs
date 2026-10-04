#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Materials
{
    using Data;

    /// <summary>
    /// Used to convert lit materials to repetitionless materials
    /// </summary>
    public static class RepetitionlessMaterialConverter
    {
        private const string LIT_SHADER_NAME_BIRP = "Standard";
        private const string LIT_SHADER_NAME_URP  = "Universal Render Pipeline/Lit";
        private const string LIT_SHADER_NAME_HDRP = "HDRP/Lit";

        [MenuItem("Window/Repetitionless/Convert Selected Materials", priority = 4)]
        [MenuItem("Assets/Create/Repetitionless/Convert Selected Materials", priority = 102)]
        private static void ConvertSelectedMaterialsToolbar()
        {
            Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            if (selectedMaterials.Length == 0) {
                Debug.LogWarning("No materials selected to convert");
                return;
            }

            ConvertMaterials(selectedMaterials);
        }

        private static void ConvertMaterialBirp(Material originalMat, MaterialDataObjects materialDataObjects)
        {
            // Get material properties
            Texture2D colourTex       = (Texture2D)originalMat.GetTexture("_MainTex");
            Texture2D metalTex        = (Texture2D)originalMat.GetTexture("_MetallicGlossMap");
            Texture2D normalTex       = (Texture2D)originalMat.GetTexture("_BumpMap");
            Texture2D occlussionTex   = (Texture2D)originalMat.GetTexture("_OcclusionMap");
            Texture2D emissionTex     = (Texture2D)originalMat.GetTexture("_EmissionMap");
            Color baseColour          = originalMat.GetColor("_Color");
            Color emissionColour      = originalMat.GetColor("_EmissionColor");
            float metal               = originalMat.GetFloat("_Metallic");
            float smoothness          = originalMat.GetFloat("_GlossMapScale");
            float occlussion          = originalMat.GetFloat("_OcclusionStrength");
            float normalStrength      = originalMat.GetFloat("_BumpScale");
            float alphaClipping       = originalMat.GetFloat("_Cutoff");
            Vector2 tiling            = originalMat.GetTextureScale("_MainTex");
            Vector2 offset            = originalMat.GetTextureOffset("_MainTex");
            bool emissionEnabled      = originalMat.IsKeywordEnabled("_EMISSION");
            bool alphaClippingEnabled = originalMat.GetFloat("_Mode") == 1;

            // Assign properties to new material
            Material newMat                             = materialDataObjects.Material;
            RepetitionlessTextureDataSO textureData     = materialDataObjects.TextureDataSO;
            RepetitionlessMaterialDataSO materialData   = materialDataObjects.MaterialDataSO;
            RepetitionlessMaterialData baseMaterialData = materialData.Data[0].BaseMaterialData;

            textureData.SetupTextureDrawers();
            textureData.AVTexturesDrawer.UpdateTexture(colourTex, 0, 0, true);
            textureData.EMTexturesDrawer.UpdateTexture(metalTex, 0, 1, true);
            textureData.NSOTexturesDrawer.UpdateTexture(normalTex, 0, 0, true);
            textureData.NSOTexturesDrawer.UpdateTexture(occlussionTex, 0, 2, true);
            textureData.EMTexturesDrawer.UpdateTexture(emissionTex, 0, 0, true);
            textureData.Save();

            materialData.UpdateAssignedTextures(newMat, textureData, 0, 0);
            baseMaterialData.AlbedoTint = baseColour;
            baseMaterialData.EmissionColour = emissionColour;
            baseMaterialData.Metallic = metal;
            baseMaterialData.SmoothnessRoughness = smoothness;
            baseMaterialData.OcclussionStrength = occlussion;
            baseMaterialData.NormalScale = normalStrength;
            baseMaterialData.AlphaClipping = alphaClipping;
            baseMaterialData.TilingOffset = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
            baseMaterialData.EmissionEnabled = emissionEnabled;
            materialData.UpdateMaterialTexture(newMat, 0);
            materialData.Save();

            ESurfaceType surfaceType = alphaClippingEnabled ? ESurfaceType.Cutout : ESurfaceType.Opaque;
            RepetitionlessMaterialUtilities.SetSurface(newMat, surfaceType, ERenderPipeline.URP);
            
            newMat.globalIlluminationFlags = originalMat.globalIlluminationFlags;
            newMat.doubleSidedGI = originalMat.doubleSidedGI;
        }

        private static void ConvertMaterialUrp(Material originalMat, MaterialDataObjects materialDataObjects)
        {
            // Get material properties
            Texture2D colourTex       = (Texture2D)originalMat.GetTexture("_BaseMap");
            Texture2D metalTex        = (Texture2D)originalMat.GetTexture("_MetallicGlossMap");
            Texture2D normalTex       = (Texture2D)originalMat.GetTexture("_BumpMap");
            Texture2D occlussionTex   = (Texture2D)originalMat.GetTexture("_OcclusionMap");
            Texture2D emissionTex     = (Texture2D)originalMat.GetTexture("_EmissionMap");
            Color baseColour          = originalMat.GetColor("_BaseColor");
            Color emissionColour      = originalMat.GetColor("_EmissionColor");
            float metal               = originalMat.GetFloat("_Metallic");
            float smoothness          = originalMat.GetFloat("_Smoothness");
            float occlussion          = originalMat.GetFloat("_OcclusionStrength");
            float normalStrength      = originalMat.GetFloat("_BumpScale");
            float alphaClipping       = originalMat.GetFloat("_Cutoff");
            Vector2 tiling            = originalMat.GetTextureScale("_BaseMap");
            Vector2 offset            = originalMat.GetTextureOffset("_BaseMap");
            bool emissionEnabled      = originalMat.IsKeywordEnabled("_EMISSION");
            bool alphaClippingEnabled = originalMat.GetFloat("_AlphaClip") == 1;

            // Assign properties to new material
            Material newMat                             = materialDataObjects.Material;
            RepetitionlessTextureDataSO textureData     = materialDataObjects.TextureDataSO;
            RepetitionlessMaterialDataSO materialData   = materialDataObjects.MaterialDataSO;
            RepetitionlessMaterialData baseMaterialData = materialData.Data[0].BaseMaterialData;

            textureData.SetupTextureDrawers();
            textureData.AVTexturesDrawer.UpdateTexture(colourTex, 0, 0, true);
            textureData.EMTexturesDrawer.UpdateTexture(metalTex, 0, 1, true);
            textureData.NSOTexturesDrawer.UpdateTexture(normalTex, 0, 0, true);
            textureData.NSOTexturesDrawer.UpdateTexture(occlussionTex, 0, 2, true);
            textureData.EMTexturesDrawer.UpdateTexture(emissionTex, 0, 0, true);
            textureData.Save();

            materialData.UpdateAssignedTextures(newMat, textureData, 0, 0);
            baseMaterialData.AlbedoTint = baseColour;
            baseMaterialData.EmissionColour = emissionColour;
            baseMaterialData.Metallic = metal;
            baseMaterialData.SmoothnessRoughness = smoothness;
            baseMaterialData.OcclussionStrength = occlussion;
            baseMaterialData.NormalScale = normalStrength;
            baseMaterialData.AlphaClipping = alphaClipping;
            baseMaterialData.TilingOffset = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
            baseMaterialData.EmissionEnabled = emissionEnabled;
            materialData.UpdateMaterialTexture(newMat, 0);
            materialData.Save();

            ESurfaceType surfaceType = alphaClippingEnabled ? ESurfaceType.Cutout : ESurfaceType.Opaque;
            RepetitionlessMaterialUtilities.SetSurface(newMat, surfaceType, ERenderPipeline.URP);
            
            newMat.globalIlluminationFlags = originalMat.globalIlluminationFlags;
            newMat.doubleSidedGI = originalMat.doubleSidedGI;
        }

        private static void ConvertMaterialHdrp(Material originalMat, MaterialDataObjects materialDataObjects)
        {
            // Get material properties
            Texture2D colourTex       = (Texture2D)originalMat.GetTexture("_BaseColorMap");
            Texture2D maskTex         = (Texture2D)originalMat.GetTexture("_MaskMap");
            Texture2D normalTex       = (Texture2D)originalMat.GetTexture("_NormalMap");
            Texture2D emissionTex     = (Texture2D)originalMat.GetTexture("_EmissiveColorMap");
            Color baseColour          = originalMat.GetColor("_BaseColor");
            Color emissionColour      = originalMat.GetColor("_EmissiveColor");
            float metal               = originalMat.GetFloat("_MetallicRemapMax");   // Min max not currently implemented
            float smoothness          = originalMat.GetFloat("_SmoothnessRemapMax"); // Min max not currently implemented
            float occlussion          = originalMat.GetFloat("_AORemapMax");         // Min max not currently implemented
            float normalStrength      = originalMat.GetFloat("_NormalScale");
            float alphaClipping       = originalMat.GetFloat("_Cutoff");
            Vector2 tiling            = originalMat.GetTextureScale("_BaseColorMap");
            Vector2 offset            = originalMat.GetTextureOffset("_BaseColorMap");
            bool alphaClippingEnabled = originalMat.GetFloat("_AlphaCutoffEnable") == 1;

            // Assign properties to new material
            Material newMat                             = materialDataObjects.Material;
            RepetitionlessTextureDataSO textureData     = materialDataObjects.TextureDataSO;
            RepetitionlessMaterialDataSO materialData   = materialDataObjects.MaterialDataSO;
            RepetitionlessMaterialData baseMaterialData = materialData.Data[0].BaseMaterialData;

            textureData.SetupTextureDrawers();
            textureData.AVTexturesDrawer.UpdateTexture(colourTex, 0, 0, true);
            textureData.NSOTexturesDrawer.UpdateTexture(normalTex, 0, 0, true);
            textureData.EMTexturesDrawer.UpdateTexture(emissionTex, 0, 0, true);
            
            bool usingPackedTexture = maskTex != null;
            if (usingPackedTexture) {
                textureData.UpdatePackedTexture(0, 0, true);
                textureData.NSOTexturesDrawer.UpdateTexture(maskTex, 0, 3, true);
                textureData.EMTexturesDrawer.UpdateTexture(maskTex, 0, 2, true);
            }

            textureData.Save();

            materialData.UpdateAssignedTextures(newMat, textureData, 0, 0);
            baseMaterialData.PackedTexture = usingPackedTexture;
            baseMaterialData.AlbedoTint = baseColour;
            baseMaterialData.EmissionColour = emissionColour;
            baseMaterialData.Metallic = metal;
            baseMaterialData.SmoothnessRoughness = smoothness;
            baseMaterialData.OcclussionStrength = occlussion;
            baseMaterialData.NormalScale = normalStrength;
            baseMaterialData.AlphaClipping = alphaClipping;
            baseMaterialData.TilingOffset = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
            baseMaterialData.EmissionEnabled = true;
            materialData.UpdateMaterialTexture(newMat, 0);
            materialData.Save();

            ESurfaceType surfaceType = alphaClippingEnabled ? ESurfaceType.Cutout : ESurfaceType.Opaque;
            RepetitionlessMaterialUtilities.SetSurface(newMat, surfaceType, ERenderPipeline.HDRP);
            
            newMat.globalIlluminationFlags = originalMat.globalIlluminationFlags;
            newMat.doubleSidedGI = originalMat.doubleSidedGI;
        }

        /// <summary>
        /// Convert a material to a repetitionless material<br />
        /// Saves the material next to the original
        /// </summary>
        /// <param name="material">
        /// The material to convert
        /// </param>
        /// <returns>
        /// The created material
        /// </returns>
        public static Material ConvertMaterial(Material material)
        {
            // Check if the asset exists
            string path = AssetDatabase.GetAssetPath(material);
            if (path == "" ||! File.Exists(path)) {
                Debug.LogWarning($"Could not convert {material.name}: Asset not found");
                return null;
            }

            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            fileName += "_repetitionless.mat";

            string shaderName = material.shader.name;

            ERenderPipeline renderPipeline = ERenderPipeline.Unknown;
            if (shaderName == LIT_SHADER_NAME_BIRP) renderPipeline = ERenderPipeline.Builtin;
            if (shaderName == LIT_SHADER_NAME_URP)  renderPipeline = ERenderPipeline.URP;
            if (shaderName == LIT_SHADER_NAME_HDRP) renderPipeline = ERenderPipeline.HDRP;
            if (renderPipeline == ERenderPipeline.Unknown) {
                Debug.LogWarning($"Could not convert {material.name}: Material must have a lit shader");
                return null;
            }

            // Create repetitionless material
            MaterialDataObjects materialDataObjects = RepetitionlessMaterialCreator.CreateMaterial(renderPipeline, directory, fileName, false);
            if (materialDataObjects.Material == null) {
                Debug.LogWarning($"Could not convert {material.name}: Failed to create material");
                return null;
            }
            
            // Move properties
            switch (renderPipeline) {
                case ERenderPipeline.Builtin:
                    ConvertMaterialBirp(material, materialDataObjects);
                    break;
                case ERenderPipeline.URP:
                    ConvertMaterialUrp(material, materialDataObjects);
                    break;
                case ERenderPipeline.HDRP:
                    ConvertMaterialHdrp(material, materialDataObjects);
                    break;
            }

            return materialDataObjects.Material;
        }

        /// <summary>
        /// Convert multiple materials to repetitionless materials<br />
        /// Saves the materials next to the originals
        /// </summary>
        /// <param name="materials">
        /// The materials to convert
        /// </param>
        /// <param name="selectConverted">
        /// If the materials will be selected in the project window after creation
        /// </param>
        public static void ConvertMaterials(Material[] materials, bool selectConverted = true)
        {
            List<Material> convertedMats = new List<Material>();

            foreach (Material material in materials) {
                Material newMat = ConvertMaterial(material);
                if (newMat == null) continue;

                convertedMats.Add(newMat);
            }

            if (selectConverted && convertedMats.Count > 0) {
                Selection.objects = convertedMats.ToArray();
            }
        }
    }
}
#endif
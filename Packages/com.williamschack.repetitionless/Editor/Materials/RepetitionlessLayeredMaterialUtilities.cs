#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

using Repetitionless.Runtime.Variables;
using Repetitionless.Runtime.Utilities;

namespace Repetitionless.Editor.Materials
{
    using Data;

    internal class RepetitionlessLayeredMaterialUtilities
    {
        private const string UV_SPACE_PROP_NAME = "_UVSpace";

        public static void SetupProperties(Material mat, RepetitionlessMaterialDataSO materialProperties)
        {
            // Set uv space to world
            mat.SetFloat(UV_SPACE_PROP_NAME, (int)EUVSpace.World);

            // Update default global tiling offset
            materialProperties.SetGlobalTilingOffset(Constants.DEFAULT_TILING_OFFSET_TERRAIN);

            // Add max layers keyword
            RepetitionlessMaterialUtilities.UpdateMaxLayersKeyword(mat, EMaxLayers.Four);

            // If in HDRP & Under Unity 6.3, add TerrainCompatible keyword
#if !UNITY_6000_3_OR_NEWER
            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            if (currentPipeline == ERenderPipeline.HDRP)
                mat.SetOverrideTag("TerrainCompatible", "True");
#endif
        }

        public static void UpdateLayerModeShader(MaterialDataManager dataManager, ELayerMode layerMode)
        {
            string newShader = "";
            switch (layerMode) {
                case ELayerMode.TerrainLayers:
                    newShader = Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN;

                    // Create terrain data if required
                    SetupTerrainData(dataManager);
                    break;
                case ELayerMode.ControlTextures:
                    newShader = Constants.SHADER_MATERIAL_NAME_LAYERED_LIT;
                    break;
                default:
                    return;
            }

            string shaderName = dataManager.Material.shader.name;
            string rp = shaderName.Split("/")[1];
            string newShaderName = $"{Constants.SHADER_FOLDER}{rp}/{newShader}";
            
            dataManager.Material.shader = Shader.Find(newShaderName);
        }

        public static RepetitionlessLayeredDataSO SetupLayeredData(MaterialDataManager dataManager)
        {
            if (dataManager.AssetExists(Constants.LAYERED_DATA_FILE_NAME))
                return dataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);

            RepetitionlessLayeredDataSO data = ScriptableObject.CreateInstance<RepetitionlessLayeredDataSO>();
            dataManager.CreateAsset(data, Constants.LAYERED_DATA_FILE_NAME);

            // Setup the textures
            data.Init();

            // Update layer mode based on shader name
            string shaderName = dataManager.Material.shader.name;
            if (shaderName.Contains(Constants.SHADER_MATERIAL_NAME_LAYERED_LIT)) {
                data.LayerMode = ELayerMode.ControlTextures;

                // Create white control texture
                data.PackControlTexture(0);
                data.AssignControlTexture(0);
            } else {
                data.LayerMode = ELayerMode.TerrainLayers;
            }

            UpdateLayerModeShader(dataManager, data.LayerMode);

            data.Save();
            AssetDatabase.SaveAssetIfDirty(data);

            return data;
        }

        public static RepetitionlessTerrainDataSO SetupTerrainData(MaterialDataManager dataManager)
        {
            if (dataManager.AssetExists(Constants.TERRAIN_DATA_FILE_NAME))
                return dataManager.LoadAsset<RepetitionlessTerrainDataSO>(Constants.TERRAIN_DATA_FILE_NAME);

            RepetitionlessTerrainDataSO data = ScriptableObject.CreateInstance<RepetitionlessTerrainDataSO>();
            dataManager.CreateAsset(data, Constants.TERRAIN_DATA_FILE_NAME);

            // If in HDRP & Under Unity 6.3, add TerrainCompatible keyword
#if !UNITY_6000_3_OR_NEWER
            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            if (currentPipeline == ERenderPipeline.HDRP)
                dataManager.Material.SetOverrideTag("TerrainCompatible", "True");
#endif

            data.Save();
            AssetDatabase.SaveAssetIfDirty(data);

            return data;
        }

        public static Material GetFirstLayeredMaterial(MeshRenderer renderer)
        {
            foreach (Material mat in renderer.sharedMaterials) {
                if (!mat.shader.name.Contains(Constants.SHADER_MATERIAL_NAME_LAYERED))
                    continue;

                return mat; // Assume only one material is on the object
            }

            return null;
        }
    }
}
#endif
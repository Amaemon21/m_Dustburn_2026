using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Data
{
    using Utilities.GUI;

    /// <summary>
    /// Stores the terrain data for a Repetitionless material
    /// </summary>
    public class RepetitionlessTerrainDataSO : ScriptableObject
    {
        [SerializeField] private List<TerrainLayer> _terrainLayers = new List<TerrainLayer>();

        /// <summary>
        /// The terrain layers linked to this material
        /// </summary>
        public List<TerrainLayer> TerrainLayers => _terrainLayers;

        private MaterialDataManager _dataManagerCache;
        private MaterialDataManager _dataManager {
            get {
                if (_dataManagerCache != null)
                    return _dataManagerCache;

                _dataManagerCache = new MaterialDataManager(this);
                return _dataManagerCache;
            }
        }

        private RepetitionlessLayeredDataSO _layeredDataCache;
        private RepetitionlessLayeredDataSO _layeredData {
            get {
                if (_layeredDataCache != null)
                    return _layeredDataCache;

                _layeredDataCache = _dataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);
                return _layeredDataCache;
            }
        }

        /// <summary>
        /// If the max layers is updated automatically with terrain layers
        /// </summary>
        public bool AutoUpdateMaxLayers = true;

        /// <summary>
        /// Toggles if textures and settings are automatically saved and loaded to and from the terrain layers
        /// </summary>
        public bool AutoSyncLayers = true;

        /// <summary>
        /// Saves this object
        /// </summary>
        public void Save()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Updates the terrain layers, overwrites the currently set with the inputted layers
        /// </summary>
        /// <param name="layers">
        /// The new terrain layers to use
        /// </param>
        public void UpdateTerrainLayers(TerrainLayer[] layers)
        {
            // Update Layers
            _terrainLayers = layers.ToList();

            // Update max layers if auto is set
            if (AutoUpdateMaxLayers)
                _layeredData.UpdateMaxLayers(TerrainLayers.Count);

            Save();
        }

        /// <summary>
        /// Clears the terrain layers
        /// </summary>
        public void ClearTerrainLayers()
        {
            _terrainLayers.Clear();
            Save();
        }

        /// <summary>
        /// Updates the layer linked to the inputted terrain layer on the material
        /// </summary>
        /// <param name="layerIndex">
        /// The index of the terrain layer to update
        /// </param>
        /// <param name="forceUpdate">
        /// Ignores the AutoSyncLayers setting and updates the data anyway
        /// </param>
        public void UpdateLayerMaterialData(int layerIndex, bool forceUpdate = false)
        {
            if (!AutoSyncLayers && !forceUpdate)
                return;

            if (layerIndex < 0 || layerIndex >= _terrainLayers.Count)
                return;

            TerrainLayer terrainLayer = _terrainLayers[layerIndex];
            if (terrainLayer == null) return;

            Material mat = _dataManager.Material;
            if (mat == null) return;

            string progressBarTitle = $"Updating {mat.name}...";
            EditorUtility.DisplayProgressBar(progressBarTitle, "Setting up", 0.0f);

            // Wrapping in try to prevent infinite progress bar on an error
            try {
                RepetitionlessMaterialDataSO materialProperties = _dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
                RepetitionlessTextureDataSO  textureData        = _dataManager.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);

                if (materialProperties == null || textureData == null) {
                    Debug.LogError($"Could not find properties or textures for material {mat.name}");
                    EditorUtility.ClearProgressBar();
                    return;
                }

                // Setup data
                textureData.SetupTextureDrawers();

                // Get the layer index
                if (layerIndex >= materialProperties.Data.Length || layerIndex >= textureData.LayersTextureData.Length) {
                    EditorUtility.ClearProgressBar();
                    return;
                }

                // Update properties
                RepetitionlessMaterialData baseMaterialData = materialProperties.Data[layerIndex].BaseMaterialData;
                baseMaterialData.NormalScale  = terrainLayer.normalScale;
                baseMaterialData.TilingOffset = new Vector4(terrainLayer.tileSize.x, terrainLayer.tileSize.y, terrainLayer.tileOffset.x, terrainLayer.tileOffset.y);

                EditorUtility.DisplayProgressBar(progressBarTitle, "Updating Textures", 0.2f);

                // Update diffuse, normal textures
                int arrayLayerIndex = layerIndex * Constants.MATERIALS_PER_LAYER_COUNT + 0; // Using base material
                if(terrainLayer.diffuseTexture != textureData.GetTextureData(layerIndex, 0, 0)[0].Texture)
                    textureData.AVTexturesDrawer.UpdateTexture(terrainLayer.diffuseTexture, arrayLayerIndex, 0, true);
                if(terrainLayer.normalMapTexture != textureData.GetTextureData(layerIndex, 0, 1)[0].Texture)
                    textureData.NSOTexturesDrawer.UpdateTexture(terrainLayer.normalMapTexture, arrayLayerIndex, 0, true);

                if (terrainLayer.maskMapTexture == null) {
                    baseMaterialData.Metallic = terrainLayer.metallic;
                    baseMaterialData.SmoothnessRoughness = terrainLayer.smoothness;

                    if (baseMaterialData.PackedTexture) {
                        // Disable packed texture
                        baseMaterialData.PackedTexture = false;
                        textureData.UpdatePackedTexture(layerIndex, 0, false);
                    }
                } else {
                    // Update packed textures
                    if(!baseMaterialData.PackedTexture ||
                        textureData.GetTextureData(layerIndex, 0, 1)[3].Texture != terrainLayer.maskMapTexture ||
                        textureData.GetTextureData(layerIndex, 0, 2)[2].Texture != terrainLayer.maskMapTexture
                    ) {
                        baseMaterialData.PackedTexture = true;
                    
                        textureData.GetTextureData(layerIndex, 0, 1)[3].Texture = terrainLayer.maskMapTexture;
                        textureData.GetTextureData(layerIndex, 0, 2)[2].Texture = terrainLayer.maskMapTexture;

                        textureData.UpdatePackedTexture(layerIndex, 0, true);
                    }
                }

                EditorUtility.DisplayProgressBar(progressBarTitle, "Updating Properties", 0.8f);

                // Save changed properties
                materialProperties.UpdateAssignedTextures(mat, textureData, layerIndex, 0);

                materialProperties.Save();
                textureData.Save();
            } catch (System.Exception e) {
                Debug.LogException(e);
            }

            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Finds all layers using this terrain layer and updates them
        /// </summary>
        /// <param name="terrainLayer">
        /// The terrain layer to update
        /// </param>
        /// <param name="forceUpdate">
        /// Ignores the AutoSyncLayers setting and updates the data anyway
        /// </param>
        public void UpdateLayerMaterialData(TerrainLayer terrainLayer, bool forceUpdate = false)
        {
            for (int i = 0; i < _terrainLayers.Count; i++) {
                if (_terrainLayers[i] != terrainLayer)
                    continue;

                UpdateLayerMaterialData(i, forceUpdate);
            }
        }

        /// <summary>
        /// Removes any unused terrain layers for a material
        /// </summary>
        /// <param name="material">
        /// The material to update
        /// </param>
        public void RemoveUnusedLayerTextures()
        {
            RepetitionlessTextureDataSO textureData = _dataManager.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);

            // Only handles if textures need to be removed
            textureData.SetupTextureDrawers();
            RemoveArrayLayer(textureData.AVTexturesDrawer,  textureData, _terrainLayers.Count);
            RemoveArrayLayer(textureData.NSOTexturesDrawer, textureData, _terrainLayers.Count);
            RemoveArrayLayer(textureData.EMTexturesDrawer,  textureData, _terrainLayers.Count);
        }

        private void RemoveArrayLayer(TextureArrayCustomChannelsGUIDrawer arrayDrawer, RepetitionlessTextureDataSO textureData, int terrainLayersCount)
        {
            if (arrayDrawer == null || arrayDrawer.Array == null)
                return;

            int arrayDepth = arrayDrawer.Array.depth;
            if (terrainLayersCount >= arrayDepth)
                return;

            for (int i = terrainLayersCount; i < arrayDepth; i++) {
                textureData.AVTexturesDrawer.RemoveArrayLayer(i * Constants.MATERIALS_PER_LAYER_COUNT + 0);
                textureData.AVTexturesDrawer.RemoveArrayLayer(i * Constants.MATERIALS_PER_LAYER_COUNT + 1);
                textureData.AVTexturesDrawer.RemoveArrayLayer(i * Constants.MATERIALS_PER_LAYER_COUNT + 2);
            }
        }
    }
}
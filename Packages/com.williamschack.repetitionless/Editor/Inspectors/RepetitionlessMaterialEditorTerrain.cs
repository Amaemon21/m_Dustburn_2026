#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Inspectors
{
    using Data;
    using Materials;
    using Utilities.GUI;
    using Utilities.Texture;
    using Editor.CustomDialog;

    /// <summary>
    /// The editor for the terrain repetitionless material
    /// </summary>
    public class RepetitionlessMaterialEditorTerrain : RepetitionlessMaterialEditorBase
    {
        private const int MAX_LAYER_OFFSET_WARNING = 8; 

        /// <summary>
        /// The max amount of layers for the material
        /// </summary>
        protected override int _maxLayers => Constants.MAX_LAYERS_TERRAIN;

        private RepetitionlessLayeredDataSO _layeredData;

        // Terrain
        private RepetitionlessTerrainDataSO _materialTerrainData;
        private List<TerrainLayer> _terrainLayers => _materialTerrainData?.TerrainLayers;

        private int _currentLayerIndex = 0;
        private bool _showingLayersDropdown = false;

        private GUIStyle _instanceHeaderStyle;
        private bool _isInstance = false;

        private GUIStyle _toggleSyncStyle;

        /// <summary>
        /// Called when the inspector is first opened
        /// </summary>
        /// <param name="materialEditor">
        /// The material editor being used
        /// </param>
        public override void OnEnable(MaterialEditor materialEditor)
        {
            // Check if this is an instance, disables gui pretty much
            // We need the data folder which is gotten from next to the editor
            _material = (Material)materialEditor.target;
            _isInstance = !AssetDatabase.Contains(_material);
            if (_isInstance) {
                _instanceHeaderStyle = new GUIStyle(GUIUtilities.BoldHeaderLargeStyle);
                _instanceHeaderStyle.wordWrap = true;

                return;
            }

            base.OnEnable(materialEditor);

            _layeredData = RepetitionlessLayeredMaterialUtilities.SetupLayeredData(_dataManager);

            if (_layeredData.LayerMode == ELayerMode.TerrainLayers)
                UpdateTerrainDetails();

            // GUIStyles
            _toggleSyncStyle = new GUIStyle(GUIUtilities.MajorToggleButtonStyle);
            _toggleSyncStyle.fontSize = 12;
        }

        /// <summary>
        /// Called when the material properties are first created
        /// </summary>
        /// <param name="materialProperties">
        /// The material properties that were just created
        /// </param>
        protected override void OnPropertiesCreated(RepetitionlessMaterialDataSO materialProperties)
        {
            RepetitionlessLayeredMaterialUtilities.SetupProperties(_material, materialProperties);
        }

        /// <summary>
        /// Base OnGUI function
        /// </summary>
        /// <param name="materialEditor">
        /// The material editor being used
        /// </param>
        /// <param name="properties">
        /// The material properties
        /// </param>
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (_isInstance) {
                GUILayout.Label("Cannot view the material from an instance, select the main material to view and edit.", _instanceHeaderStyle);
                return;
            }

            base.OnGUI(materialEditor, properties);

            // If OnEnable was just called, prevents errors
            if (_isInstance) return;

            DrawLayerSettings();

            GUILayout.Space(SETTING_PADDING);

            // Base Material
            DrawBaseMaterialGUI(_currentLayerIndex);

            GUILayout.Space(SETTING_PADDING);

            // Distance Blend Material
            DrawDistanceBlendGUI(_currentLayerIndex);

            GUILayout.Space(SETTING_PADDING);

            // Material Blend
            DrawMaterialBlendGUI(_currentLayerIndex);

            GUILayout.Space(SETTING_PADDING);

            // Footer Settings
            DrawDebugGUI();
        }

        private void DrawLayerSettings()
        {
            GUIUtilities.BeginBackgroundVertical();

            GUIUtilities.DrawHeaderLabelLarge("Layers");

            GUILayout.Space(10);

            bool terrainNotAvailable = _layeredData.LayerMode == ELayerMode.TerrainLayers && (_terrainLayers == null || _terrainLayers.Count == 0);
            if (terrainNotAvailable) GUI.enabled = false;
            _currentLayerIndex = EditorGUILayout.IntSlider("Editing Layer", _currentLayerIndex + 1, 1, _layeredData.LayerMode == ELayerMode.TerrainLayers && !terrainNotAvailable ? Mathf.Min(_terrainLayers.Count, (int)_layeredData.MaxLayers) : (int)_layeredData.MaxLayers) - 1;
            if (terrainNotAvailable) GUI.enabled = true;

            EditorGUI.BeginChangeCheck();
            _layeredData.LayerMode = (ELayerMode)EditorGUILayout.EnumPopup(new GUIContent("Mode", "Control Textures: Uses manually set textures to specify where each layer is\nTerrain Layers: Uses automatically synced terrain textures and its terrain layers to assign textures and settings to each layer"), _layeredData.LayerMode);
            bool layerModeChanged = EditorGUI.EndChangeCheck();
            if (layerModeChanged) {
                RepetitionlessLayeredMaterialUtilities.UpdateLayerModeShader(_dataManager, _layeredData.LayerMode);

                if (_layeredData.LayerMode == ELayerMode.TerrainLayers)
                    UpdateTerrainDetails();

                _layeredData.Save();
                _materialProperties.CallOnExternalDataChanged();
            }

            EditorGUI.BeginChangeCheck();
            _layeredData.MaxLayers = (EMaxLayers)EditorGUILayout.EnumPopup(new GUIContent("Max Layers"), _layeredData.MaxLayers);
            if (EditorGUI.EndChangeCheck()) {
                _currentLayerIndex = Mathf.Min(_currentLayerIndex, (int)_layeredData.MaxLayers - 1);

                RepetitionlessMaterialUtilities.UpdateMaxLayersKeyword(_material, _layeredData.MaxLayers);

                _layeredData.Save();
                _materialProperties.CallOnExternalDataChanged();
            }

            switch (_layeredData.LayerMode) {
                case ELayerMode.ControlTextures:
                    DrawControlTextureSettings();
                    break;
                case ELayerMode.TerrainLayers:
                    if (layerModeChanged && (_terrainLayers == null || _terrainLayers.Count == 0))
                        _currentLayerIndex = 0;

                    DrawTerrainSettings();
                    break;
            }

            GUIUtilities.EndBackgroundVertical();
        }

        private void DrawControlTextureSettings()
        {
            DrawHolesTexture();
            DrawControlTexture(_currentLayerIndex, $"Control Texture");

            if (_currentLayerIndex == 0 && _layeredData.GetControlTextureData(0).Texture == null) {
                EditorGUILayout.HelpBox("With no texture assigned, the first layer will always be visible/have a full white control texture", MessageType.Info);
            }

            GUILayout.Space(10);

            GUIUtilities.BeginBackgroundVertical();
            _showingLayersDropdown = GUIUtilities.DrawFoldout(_showingLayersDropdown, "Control Textures");
            if (_showingLayersDropdown) {
                for (int i = 0; i < _maxLayers; i++) {
                    DrawControlTexture(i, $"Control Texture {i + 1}");
                }
            }
            GUIUtilities.EndBackgroundVertical();
        }

        private void DrawControlTexture(int layerIndex, string label)
        {
            // Have to use only line height for ObjectField to draw texture properly
            Rect lineRect = GUIUtilities.GetLineRect(GUIUtilities.LINE_HEIGHT);
            Rect textureRect = lineRect;
            textureRect.width -= CHANNEL_PICKER_WIDTH + 5;

            ref TexturePacker.TextureData textureData = ref _layeredData.GetControlTextureData(layerIndex);
            if (textureData.FromToChannels.Count == 0) {
                // In the case of an error, will reset the from channel
                _layeredData.SetupControlTexture(layerIndex);
                textureData = ref _layeredData.GetControlTextureData(layerIndex);
            }

            TexturePacker.TextureChannel previousFromChannel = textureData.FromToChannels[0].From;

            EditorGUI.BeginChangeCheck();
            textureData.Texture = (Texture2D)EditorGUI.ObjectField(textureRect, new GUIContent(label, "The control texture that will be used for this layer. It will read from the selected channel"), textureData.Texture, typeof(Texture2D), false);
            textureData.FromToChannels[0] = new TexturePacker.FromToChannel(DrawChannelPicker(lineRect, textureData.FromToChannels[0].From), textureData.FromToChannels[0].To);
            if (EditorGUI.EndChangeCheck()) {
                bool packingTexture = true;

                // If from channel changed and texture is the packed texture, warn of issues
                if (previousFromChannel != textureData.FromToChannels[0].From &&
                    textureData.Texture == _layeredData.PackedControlTextures[_layeredData.GetControlIndexFromLayerIndex(layerIndex)]) {
                    packingTexture = ShaderGUIDialog.DisplayDialog(
                        "Channel Update Warning",
                        "THIS WILL CAUSE ISSUES!\nUpdating the channel while its packed texture is assigned will lose the channel data and cause undefined behaviour.\nWould you still like to proceed?",
                        "Proceed",
                        "Cancel"
                    );

                    if (!packingTexture)
                        textureData.FromToChannels[0] = new TexturePacker.FromToChannel(previousFromChannel, textureData.FromToChannels[0].To);
                }

                if (packingTexture) {
                    int controlIndex = _layeredData.GetControlIndexFromLayerIndex(layerIndex);
                    _layeredData.PackControlTexture(controlIndex);
                    _layeredData.AssignControlTexture(controlIndex);
                    _layeredData.UpdateLayersCount();
                    _layeredData.Save();
                }
            }
        }

        private void DrawHolesTexture()
        {
            // Have to use only line height for ObjectField to draw texture properly
            Rect lineRect = GUIUtilities.GetLineRect(GUIUtilities.LINE_HEIGHT);
            Rect textureRect = lineRect;
            textureRect.width -= CHANNEL_PICKER_WIDTH + 5;

            ref TexturePacker.TextureData textureData = ref _layeredData.HolesTexture;
            if (textureData.FromToChannels.Count == 0) {
                // In the case of an error, will reset the from channel
                _layeredData.SetupHolesTexture();
                textureData = ref _layeredData.HolesTexture;
            }

            EditorGUI.BeginChangeCheck();
            textureData.Texture = (Texture2D)EditorGUI.ObjectField(textureRect, new GUIContent("Holes Texture", "The holes texture that will be used for the material. It will read from the selected channel"), textureData.Texture, typeof(Texture2D), false);
            textureData.FromToChannels[0] = new TexturePacker.FromToChannel(DrawChannelPicker(lineRect, textureData.FromToChannels[0].From), textureData.FromToChannels[0].To);
            if (EditorGUI.EndChangeCheck()) {
                _layeredData.AssignHolesTexture();
                _layeredData.Save();
            }
        }

        private void DrawTerrainSettings()
        {
            if (_terrainLayers == null || _terrainLayers.Count == 0) {
                GUILayout.Space(10);

                EditorGUILayout.HelpBox("No terrain layers found\nAdd a RepetitionlessTerrain component to a terrain to setup this material", MessageType.Warning);
                return;
            }

            // Cap layer index incase they changed while outside the inspector
            _currentLayerIndex = Mathf.Min(_currentLayerIndex, _terrainLayers.Count - 1);

            _materialTerrainData.AutoUpdateMaxLayers = EditorGUILayout.Toggle(new GUIContent("Auto Update Max Layers"), _materialTerrainData.AutoUpdateMaxLayers);

            if (_terrainLayers.Count > (int)_layeredData.MaxLayers) {
                EditorGUILayout.HelpBox($"You have {_terrainLayers.Count} terrain layers synced with a max of {(int)_layeredData.MaxLayers} layers.\nAll layers past {(int)_layeredData.MaxLayers} will not be shown, change Max Layers above to allow more layers.", MessageType.Warning);
            } else if (_terrainLayers.Count < (int)_layeredData.MaxLayers - MAX_LAYER_OFFSET_WARNING) {
                EditorGUILayout.HelpBox($"You have a max of {(int)_layeredData.MaxLayers} terrain layers with only {_terrainLayers.Count} assigned.\nChange Max Layers to a lower amount, you are wasting performance.", MessageType.Error);
            }

            GUILayout.Space(10);

            GUI.enabled = false;
            EditorGUILayout.ObjectField(new GUIContent("Terrain Layer", "The terrain layer that is being used for these fields. Modifying these fields that are in the layer will also update that layers fields."), _terrainLayers[_currentLayerIndex], typeof(TerrainLayer), false);
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button(new GUIContent("Load From Layer", "Loads the textures and settings from the current terrain layer"))) {
                EditorApplication.delayCall += () => { _materialTerrainData.UpdateLayerMaterialData(_currentLayerIndex, true); };
            }

            if (GUILayout.Button(new GUIContent("Save To Layer", "Saves the textures and settings in the base material to the current terrain layer"))) {
                EditorApplication.delayCall += () => { SaveMaterialToLayer(_currentLayerIndex); };
            }

            GUILayout.EndHorizontal();

            Color prevBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = _materialTerrainData.AutoSyncLayers ? Color.green : Color.red;

            EditorGUI.BeginChangeCheck();
            _materialTerrainData.AutoSyncLayers = GUILayout.Toggle(_materialTerrainData.AutoSyncLayers, new GUIContent("Auto Sync Layers", "Toggles if textures and settings are automatically saved and loaded to and from the terrain layers"), _toggleSyncStyle);
            if (EditorGUI.EndChangeCheck()) {
                _materialTerrainData.Save();
                AssetDatabase.SaveAssetIfDirty(_materialTerrainData);
            }
            
            GUI.backgroundColor = prevBackgroundColor;

            GUILayout.Space(10);

            GUIUtilities.BeginBackgroundVertical();
            _showingLayersDropdown = GUIUtilities.DrawFoldout(_showingLayersDropdown, "Terrain Layers");
            if (_showingLayersDropdown) {
                GUILayout.Label("These can be changed in the terrain");

                GUI.enabled = false;
                for (int i = 0; i < _terrainLayers.Count; i++) {
                    EditorGUILayout.ObjectField($"Terrain Layer {i + 1}", _terrainLayers[i], typeof(TerrainLayer), false);
                }
                GUI.enabled = true;
            }
            GUIUtilities.EndBackgroundVertical();
        }

        /// <summary>
        /// Used to draw all the texture fields
        /// </summary>
        /// <param name="layerIndex">
        /// The layer which the texture will be drawn
        /// </param>
        /// <param name="sectionIndex">
        /// The section that this texture is in
        /// </param>
        /// <param name="textureIndex">
        /// The index of the texture in this section
        /// </param>
        /// <param name="content">
        /// The GUIContent to use in the field
        /// </param>
        /// <returns>
        /// The rect that the texture field is using
        /// </returns>
        protected override Rect DrawTexture(int layerIndex, int sectionIndex, int textureIndex, GUIContent content)
        {
            EditorGUI.BeginChangeCheck();
            Rect rect = base.DrawTexture(layerIndex, sectionIndex, textureIndex, content);
            if (!EditorGUI.EndChangeCheck() || sectionIndex != 0) return rect;
    
            // Update terrain layer
            if (_layeredData.LayerMode != ELayerMode.TerrainLayers || !_materialTerrainData.AutoSyncLayers || _terrainLayers.Count == 0)
                return rect;

            TerrainLayer terrainLayer = _terrainLayers[layerIndex];

            switch (textureIndex) {
                case 0: { // Albedo
                    ref TexturePacker.TextureData[] textureData = ref _textureData.GetTextureData(layerIndex, sectionIndex, 0);
                    terrainLayer.diffuseTexture = textureData[0].Texture;
                    break;
                } case 1: { // Packed texture
                    ref TexturePacker.TextureData[] textureData = ref _textureData.GetTextureData(layerIndex, sectionIndex, 1);
                    RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);
                    if (!currentData.PackedTexture) break;

                    terrainLayer.maskMapTexture = textureData[3].Texture;
                    break;
                } case 3: { // Normal
                    ref TexturePacker.TextureData[] textureData = ref _textureData.GetTextureData(layerIndex, sectionIndex, 1);
                    terrainLayer.normalMapTexture = textureData[0].Texture;
                    break;
                }
            }

            EditorUtility.SetDirty(terrainLayer);

            return rect;
        }

        /// <summary>
        /// Saves the material property if changed in the action<br />
        /// <b>Each gui function modifying the material properties should be using this function</b>
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index of this property
        /// </param>
        /// <param name="drawPropertyAction">
        /// The draw function
        /// </param>
        protected override void DrawProperty(int layerIndex, Action drawPropertyAction)
        {
            // Check for terrain layer properties
            RepetitionlessMaterialData baseData = GetMaterialData(layerIndex, 0);
            float prevNormal               = baseData.NormalScale;
            float prevMetallic             = baseData.Metallic;
            float prevSmoothness           = baseData.SmoothnessRoughness;
            Vector4 prevTilingOffset       = baseData.TilingOffset;
            Vector4 prevGlobalTilingOffset = _materialProperties.GlobalTilingOffset;

            EditorGUI.BeginChangeCheck();
            base.DrawProperty(layerIndex, drawPropertyAction);
            if (!EditorGUI.EndChangeCheck()) return;
            
            if (_layeredData.LayerMode == ELayerMode.TerrainLayers && layerIndex >= _terrainLayers.Count)
                return;

            if (prevGlobalTilingOffset != _materialProperties.GlobalTilingOffset) {
                // Update for all layers, by default only updates layer 0
                for (int i = 1; i < _maxLayers; i++) {
                    UpdateMaterialPropertiesTexture(i);
                }
            }

            // If any terrain layer properties changed, update them
            if (_layeredData.LayerMode != ELayerMode.TerrainLayers || !_materialTerrainData.AutoSyncLayers || _terrainLayers.Count == 0)
                return;

            TerrainLayer terrainLayer = _terrainLayers[layerIndex];

            if (prevNormal != baseData.NormalScale)
                terrainLayer.normalScale = baseData.NormalScale;

            if (prevMetallic != baseData.Metallic)
                terrainLayer.metallic = baseData.Metallic;
                
            if (prevSmoothness != baseData.SmoothnessRoughness)
                terrainLayer.smoothness = baseData.SmoothnessRoughness;

            if (prevTilingOffset != baseData.TilingOffset) {
                terrainLayer.tileSize   = new Vector2(baseData.TilingOffset.x, baseData.TilingOffset.y);
                terrainLayer.tileOffset = new Vector2(baseData.TilingOffset.z, baseData.TilingOffset.w);
            }
        }

        private void UpdateTerrainDetails()
        {
            if (_materialTerrainData != null)
                return;

            _materialTerrainData = RepetitionlessLayeredMaterialUtilities.SetupTerrainData(_dataManager);
        }

        private void SaveMaterialToLayer(int layerIndex)
        {
            TerrainLayer terrainLayer = _terrainLayers[layerIndex];
            if (terrainLayer == null) return;

            RepetitionlessMaterialData baseData = GetMaterialData(layerIndex, 0);

            // Textures
            ref TexturePacker.TextureData[] AVTextureData = ref _textureData.GetTextureData(layerIndex, 0, 0);
            terrainLayer.diffuseTexture = AVTextureData[0].Texture;

            ref TexturePacker.TextureData[] NSOTextureData = ref _textureData.GetTextureData(layerIndex, 0, 1);
            terrainLayer.normalMapTexture = NSOTextureData[0].Texture;

            if (baseData.PackedTexture)
                terrainLayer.maskMapTexture = NSOTextureData[3].Texture;

            // Properties
            terrainLayer.normalScale = baseData.NormalScale;
            terrainLayer.metallic = baseData.Metallic;
            terrainLayer.smoothness = baseData.SmoothnessRoughness;
            terrainLayer.tileSize   = new Vector2(baseData.TilingOffset.x, baseData.TilingOffset.y);
            terrainLayer.tileOffset = new Vector2(baseData.TilingOffset.z, baseData.TilingOffset.w);
        }
    }
}
#endif
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Inspectors
{
    using Materials;
    using Utilities.GUI;
    using Utilities.Texture;
    using CustomWindows;
    using CustomDialog;
    using Data;

    /// <summary>
    /// Base class for creating the Master/Terrain repetitionless inspector windows<br />
    /// This assumes individual textures which is now unused in the current materials<br />
    /// To use the current packed texture arrays, use RepetitionlessPackedArrayGUIBase
    /// </summary>
    public class RepetitionlessMaterialEditorBase : ShaderGUI
    {
        #region Variables
        
        /// <summary>
        /// Holds the state for each property foldout in a material section
        /// </summary>
        protected class MaterialFoldoutState
        {
            /// <summary>
            /// Foldout state for the main properties
            /// </summary>
            public bool MainProperties = true;

            /// <summary>
            /// Foldout state for the noise properties
            /// </summary>
            public bool NoiseProperties = true;

            /// <summary>
            /// Foldout state for the variation properties
            /// </summary>
            public bool VariationProperties = true;
        }

        /// <summary>
        /// Contains the texture drawer and channel for a texture
        /// </summary>
        protected struct TextureDrawerDetails
        {
            /// <summary>
            /// The texture drawer used
            /// </summary>
            public TextureArrayCustomChannelsGUIDrawer TextureDrawer;
            /// <summary>
            /// The channel index used to index into the texture data
            /// </summary>
            public int ChannelIndex;

            /// <summary>
            /// TextureDrawerDetails Constructor
            /// </summary>
            /// <param name="textureDrawer">
            /// The texture drawer used
            /// </param>
            /// <param name="channelIndex">
            /// The channel index used to index into the texture data
            /// </param>
            public TextureDrawerDetails(TextureArrayCustomChannelsGUIDrawer textureDrawer, int channelIndex)
            {
                TextureDrawer = textureDrawer;
                ChannelIndex = channelIndex;
            }
        }

        // Constants

        /// <summary>
        /// Amount of padding at the top of the inspector
        /// </summary>
        protected const int HEADER_PADDING = 4;

        /// <summary>
        /// Amount of padding between sections
        /// </summary>
        protected const int SETTING_PADDING = 4;

        /// <summary>
        /// Buffer ontop of minWidth for GetScaledText
        /// </summary>
        protected const int SCALED_TEXT_PADDING = 10;

        /// <summary>
        /// The width of a channel picker
        /// </summary>
        protected const int CHANNEL_PICKER_WIDTH = 50;

        // Overridable

        /// <summary>
        /// The max amount of layers for the material
        /// </summary>
        /// 
        protected virtual int _maxLayers => Constants.MAX_LAYERS_REGULAR;

        // Data

        /// <summary>
        /// The data manager used for this material
        /// </summary>
        protected MaterialDataManager _dataManager;

        /// <summary>
        /// The texture data for this material
        /// </summary>
        protected RepetitionlessTextureDataSO _textureData;

        /// <summary>
        /// The material properties for this material
        /// </summary>
        protected RepetitionlessMaterialDataSO _materialProperties;

        // Array Settings Button

        private GUIStyle _arraySettingsButtonStyle;

        // Material Helpers

        /// <summary>
        /// The material being edited
        /// </summary>
        protected Material _material;

        /// <summary>
        /// The material editor being used
        /// </summary>
        protected MaterialEditor _editor;

        /// <summary>
        /// <b>Use FindProperty for getting properties</b><br />
        /// Contains all the material properties<br />
        /// </summary>
        protected Dictionary<string, MaterialProperty> _cachedProperties = new Dictionary<string, MaterialProperty>();

        // Foldout States, dynamically adds new sections

        /// <summary>
        /// Contains the current states for all foldouts<br />
        /// Keys are the material property prefix for that section
        /// </summary>
        protected Dictionary<string, MaterialFoldoutState> _foldoutStates = new Dictionary<string, MaterialFoldoutState>();

        // Debug
        private int _prevDebugIndex = 0;

        // ShaderGUI doesnt have an OnEnable function, using this instead
        private bool _firstSetup = true;

        private EUVSpace _prevUVSpace;

        private bool _triplanarEnabled = false;
        private bool _specularHighlightsEnabled = true;
        private bool _environmentReflectionsEnabled = true;
        #endregion

        #region Helpers
        /// <summary>
        /// Gets a property from the cached properties
        /// </summary>
        /// <param name="name">
        /// The name of the material property
        /// </param>
        /// <returns>
        /// The material property requested
        /// </returns>
        protected MaterialProperty FindProperty(string name)
        {
            return _cachedProperties[name];
        }

        /// <summary>
        /// Dynamically returns a text if the window width is within the given minWidth 
        /// </summary>
        /// <param name="minWidth">
        /// The minimum width for the window width to be to show the large text
        /// </param>
        /// <param name="largeText">
        /// The text to return if the window width is greater than minWidth
        /// </param>
        /// <param name="smallText">
        /// The text to return if the window width is less than minWidth
        /// </param>
        /// <returns>
        /// The scaled text
        /// </returns>
        protected string GetScaledText(int minWidth, string largeText, string smallText)
        {
            // Using screen width so it is accurate in both layout and repaint events
            return Screen.width <= minWidth + SCALED_TEXT_PADDING ? smallText : largeText;
        }

        private Dictionary<string, MaterialProperty> GetMaterialProperties(MaterialProperty[] properties)
        {
            Dictionary<string, MaterialProperty> cachedProperties = new Dictionary<string, MaterialProperty>();
            foreach (MaterialProperty property in properties) {
                cachedProperties.Add(property.name, property);
            }

            return cachedProperties;
        }

        private string GetMaterialPrefix(int sectionIndex)
        {
            switch (sectionIndex) {
              //case 0: return "Base";
                case 1: return "Far";
                case 2: return "Blend";
            }

            return "Base";
        }

        /// <summary>
        /// Gets the layer data for a layer
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to get the data from
        /// </param>
        /// <returns>
        /// The layer data at a layer
        /// </returns>
        protected RepetitionlessLayerData GetLayerData(int layerIndex = 0)
        {
            return _materialProperties.Data[layerIndex];
        }

        /// <summary>
        /// Gets the material data for a specific material
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to get the data from
        /// </param>
        /// <param name="sectionIndex">
        /// The section to get the data from:
        /// 0: Base, 1: Far, 2: Blend
        /// </param>
        /// <returns></returns>
        protected RepetitionlessMaterialData GetMaterialData(int layerIndex, int sectionIndex)
        {
            return _materialProperties.GetMaterialData(layerIndex, sectionIndex);
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
        protected virtual Rect DrawTexture(int layerIndex, int sectionIndex, int textureIndex, GUIContent content)
        {
            Rect lineRect = GUIUtilities.GetLineRect();

            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            TextureDrawerDetails textureDrawerDetails = GetTextureDrawerDetails(textureIndex, currentData.PackedTexture);

            if (_materialProperties != null && _textureData != null)
                Undo.RecordObjects(new Object[] {_materialProperties, _textureData}, $"Modified {_material.name} texture");

            EditorGUI.BeginChangeCheck();
            textureDrawerDetails.TextureDrawer.DrawTexture(lineRect, layerIndex * Constants.MATERIALS_PER_LAYER_COUNT + sectionIndex, textureDrawerDetails.ChannelIndex, content);

            if (EditorGUI.EndChangeCheck()) {
                // Update assigned textures and update the properties
                UpdateAssignedTextures(layerIndex, sectionIndex);

                // If packed texture was changed, manually update texture in emission array aswell
                if (currentData.PackedTexture && textureIndex == 1)
                    _textureData.EMTexturesDrawer.UpdateTexture(_textureData.GetTextureData(layerIndex, sectionIndex, 1)[3].Texture, 0, 2, true);
            }

            // Return rect after texture field
            lineRect = MaterialEditor.GetRectAfterLabelWidth(lineRect);
            return lineRect;
        }

        /// <summary>
        /// Saves the material property if changed in the action<br />
        /// <b>Each gui function modifying the material properties should be using this function</b>
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index 
        /// </param>
        /// <param name="drawPropertyAction"></param>
        protected virtual void DrawProperty(int layerIndex, System.Action drawPropertyAction)
        {
            if (drawPropertyAction == null)
                return;

            MaterialProperty textureProperty = FindProperty(RepetitionlessMaterialDataSO.PROPERTIES_TEXTURE_PROP_NAME);
            if (_materialProperties != null && textureProperty.textureValue != null) {
                Undo.RecordObjects(new Object[] {_materialProperties, (Texture2D)textureProperty.textureValue}, $"Modified {_material.name} property");
            }

            EditorGUI.BeginChangeCheck();
            drawPropertyAction();
            if (EditorGUI.EndChangeCheck()) {
                UpdateMaterialPropertiesTexture(layerIndex);
                _materialProperties.Save();
            }
        }

        private void UpdateVariationTexture(int layerIndex, int sectionIndex, EVariationType prevVariationMode, bool forceRemove = false)
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);
            ref RepetitionlessTextureDataSO.MaterialTextureData textureData = ref _textureData.GetMaterialTextureData(0, sectionIndex);

            // If enabling texture, add it to the array
            if (currentData.VariationMode == EVariationType.CustomTexture && !forceRemove) {
                textureData.AVTextures[1].Disabled = false;

                // Set the texture to the default variation if none assigned
                Texture2D texture = _textureData.GetTextureData(layerIndex, sectionIndex, 0)[1].Texture;
                if (texture == null) {
                    texture = Resources.Load<Texture2D>(Constants.DEFAULT_VARIATION_TEXTURE_NAME_2K);
                }

                bool textureAdded = _textureData.AVTexturesDrawer.UpdateTexture(texture, sectionIndex, 1, true).Item2;
                if (!textureAdded)
                    textureData.AVTextures[1].Texture = null;

                UpdateAssignedTextures(layerIndex, sectionIndex);
                _textureData.Save();
            }

            // If was texture, remove it from the array
            else if (prevVariationMode == EVariationType.CustomTexture || forceRemove) {
                textureData.AVTextures[1].Disabled = true;
                _textureData.Save();

                if (textureData.AVTextures[1].Texture != null)
                    _textureData.AVTexturesDrawer.UpdateTexture(_textureData.GetTextureData(0, sectionIndex, 0)[1].Texture, sectionIndex, 1, true);
            }
        }

        private void UpdateBlendMaskTexture(int layerIndex, EMaskType prevMaskType, bool forceRemove = false)
        {
            RepetitionlessLayerData layerData = _materialProperties.Data[layerIndex];
            ref TexturePacker.TextureData textureData = ref _textureData.LayersTextureData[layerIndex].BlendMaskTexture[0];

            // If enabling texture, add it to the array
            if (layerData.BlendMaskType == EMaskType.CustomTexture && !forceRemove) {
                textureData.Disabled = false;

                bool textureAdded = _textureData.BMTexturesDrawer.UpdateTexture(textureData.Texture, layerIndex, 0, true).Item2;
                if (!textureAdded)
                    textureData.Texture = null;

                UpdateAssignedTextures(layerIndex, 0);
                _textureData.Save();
            }

            // If was texture, remove it from the array
            else if (prevMaskType == EMaskType.CustomTexture || forceRemove) {
                textureData.Disabled = true;
                _textureData.Save();

                if (textureData.Texture != null)
                    _textureData.BMTexturesDrawer.UpdateTexture(textureData.Texture, layerIndex, 0, true);
            }
        }

        /// <summary>
        /// Handles assigned textures that the shader uses to determine whether to use textures or values<br />
        /// Can be overrided to change how the assigned textures are set
        /// </summary>
        /// <param name="layerIndex">
        /// The layer that will be updated
        /// </param>
        /// <param name="sectionIndex">
        /// The section that this texture is in
        /// </param>
        /// <returns>
        /// The compressed assigned textures
        /// </returns>
        protected virtual void UpdateAssignedTextures(int layerIndex, int sectionIndex)
        {
            _materialProperties.UpdateAssignedTextures(_material, _textureData, layerIndex, sectionIndex);
        }

        /// <summary>
        /// Updates the material properties texture
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to update
        /// </param>
        protected virtual void UpdateMaterialPropertiesTexture(int layerIndex)
        {
            _materialProperties.UpdateMaterialTexture(_material, layerIndex);
        }

        /// <summary>
        /// Gets the texture drawer details for a texture
        /// </summary>
        /// <param name="textureIndex">
        /// The texture index:
        /// 0: Albedo, 1: Metallic, 2: Smoothness, 3: Normal, 4: Occlussion, 5: Emission, 6: Variation
        /// </param>
        /// <param name="packedTexture">
        /// If the drawer details for the packed texture will return
        /// </param>
        /// <returns>
        /// The texture drawer detailsUpdateMaterialTexture
        /// </returns>
        protected TextureDrawerDetails GetTextureDrawerDetails(int textureIndex, bool packedTexture)
        {
            if (packedTexture && textureIndex == 1) {
                return new TextureDrawerDetails(_textureData.NSOTexturesDrawer, 3); // Packed Texture
            }

            switch (textureIndex) {
                case 0: return new TextureDrawerDetails(_textureData.AVTexturesDrawer, 0);  // Albedo
                case 1: return new TextureDrawerDetails(_textureData.EMTexturesDrawer, 1);  // Metallic
                case 2: return new TextureDrawerDetails(_textureData.NSOTexturesDrawer, 1); // Smoothness/Roughness
                case 3: return new TextureDrawerDetails(_textureData.NSOTexturesDrawer, 0); // Normal
                case 4: return new TextureDrawerDetails(_textureData.NSOTexturesDrawer, 2); // Occlussion
                case 5: return new TextureDrawerDetails(_textureData.EMTexturesDrawer, 0);  // Emission
                case 6: return new TextureDrawerDetails(_textureData.AVTexturesDrawer, 1);  // Variation
            }

            return new TextureDrawerDetails(null, 0);
        }

        private void ShowArrayConfigureWindow(TextureArrayCustomChannelsGUIDrawer arrayDrawer)
        {
            if (arrayDrawer.Array == null) {
                Debug.LogWarning($"The texture array has not been created yet, there are no textures assigned to modify...");
                return;
            }

            ConfigureArrayWindowLimited.Open(arrayDrawer.Array, $"Configuring ({arrayDrawer.Array.name})", (Texture2DArray newArray) => {
                arrayDrawer.UpdateArray(newArray);
            });
        }

        private void UpdateDistanceBlendKeyword()
        {
            RepetitionlessMaterialUtilities.UpdateDistanceBlendKeyword(_material, _materialProperties);
            _materialProperties.CallOnExternalDataChanged();
        }

        private void UpdateMaterialBlendKeyword()
        {
            RepetitionlessMaterialUtilities.UpdateMaterialBlendKeyword(_material, _materialProperties);
            _materialProperties.CallOnExternalDataChanged();
        }

        private void UpdateVariationKeyword()
        {
            RepetitionlessMaterialUtilities.UpdateVariationKeyword(_material, _maxLayers, _materialProperties);
            _materialProperties.CallOnExternalDataChanged();
        }

        private void SetNoiseQuality(ENoiseQuality noiseQuality)
        {
            RepetitionlessMaterialUtilities.SetNoiseQuality(_material, noiseQuality);

            _materialProperties.CallOnExternalDataChanged();
        }

        private void SetTriplanarEnabled(bool enabled)
        {
            _triplanarEnabled = enabled;
            RepetitionlessMaterialUtilities.SetTriplanarEnabled(_material, enabled);

            _materialProperties.CallOnExternalDataChanged();
        }

        private void SetSpecularHighlightsEnabled(bool enabled)
        {
            _specularHighlightsEnabled = enabled;
            RepetitionlessMaterialUtilities.SetSpecularHighlightsEnabled(_material, enabled);

            _materialProperties.CallOnExternalDataChanged();
        }

        private void SetEnvironmentReflectionsEnabled(bool enabled)
        {
            _environmentReflectionsEnabled = enabled;
            RepetitionlessMaterialUtilities.SetEnvironmentReflectionsEnabled(_material, enabled);

            _materialProperties.CallOnExternalDataChanged();
        }

        private void DrawTextureChannelPicker(Rect lineRect, int layerIndex, int sectionIndex, int texturesIndex, int elementIndex, int channelIndex)
        {
            ref TexturePacker.TextureData textureData = ref _textureData.GetTextureData(layerIndex, sectionIndex, texturesIndex)[elementIndex];

            EditorGUI.BeginChangeCheck();
            TexturePacker.TextureChannel textureChannel = DrawChannelPicker(lineRect, textureData.FromToChannels[channelIndex].From);
            textureData.FromToChannels[channelIndex] = new TexturePacker.FromToChannel(textureChannel, textureData.FromToChannels[channelIndex].To);

            if (EditorGUI.EndChangeCheck()) {
                _textureData.Save();
                AssetDatabase.SaveAssetIfDirty(_textureData);
            }
        }

        private protected TexturePacker.TextureChannel DrawChannelPicker(Rect lineRect, TexturePacker.TextureChannel channel)
        {
            Rect rect = lineRect;
            rect.x += lineRect.width - CHANNEL_PICKER_WIDTH;
            rect.width = CHANNEL_PICKER_WIDTH;

            return (TexturePacker.TextureChannel)EditorGUI.EnumPopup(rect, channel);
        }

        #endregion

        #region GUI Calls
        /// <summary>
        /// Called when the inspector is first opened
        /// </summary>
        /// <param name="materialEditor">
        /// The material editor being used
        /// </param>
        public virtual void OnEnable(MaterialEditor materialEditor)
        {
            // Assign Material Helpers
            _material = (Material)materialEditor.target;
            _editor = materialEditor;

            MaterialProperty uvSpaceProp = FindProperty("_UVSpace");
            _prevUVSpace = (EUVSpace)uvSpaceProp.floatValue;
            
            _triplanarEnabled = _material.IsKeywordEnabled(Constants.TRIPLANAR_KEYWORD);
            _specularHighlightsEnabled = !_material.IsKeywordEnabled(Constants.SPECULAR_HIGHLIGHTS_OFF_KEYWORD);
            _environmentReflectionsEnabled = !_material.IsKeywordEnabled(Constants.ENVIRONMENT_REFLECTIONS_OFF_KEYWORD);

            // Initialize array settings style and menu
            _arraySettingsButtonStyle = new GUIStyle("DropdownButton");
            _arraySettingsButtonStyle.normal.textColor = GUI.skin.button.normal.textColor;
            _arraySettingsButtonStyle.margin = GUI.skin.button.margin;

            // Setup data
            MaterialDataObjects materialDataObjects = RepetitionlessMaterialCreator.SetupMaterial(_material, _maxLayers, OnPropertiesCreated);
            _dataManager = materialDataObjects.DataManager;
            _textureData = materialDataObjects.TextureDataSO;
            _materialProperties = materialDataObjects.MaterialDataSO;

            _textureData.SetupTextureDrawers();

            // Load noise texture incase it got removed
            RepetitionlessMaterialUtilities.UpdateNoiseQualityTexture(_material, _materialProperties.NoiseQuality);
        }

        /// <summary>
        /// Called when the material properties are first created<br />
        /// No need to call base, nothing happens
        /// </summary>
        /// <param name="materialProperties">
        /// The material properties that were just created
        /// </param>
        protected virtual void OnPropertiesCreated(RepetitionlessMaterialDataSO materialProperties) {}

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
            // Cache properties into dict, can change each call, faster than FindProperty (loops through all properties each call)
            _cachedProperties = GetMaterialProperties(properties);
            
            // OnEnable if first call
            if (_firstSetup) {
                OnEnable(materialEditor);
                _firstSetup = false;
            }
            
            // Make Vectors One Line
            EditorGUIUtility.wideMode = true;
            
            GUILayout.Space(HEADER_PADDING);

            // Material Properties
            GUIUtilities.BeginBackgroundVertical();
            DrawMaterialPropertiesGUI();
            GUIUtilities.EndBackgroundVertical();

            GUILayout.Space(SETTING_PADDING);
        }
        #endregion

        #region Material GUI
        /// <summary>
        /// Draws the general material settings at the top of the inspector
        /// </summary>
        protected virtual void DrawMaterialPropertiesGUI()
        {
            if (_materialProperties == null)
                return;

            // Header
            GUIUtilities.DrawHeaderLabelLarge($"Material Properties");

            GUILayout.Space(4);

            // Surface Type
            MaterialProperty surfaceTypeProp = FindProperty("_SurfaceTypeSetting");

            EditorGUI.BeginChangeCheck();
            ESurfaceType surfaceType = (ESurfaceType)surfaceTypeProp.floatValue;
            surfaceType = (ESurfaceType)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), "Surface Type", surfaceType);

            if (EditorGUI.EndChangeCheck()) {
                RepetitionlessMaterialUtilities.SetSurface(_material, surfaceType);
                _materialProperties.CallOnExternalDataChanged();
            }

            // UV Space
            MaterialProperty uvSpaceProp = FindProperty("_UVSpace");

            EditorGUI.BeginChangeCheck();
            EUVSpace uvSpace = (EUVSpace)uvSpaceProp.floatValue;
            uvSpace = (EUVSpace)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), "UV Space", uvSpace);
            if (EditorGUI.EndChangeCheck()) {
                uvSpaceProp.floatValue = (int)uvSpace;
                _prevUVSpace = uvSpace;

                // If setting uv space to local, disable triplanar
                if (uvSpace == EUVSpace.Local)
                    SetTriplanarEnabled(false);

                _materialProperties.CallOnExternalDataChanged();
            }

            // Noise Quality
            EditorGUI.BeginChangeCheck();
            _materialProperties.NoiseQuality = (ENoiseQuality)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), new GUIContent("Noise Quality", "High: Dynamically samples noise\n(Required for non-repeating noise and the angle offset setting)\nMedium: Uses the pre-rendered 4k texture\nLow: Uses the pre-rendered 1k texture"), _materialProperties.NoiseQuality);
            if (EditorGUI.EndChangeCheck()) {
                SetNoiseQuality(_materialProperties.NoiseQuality);
                _materialProperties.CallOnExternalDataChanged();
            }

            // Vertex Colour Blend Mode
            MaterialProperty vertexColourBlendModeProp = FindProperty("_VertexColourBlendMode");

            EditorGUI.BeginChangeCheck();
            EVertexColourBlendMode vertexColourBlendMode = (EVertexColourBlendMode)vertexColourBlendModeProp.floatValue;
            vertexColourBlendMode = (EVertexColourBlendMode)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), new GUIContent("Vertex Colour", "Multiply: Multiplies the vertex colour with the output\nAdditive: Adds the vertex colour to the output\nSubtractive: Subtracts the vertex colour to the output\nOverwrite: Overwrites the output colour"), vertexColourBlendMode);
            if (EditorGUI.EndChangeCheck()) {
                vertexColourBlendModeProp.floatValue = (int)vertexColourBlendMode;
                _materialProperties.CallOnExternalDataChanged();
            }

            // Triplanar
            EditorGUI.BeginChangeCheck();
            _triplanarEnabled = EditorGUILayout.Toggle(new GUIContent("Triplanar", "Uses world space UVs and samples the material in each direction"), _triplanarEnabled);
            if (EditorGUI.EndChangeCheck()) {
                // If enabled set uv space to world
                if (_triplanarEnabled && uvSpace == (int)EUVSpace.Local)
                    uvSpaceProp.floatValue = (int)EUVSpace.World;

                // If disabling triplanar, set uv space back to what it was before
                if (!_triplanarEnabled)
                    uvSpaceProp.floatValue = (int)_prevUVSpace;

                SetTriplanarEnabled(_triplanarEnabled);
            }

            // Global Tiling & Offset
            DrawProperty(0, () => {
                _materialProperties.GlobalTilingOffset = GUIUtilities.DrawTilingOffset(
                    _materialProperties.GlobalTilingOffset,
                    new GUIContent("Global Tiling", "Multiplied with each materials tiling"),
                    new GUIContent("Global Offset", "Added onto each materials offset")
                );
            });

            // Advanced Options
            if (_editor != null) {
                GUILayout.Space(10);

                _editor.RenderQueueField();
                _editor.LightmapEmissionProperty();
                _editor.DoubleSidedGIField();

                // Specular Highlights
                EditorGUI.BeginChangeCheck();
                _specularHighlightsEnabled = EditorGUILayout.Toggle(new GUIContent("Specular Highlights", "When enabled, the Material reflects the shine from direct lighting"), _specularHighlightsEnabled);
                if (EditorGUI.EndChangeCheck())
                    SetSpecularHighlightsEnabled(_specularHighlightsEnabled);

                // Environment Reflections
                EditorGUI.BeginChangeCheck();
                _environmentReflectionsEnabled = EditorGUILayout.Toggle(new GUIContent("Environment Reflections", "When enabled, the Material samples reflections from the nearest Reflection Probes or Lighting Probe"), _environmentReflectionsEnabled);
                if (EditorGUI.EndChangeCheck())
                    SetEnvironmentReflectionsEnabled(_environmentReflectionsEnabled);
            }

            // Texture Array Settings
            GUILayout.Space(10);
            if (EditorGUILayout.DropdownButton(new GUIContent("Array Settings", "Configure the array settings:\nAV: Albedo (rgb), Variation (a)\nNSO: Normal (rg), Smoothness (b), Occlussion (a)\nEM: Emission (rgb), Metallic (a)\nBM: Blend Mask (r)"), FocusType.Keyboard, _arraySettingsButtonStyle)) {
                // Create the menu with only the created arrays

                GenericMenu arrayMenu = new GenericMenu();
                bool addedItem = false;

                if (_textureData.AVTexturesDrawer.Array != null)  { arrayMenu.AddItem(new GUIContent("AV Textures"),  false, () => { ShowArrayConfigureWindow(_textureData.AVTexturesDrawer); });  addedItem = true; }
                if (_textureData.NSOTexturesDrawer.Array != null) { arrayMenu.AddItem(new GUIContent("NSO Textures"), false, () => { ShowArrayConfigureWindow(_textureData.NSOTexturesDrawer); }); addedItem = true; }
                if (_textureData.EMTexturesDrawer.Array != null)  { arrayMenu.AddItem(new GUIContent("EM Textures"),  false, () => { ShowArrayConfigureWindow(_textureData.EMTexturesDrawer); });  addedItem = true; }
                if (_textureData.BMTexturesDrawer.Array != null)  { arrayMenu.AddItem(new GUIContent("BM Textures"),  false, () => { ShowArrayConfigureWindow(_textureData.BMTexturesDrawer); });  addedItem = true; }

                if (addedItem) arrayMenu.ShowAsContext();
                else           ShaderGUIDialog.DisplayDialog("Cant Configure Arrays", "No textures are assigned so no arrays have been created. Textures need to be assigned before the arrays can be configured.", "OK");
            }

            // Data Folder
            if (GUILayout.Button("Ping Data Folder")) {
                Object folderObj = AssetDatabase.LoadAssetAtPath(_dataManager.DataFolderPath(), typeof(Object));
                EditorGUIUtility.PingObject(folderObj);
            }
        }
        #endregion

        #region Base Material GUI Sections
        /// <summary>
        /// Draws a material section GUI
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The material section index
        /// </param>
        /// <param name="headerText">
        /// The header label text for the section
        /// </param>
        protected virtual void DrawMaterialGUI(int layerIndex, int sectionIndex, string headerText = "")
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            // Setup Foldouts
            string foldoutKey = $"{layerIndex}{sectionIndex}";
            if (!_foldoutStates.ContainsKey(foldoutKey))
                _foldoutStates.Add(foldoutKey, new MaterialFoldoutState());

            // Title Label
            if (headerText == "") {
                string materialPrefix = GetMaterialPrefix(sectionIndex);
                headerText = $"{materialPrefix} Material";
            }

            GUIUtilities.DrawHeaderLabelLarge(headerText);

            // Draw Settings Toggles
            DrawMaterialSettingsGUI(layerIndex, sectionIndex);

            // Draw Main Properties Foldout
            EditorGUI.BeginChangeCheck();
            bool mainPropertiesFoldout = _foldoutStates[foldoutKey].MainProperties;
            mainPropertiesFoldout = GUIUtilities.DrawFoldout(mainPropertiesFoldout, "Main Properties");
            if (EditorGUI.EndChangeCheck())
                _foldoutStates[foldoutKey].MainProperties = mainPropertiesFoldout;

            // Draw Main Properties
            if (mainPropertiesFoldout)
                DrawMaterialMainProperties(layerIndex, sectionIndex);

            // Draw Noise Properties
            if (currentData.NoiseEnabled) {
                // Foldout
                EditorGUI.BeginChangeCheck();
                bool noisePropertiesFoldout = _foldoutStates[foldoutKey].NoiseProperties;
                noisePropertiesFoldout = GUIUtilities.DrawFoldout(noisePropertiesFoldout, "Noise Properties");
                if (EditorGUI.EndChangeCheck())
                    _foldoutStates[foldoutKey].NoiseProperties = noisePropertiesFoldout;

                // Properties
                if (noisePropertiesFoldout)
                    DrawMaterialNoiseGUI(layerIndex, sectionIndex);
            }

            // Draw Variation properties
            if (currentData.VariationEnabled) {
                // Foldout
                EditorGUI.BeginChangeCheck();
                bool variationPropertiesFoldout = _foldoutStates[foldoutKey].VariationProperties;
                variationPropertiesFoldout = GUIUtilities.DrawFoldout(variationPropertiesFoldout, "Variation Properties");
                if (EditorGUI.EndChangeCheck())
                    _foldoutStates[foldoutKey].VariationProperties = variationPropertiesFoldout;

                // Properties
                if (variationPropertiesFoldout)
                    DrawMaterialVariationProperties(layerIndex, sectionIndex);
            }
        }

        /// <summary>
        /// Draws the settings at the top of each material section
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The section index to draw
        /// </param>
        /// <param name="showNoise">
        /// Toggles if the noise settings are enabled
        /// </param>
        /// <param name="showVariation">
        /// Toggles if the variation setting is enabled
        /// </param>
        /// <param name="showPT">
        /// Toggles if the packed texture setting is enabled
        /// </param>
        /// <param name="showEmission">
        /// Toggles if the emission setting is enabled
        /// </param>
        /// <param name="showSR">
        /// Toggles if the smoothness/roughness toggle setting is enabled
        /// </param>
        /// <param name="extraWidth">
        /// Any extra width required for the whole section<br />
        /// Used to increase the required width for the labels to expand
        /// </param>
        protected virtual void DrawMaterialSettingsGUI(int layerIndex, int sectionIndex, bool showNoise = true, bool showVariation = true, bool showPT = true, bool showEmission = true, bool showSR = true, int extraWidth = 0)
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            // Calculate scaled text min width
            int minScaledTextWidth = 0;
            if (showNoise)     minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Noise")).x;
            if (showVariation) minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Variation")).x;
            if (showPT)        minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Packed Texture")).x;
            if (showEmission)  minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Emission")).x;
            if (showSR) {
                minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Smooth")).x;
                minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Rough")).x;
            }
            if (currentData.NoiseEnabled) {
                minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Random Scaling")).x;
                minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Random Rotation")).x;
            }
            minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("------------------------------------------")).x; // Filler space (I need to calculate this properly)
            minScaledTextWidth += extraWidth;

            EditorGUILayout.BeginHorizontal();

            // Draw Settings
            DrawLeftMaterialSettingsGUI(currentData, layerIndex, sectionIndex, minScaledTextWidth, showNoise, showVariation);
            GUILayout.FlexibleSpace();
            DrawRightMaterialSettingsGUI(currentData, layerIndex, sectionIndex, minScaledTextWidth, showPT, showEmission, showSR);

            EditorGUILayout.EndHorizontal();
        }
        
        /// <summary>
        /// Draws the left section of the material settings
        /// </summary>
        /// <param name="currentData">
        /// The current material data
        /// </param>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The section index to draw
        /// </param>
        /// <param name="minScaledTextWidth">
        /// The minimum width required for labels to expand
        /// </param>
        /// <param name="showNoise">
        /// Toggles if the noise settings are enabled
        /// </param>
        /// <param name="showVariation">
        /// Toggles if the variation setting is enabled
        /// </param>
        /// <returns>
        /// The modified compressed setting values
        /// </returns>
        protected virtual void DrawLeftMaterialSettingsGUI(RepetitionlessMaterialData currentData, int layerIndex, int sectionIndex, int minScaledTextWidth, bool showNoise = true, bool showVariation = true)
        {
            // Noise Enabled
            if (showNoise) {
                string noiseEnabledStyle = currentData.NoiseEnabled ? "ButtonLeft" : "Button";
                DrawProperty(layerIndex, () => currentData.NoiseEnabled = GUILayout.Toggle(currentData.NoiseEnabled, new GUIContent(GetScaledText(minScaledTextWidth, "Noise", "N"), "Adds random scaling & rotation based on voronoi noise"), noiseEnabledStyle));

                if (currentData.NoiseEnabled) {
                    // Noise Scaling Enabled
                    DrawProperty(layerIndex, () => currentData.RandomiseNoiseScaling = GUILayout.Toggle(currentData.RandomiseNoiseScaling, new GUIContent(GetScaledText(minScaledTextWidth, "Random Scaling", "RS"), "Adds random scaling to each voronoi cell"), "ButtonMid"));

                    // Randomise Rotation Enabled
                    DrawProperty(layerIndex, () => currentData.RandomiseNoiseRotation = GUILayout.Toggle(currentData.RandomiseNoiseRotation, new GUIContent(GetScaledText(minScaledTextWidth, "Random Rotation", "RR"), "Adds random rotation to each voronoi cell"), "ButtonRight"));
                }
            }

            // Variation toggle
            if (showVariation) {
                EditorGUI.BeginChangeCheck();
                DrawProperty(layerIndex, () => currentData.VariationEnabled = GUILayout.Toggle(currentData.VariationEnabled, new GUIContent(GetScaledText(minScaledTextWidth, "Variation", "V"), "Adds random variation on top of the albedo color\n\nUsing a custom texture can cause visible tiling"), "Button"));
                if (EditorGUI.EndChangeCheck()) {
                    UpdateVariationKeyword();

                    if (currentData.VariationMode == EVariationType.CustomTexture)
                        UpdateVariationTexture(layerIndex, sectionIndex, EVariationType.PerlinNoise, !currentData.VariationEnabled);
                }
            }
        }

        /// <summary>
        /// Draws the right section of the material settings
        /// </summary>
        /// <param name="currentData">
        /// The current material data
        /// </param>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The section index to draw
        /// </param>
        /// <param name="minScaledTextWidth">
        /// The minimum width required for labels to expand
        /// </param>
        /// <param name="showPT">
        /// Toggles if the packed texture setting is enabled
        /// </param>
        /// <param name="showEmission">
        /// Toggles if the emission setting is enabled
        /// </param>
        /// <param name="showSR">
        /// Toggles if the smoothness/roughness toggle setting is enabled
        /// </param>
        /// <returns>
        /// The modified compressed setting values
        /// </returns>
        protected virtual void DrawRightMaterialSettingsGUI(RepetitionlessMaterialData currentData, int layerIndex, int sectionIndex, int minScaledTextWidth, bool showPT = true, bool showEmission = true, bool showSR = true)
        {
            // Packed Texture Toggle
            if (showPT) {
                bool prevPackedTexture = currentData.PackedTexture;
                DrawProperty(layerIndex, () => currentData.PackedTexture = GUILayout.Toggle(currentData.PackedTexture, new GUIContent(GetScaledText(minScaledTextWidth, "Packed Texture", "PT"), "If you are using a packed texture of multiple regular ones (Note that textures automatically pack even without this setting enabled)\nR: Metallic\nG: Occlussion\nA: Smoothness/Roughness"), "Button"));

                // If packed texture was changed, update the texture data
                if (prevPackedTexture != currentData.PackedTexture) {
                    UpdateAssignedTextures(layerIndex, sectionIndex);
                    _textureData.UpdatePackedTexture(0, sectionIndex, currentData.PackedTexture);
                }
            }

            // Emission Toggle
            if (showEmission)
                DrawProperty(layerIndex, () => currentData.EmissionEnabled = GUILayout.Toggle(currentData.EmissionEnabled, new GUIContent(GetScaledText(minScaledTextWidth, "Emission", "E"), "If Emission is enabled"), "Button"));

            // Smoothness/Roughness Toggle
            if (showSR) {
                // S=0,R=1, flip the value
                DrawProperty(layerIndex, () => currentData.SmoothnessEnabled = GUILayout.Toolbar(currentData.SmoothnessEnabled ? 0 : 1, new GUIContent[] { new GUIContent(GetScaledText(minScaledTextWidth, "Smooth", "S"), "Using smoothness for material\n(Default unity material behaviour)"), new GUIContent(GetScaledText(minScaledTextWidth, "Rough", "R"), "Uses roughness for material (1 - smoothness)") }) == 0 ? true : false);
            }
        }

        /// <summary>
        /// Draws the main properties in a material section
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The material section index
        /// </param>
        protected virtual void DrawMaterialMainProperties(int layerIndex, int sectionIndex)
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            // Albedo
            Rect albedoTintRect = DrawTexture(layerIndex, sectionIndex, 0, new GUIContent("Albedo", "Albedo (RGB)"));
            DrawProperty(layerIndex, () => currentData.AlbedoTint = EditorGUI.ColorField(albedoTintRect, currentData.AlbedoTint));

            // Normal Map
            Rect normalStrengthSliderRect = DrawTexture(layerIndex, sectionIndex, 3, new GUIContent("Normal Map", "Normal (RG)"));
            if (currentData.NormalAssigned)
                DrawProperty(layerIndex, () => currentData.NormalScale = EditorGUI.FloatField(normalStrengthSliderRect, currentData.NormalScale));

            if (currentData.PackedTexture) {
                // Use metallic as packed texture
                DrawTexture(layerIndex, sectionIndex, 1, new GUIContent("Packed Texture", $"Smoothness/Roughness can be toggled above.\nIf your material is darker with this enabled, untick \"sRGB\" in the texture import settings\n\nR: Metallic\nG: Occlussion\nA: {(currentData.SmoothnessEnabled ? "Smoothness" : "Roughness")}"));

                // Occlussion slider
                // Get rects seperately to make slider same width as others
                Rect occlussionStrengthLabelRect = GUIUtilities.GetLineRect();
                Rect occlussionStrengthSliderRect = MaterialEditor.GetRectAfterLabelWidth(occlussionStrengthLabelRect);
                
                // Push out label to be inline with Packed Texture
                occlussionStrengthLabelRect.x += 30;
                occlussionStrengthLabelRect.width -= 30;

                EditorGUI.LabelField(occlussionStrengthLabelRect, "Occlussion Strength");
                DrawProperty(layerIndex, () => currentData.OcclussionStrength = EditorGUI.Slider(occlussionStrengthSliderRect, currentData.OcclussionStrength, 0, 1));
            } else {
                // Metallic
                Rect metallicSliderRect = DrawTexture(layerIndex, sectionIndex, 1, new GUIContent("Metallic", "Metallic"));
                if (!currentData.MetallicAssigned)
                    DrawProperty(layerIndex, () => currentData.Metallic = EditorGUI.Slider(metallicSliderRect, currentData.Metallic, 0, 1));
                else
                    DrawTextureChannelPicker(metallicSliderRect, layerIndex, sectionIndex, 2, 1, 0);

                // Smoothness/Roughness
                string smoothnessText = currentData.SmoothnessEnabled ? "Smoothness" : "Roughness";
                Rect smoothnessSliderRect = DrawTexture(layerIndex, sectionIndex, 2, new GUIContent(smoothnessText, $"{smoothnessText}"));
                if (!currentData.SmoothnessAssigned)
                    DrawProperty(layerIndex, () => currentData.SmoothnessRoughness = EditorGUI.Slider(smoothnessSliderRect, currentData.SmoothnessRoughness, 0, 1));
                else
                    DrawTextureChannelPicker(smoothnessSliderRect, layerIndex, sectionIndex, 1, 1, 0);

                // Occlussion Map
                Rect occlussionLabelRect = DrawTexture(layerIndex, sectionIndex, 4, new GUIContent("Occlussion", "Occlussion"));
                if (currentData.OcclussionAssigned) {
                    Rect sliderRect = occlussionLabelRect;
                    sliderRect.width -= CHANNEL_PICKER_WIDTH + 5;

                    DrawProperty(layerIndex, () => currentData.OcclussionStrength = EditorGUI.Slider(sliderRect, currentData.OcclussionStrength, 0, 1));
                    DrawTextureChannelPicker(occlussionLabelRect, layerIndex, sectionIndex, 1, 2, 0);
                }
            }

            // Emission
            if (currentData.EmissionEnabled) {
                bool prevEmissionAssigned = currentData.EmissionAssigned;

                // Change emission colour to white if texture assigned and texture is black
                EditorGUI.BeginChangeCheck();
                Rect emissionColourRect = DrawTexture(layerIndex, sectionIndex, 5, new GUIContent("Emission", "Emission (RGB)"));
                DrawProperty(layerIndex, () => currentData.EmissionColour = EditorGUI.ColorField(emissionColourRect, GUIContent.none, currentData.EmissionColour, true, false, true));
                if (EditorGUI.EndChangeCheck()) {
                    // Update assigned textures since the function can be changed in child classes
                    UpdateAssignedTextures(layerIndex, sectionIndex);

                    // If texture just assigned and colour is black, change colour to white
                    if (currentData.EmissionAssigned && !prevEmissionAssigned) {
                        Color blackColor = new Color(0, 0, 0, currentData.EmissionColour.a);
                        if (currentData.EmissionColour == blackColor) {
                            currentData.EmissionColour = Color.white;
                        }
                    }
                }
            }

            // Alpha Clipping
            MaterialProperty surfaceTypeProp = FindProperty("_SurfaceTypeSetting");
            if (surfaceTypeProp.floatValue == 1.0f)
                DrawProperty(layerIndex, () => currentData.AlphaClipping = EditorGUI.Slider(GUIUtilities.GetLineRect(), "Alpha Clipping", currentData.AlphaClipping, 0, 1));

            // Scale & Offset
            DrawProperty(layerIndex, () => currentData.TilingOffset = GUIUtilities.DrawTilingOffset(currentData.TilingOffset));
        }

        /// <summary>
        /// Draws the noise properties in a material section
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The section index to draw
        /// </param>
        protected virtual void DrawMaterialNoiseGUI(int layerIndex, int sectionIndex)
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            // Angle Offset
            if (_materialProperties.NoiseQuality == ENoiseQuality.High)
                DrawProperty(layerIndex, () => currentData.NoiseAngleOffset = EditorGUI.FloatField(GUIUtilities.GetLineRect(), "Noise Angle Offset", currentData.NoiseAngleOffset));

            // Scale Randomising
            if (currentData.RandomiseNoiseScaling) {
                // Scale
                DrawProperty(layerIndex, () => currentData.NoiseScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), "Noise Scale", currentData.NoiseScale));

                // Scaling Min Max
                EditorGUI.BeginChangeCheck();
                DrawProperty(layerIndex, () => currentData.NoiseScalingMinMax = GUIUtilities.DrawVector2Field(currentData.NoiseScalingMinMax, new GUIContent("Noise Scaling Min Max", "(x: Min Scale, y: Max Scale)\n\nRange that each voronoi cell is randomly scaled by")));
                if (EditorGUI.EndChangeCheck()) {
                    if (currentData.NoiseScalingMinMax.x < 0) currentData.NoiseScalingMinMax.x = 0;
                    if (currentData.NoiseScalingMinMax.y < 0) currentData.NoiseScalingMinMax.y = 0;
                }
            }

            // Rotation Randomising
            if (currentData.RandomiseNoiseRotation)
                DrawProperty(layerIndex, () => currentData.NoiseRandomiseRotationMinMax = GUIUtilities.DrawVector2Field(currentData.NoiseRandomiseRotationMinMax, new GUIContent("Random Rotation Min Max", "(x: Min Rotation Degrees, y: Max Rotation Degrees)\n\nRange that each voronoi cell is randomly rotated by")));
        }


        /// <summary>
        /// Draws the variation properties in a material section
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="sectionIndex">
        /// The material section index
        /// </param>
        protected virtual void DrawMaterialVariationProperties(int layerIndex, int sectionIndex)
        {
            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, sectionIndex);

            // Variation Mode
            EVariationType prevVariationMode = currentData.VariationMode;
            EditorGUI.BeginChangeCheck();
            DrawProperty(layerIndex, () => currentData.VariationMode = (EVariationType)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), new GUIContent("Variation Mode", "Using a custom texture can cause visible tiling"), currentData.VariationMode));
            if (EditorGUI.EndChangeCheck() && currentData.VariationMode != prevVariationMode)
                UpdateVariationTexture(layerIndex, sectionIndex, prevVariationMode);

            // Opacity
            DrawProperty(layerIndex, () => currentData.VariationOpacity = EditorGUI.Slider(GUIUtilities.GetLineRect(), new GUIContent("Opacity", "Transparency of the variation"), currentData.VariationOpacity, 0, 1));

            // Brightness
            DrawProperty(layerIndex, () => currentData.VariationBrightness = EditorGUI.Slider(GUIUtilities.GetLineRect(), new GUIContent("Brightness", "Intensity of the variation"), currentData.VariationBrightness, 0, 1));

            // Scaling
            DrawProperty(layerIndex, () => currentData.VariationSmallScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), new GUIContent("Small Scale", "Scale of the small variation sample"), currentData.VariationSmallScale));
            DrawProperty(layerIndex, () => currentData.VariationMediumScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), new GUIContent("Medium Scale", "Scale of the medium variation sample"), currentData.VariationMediumScale));
            DrawProperty(layerIndex, () => currentData.VariationLargeScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), new GUIContent("Large Scale", "Scale of the large variation sample"), currentData.VariationLargeScale));

            if (currentData.VariationMode != EVariationType.CustomTexture) { // Noise
                // Strength
                DrawProperty(layerIndex, () => currentData.VariationNoiseStrength = EditorGUI.FloatField(GUIUtilities.GetLineRect(), "Noise Strength", currentData.VariationNoiseStrength));

                // Scale
                DrawProperty(layerIndex, () => currentData.VariationNoiseScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), "Noise Scale", currentData.VariationNoiseScale));

                // Offset
                DrawProperty(layerIndex, () => currentData.VariationNoiseOffset = GUIUtilities.DrawVector2Field(currentData.VariationNoiseOffset, new GUIContent("Noise Offset")));
            } else {
                // Texture
                Rect channelPickerRect = DrawTexture(layerIndex, sectionIndex, 6, new GUIContent("Variation Texture", "Variation (R), other channels are ignored\n\nTexture that is drawn onto other materials, can cause visible tiling"));
                if (currentData.VariationAssigned)
                    DrawTextureChannelPicker(channelPickerRect, layerIndex, sectionIndex, 0, 1, 0);

                // Tiling & Offset
                DrawProperty(layerIndex, () => currentData.VariationTextureTO = GUIUtilities.DrawTilingOffset(currentData.VariationTextureTO, "Variation Tiling", "Variation Offset"));
            }
        }
        #endregion

        #region Materials GUI
        /// <summary>
        /// Draws the base material GUI
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="propertiesPrefix">
        /// The material property prefix for the material
        /// </param>
        protected virtual void DrawBaseMaterialGUI(int layerIndex, string propertiesPrefix = "")
        {
            GUIUtilities.BeginBackgroundVertical();

            DrawMaterialGUI(layerIndex, 0, "Base Material");

            GUIUtilities.EndBackgroundVertical();
        }

        /// <summary>
        /// Draws the distance blend GUI
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="propertiesPrefix">
        /// The material property prefix for the material
        /// </param>
        protected virtual void DrawDistanceBlendGUI(int layerIndex, string propertiesPrefix = "")
        {
            RepetitionlessLayerData layerData = GetLayerData(layerIndex);

            // Start Background
            GUIUtilities.BeginBackgroundVertical();

            // Distance Blend Enabled Toggle
            EditorGUI.BeginChangeCheck();
            DrawProperty(layerIndex, () => layerData.DistanceBlendEnabled = GUIUtilities.DrawMajorToggleButton(layerData.DistanceBlendEnabled, "Distance Blending"));
            if (EditorGUI.EndChangeCheck())
                UpdateDistanceBlendKeyword();

            // Draw distance blending settings
            if (layerData.DistanceBlendEnabled) {
                GUILayout.Space(5);

                // Distance Blend Mode
                DrawProperty(layerIndex, () => layerData.DistanceBlendMode = (EDistanceBlendMode)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), new GUIContent("Blend Mode", "Tiling & Offset: Resamples materials with defined Tiling & Offset\nMaterial: Samples far material"), layerData.DistanceBlendMode));

                // Distance Blend Min Max
                EditorGUI.BeginChangeCheck();
                DrawProperty(layerIndex, () => layerData.DistanceBlendMinMax = GUIUtilities.DrawVector2Field(layerData.DistanceBlendMinMax, new GUIContent("Distance Blend Min Max", "(x: Min Distance, y: Max Distance)\n\nBlend distance which the material will be sampled. Materials will be blended with regular material at min, and far material at max")));
                if (EditorGUI.EndChangeCheck()) {
                    if (layerData.DistanceBlendMinMax.x < 0) layerData.DistanceBlendMinMax.x = 0;
                    if (layerData.DistanceBlendMinMax.y < 0) layerData.DistanceBlendMinMax.y = 0;
                }

                int sectionIndex = 1;
                switch (layerData.DistanceBlendMode) {
                    case EDistanceBlendMode.TilingOffset:
                        // Tiling & Offset GUI
                        RepetitionlessMaterialData farMaterialData = GetMaterialData(layerIndex, sectionIndex);
                        DrawProperty(layerIndex, () => farMaterialData.TilingOffset = GUIUtilities.DrawTilingOffset(farMaterialData.TilingOffset));
                        break;
                    case EDistanceBlendMode.Material:
                        GUILayout.Space(10);

                        // Material GUI
                        DrawMaterialGUI(layerIndex, sectionIndex, "Far Material");
                        break;
                }
            }

            // End Background
            GUIUtilities.EndBackgroundVertical();
        }

        /// <summary>
        /// Draws the blend material GUI
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to draw
        /// </param>
        /// <param name="propertiesPrefix">
        /// The material property prefix for the material
        /// </param>
        protected virtual void DrawMaterialBlendGUI(int layerIndex, string propertiesPrefix = "")
        {
            RepetitionlessLayerData layerData = GetLayerData(layerIndex);

            // Start Background
            GUIUtilities.BeginBackgroundVertical();

            // Material Blend Enabled Toggle
            EditorGUI.BeginChangeCheck();
            DrawProperty(layerIndex, () => layerData.MaterialBlendEnabled = GUIUtilities.DrawMajorToggleButton(layerData.MaterialBlendEnabled, "Material Blending"));
            if (EditorGUI.EndChangeCheck()) {
                UpdateMaterialBlendKeyword();

                if (layerData.BlendMaskType == EMaskType.CustomTexture)
                    UpdateBlendMaskTexture(layerIndex, EMaskType.PerlinNoise, !layerData.MaterialBlendEnabled);
            }

            if (layerData.MaterialBlendEnabled) {
                // Mask
                GUIUtilities.DrawHeaderLabelLarge("Mask");

                EMaskType prevMaskType = layerData.BlendMaskType;
                EditorGUI.BeginChangeCheck();
                DrawProperty(layerIndex, () => layerData.BlendMaskType = (EMaskType)EditorGUI.EnumPopup(GUIUtilities.GetLineRect(), "Mask Type", layerData.BlendMaskType));
                if (EditorGUI.EndChangeCheck() && layerData.BlendMaskType != prevMaskType)
                    UpdateBlendMaskTexture(layerIndex, prevMaskType);

                DrawProperty(layerIndex, () => layerData.BlendMaskOpacity = EditorGUI.Slider(GUIUtilities.GetLineRect(), new GUIContent("Mask Opacity", "Opacity of the mask and in response the blend material"), layerData.BlendMaskOpacity, 0, 1));
                DrawProperty(layerIndex, () => layerData.BlendMaskStrength = EditorGUI.FloatField(GUIUtilities.GetLineRect(), new GUIContent("Mask Strength", "The higher the value, the sharper the edges and vice versa"), layerData.BlendMaskStrength));

                switch (layerData.BlendMaskType) {
                    case EMaskType.PerlinNoise:
                    case EMaskType.SimplexNoise: {
                        // Scale & Offset
                        DrawProperty(layerIndex, () => layerData.BlendMaskNoiseScale = EditorGUI.FloatField(GUIUtilities.GetLineRect(), "Noise Scale", layerData.BlendMaskNoiseScale));
                        DrawProperty(layerIndex, () => layerData.BlendMaskNoiseOffset = GUIUtilities.DrawVector2Field(layerData.BlendMaskNoiseOffset, new GUIContent("Noise Offset")));

                        break;
                    }
                    case EMaskType.CustomTexture: {
                        // Texture
                        EditorGUI.BeginChangeCheck();
                        _textureData.BMTexturesDrawer.DrawTexture(0, 0, new GUIContent("Blend Mask", "Blend Mask (R), other channels are ignored\n\nTexture that is sampled as the mask for the blend material. Color from black-white represents opacity (0-1)"));
                        if (EditorGUI.EndChangeCheck()) {
                            UpdateAssignedTextures(layerIndex, 2);
                        }

                        // Scale & Offset
                        DrawProperty(layerIndex, () => layerData.BlendMaskTextureTO = GUIUtilities.DrawTilingOffset(layerData.BlendMaskTextureTO));

                        break;
                    }
                    case EMaskType.VertexColour: {
                        DrawProperty(layerIndex, () => layerData.BlendMaskVertexColour = EditorGUI.ColorField(GUIUtilities.GetLineRect(), new GUIContent("Mask Colour", "The colour used as a mask in the vertex colour"), layerData.BlendMaskVertexColour));
                        DrawProperty(layerIndex, () => {
                            Vector2 newThreshold = GUIUtilities.DrawVector2Field(layerData.BlendMaskVertexColourThreshold, new GUIContent("Colour Threshold", "The smoothstep min max for the colour"));
                            
                            // Lock x/y between 0-1
                            if (newThreshold.x < 0) newThreshold.x = 0;
                            if (newThreshold.x > 1) newThreshold.x = 1;
                            if (newThreshold.y < 0) newThreshold.y = 0;
                            if (newThreshold.y > 1) newThreshold.y = 1;

                            layerData.BlendMaskVertexColourThreshold = newThreshold;
                        });

                        break;
                    }
                }

                GUILayout.Space(10);

                // Distance Blending
                if (layerData.DistanceBlendEnabled) {
                    // Calculate scaled text min width
                    int minScaledTextWidth = 0;
                    minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Override Distance Blending")).x;
                    if (layerData.OverrideDistanceBlend && layerData.DistanceBlendMode == EDistanceBlendMode.TilingOffset)
                        minScaledTextWidth += (int)GUI.skin.button.CalcSize(new GUIContent("Override Tiling & Offset")).x;

                    // Header
                    GUIUtilities.DrawHeaderLabelLarge("Distance Blending");

                    GUILayout.BeginHorizontal();

                    // Override Distance Blending Toggle
                    string distanceBlendEnabledStyle = layerData.OverrideDistanceBlend && layerData.DistanceBlendMode == 0 ? "ButtonLeft" : "Button";
                    DrawProperty(layerIndex, () => layerData.OverrideDistanceBlend = GUILayout.Toggle(layerData.OverrideDistanceBlend, new GUIContent(GetScaledText(minScaledTextWidth, "Override Distance Blending", "ODB"), "Draws the blend material on top of the far material"), distanceBlendEnabledStyle));

                    // Override Tiling & Offset Options
                    bool endedHorizontal = false;
                    if (layerData.OverrideDistanceBlend) {
                        // Override Tiling & Offset Toggle
                        DrawProperty(layerIndex, () => layerData.OverrideDistanceBlendTO = GUILayout.Toggle(layerData.OverrideDistanceBlendTO, new GUIContent(GetScaledText(minScaledTextWidth, "Override Tiling & Offset", "OTO"), "Changes the tiling offset of the blend material at a distance"), "ButtonRight"));

                        endedHorizontal = true;
                        GUILayout.EndHorizontal();

                        // Override Tiling & Offset
                        if (layerData.OverrideDistanceBlendTO)
                            DrawProperty(layerIndex, () => layerData.BlendMaskDistanceTO = GUIUtilities.DrawTilingOffset(layerData.BlendMaskDistanceTO));
                    }

                    if (!endedHorizontal)
                        GUILayout.EndHorizontal();

                    GUILayout.Space(10);
                }

                // Material
                DrawMaterialGUI(layerIndex, 2, "Blend Material");
            }

            // End Background
            GUIUtilities.EndBackgroundVertical();
        }
        #endregion

        #region Debug GUI
        /// <summary>
        /// Draws the debug selection GUI
        /// </summary>
        protected virtual void DrawDebugGUI()
        {
            // Start Background
            GUIUtilities.BeginBackgroundVertical();

            // Material Property
            MaterialProperty debuggingIndexProp = FindProperty("_DebuggingIndex");

            // Debug Toggle
            bool prevDebugging = debuggingIndexProp.floatValue != -1;

            bool debugging = GUIUtilities.DrawMajorToggleButton(prevDebugging, "Debug");

            if (debugging) {
                GUILayout.Space(5);

                // If just started debugging, get previous debugging index
                if (!prevDebugging) {
                    debuggingIndexProp.floatValue = _prevDebugIndex;
                    _materialProperties.CallOnExternalDataChanged();
                }

                // Title Label
                GUIUtilities.DrawHeaderLabelLarge("Debug Texture");

                // Selection Grid
                string[] debugValues = new string[] {
                    "Voronoi Cells",
                    "Edge Mask",
                    "Distance Mask",
                    "Blend Material Mask",
                    "Variation Multiplier"
                };

                EditorGUI.BeginChangeCheck();
                float debuggingIndex = debuggingIndexProp.floatValue;
                debuggingIndex = GUILayout.SelectionGrid((int)debuggingIndex, debugValues, 1);
                if (EditorGUI.EndChangeCheck()) {
                    debuggingIndexProp.floatValue = debuggingIndex;

                    _materialProperties.CallOnExternalDataChanged();
                }
            } else if (debuggingIndexProp.floatValue != -1) {
                _prevDebugIndex = (int)debuggingIndexProp.floatValue;
                debuggingIndexProp.floatValue = -1;

                _materialProperties.CallOnExternalDataChanged();
            }

            // End Background
            GUIUtilities.EndBackgroundVertical();
        }
        #endregion
    }
}
#endif
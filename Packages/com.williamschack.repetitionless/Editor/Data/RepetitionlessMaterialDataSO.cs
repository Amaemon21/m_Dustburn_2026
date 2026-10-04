using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Data
{
    /// <summary>
    /// Stores the material properties for a Repetitionless material<br />
    /// Creates and manages a texture storing these properties that will be passed to the shader
    /// </summary>
    public class RepetitionlessMaterialDataSO : ScriptableObject
    {
        /// <summary>
        /// The properties texture material property
        /// </summary>
        public const string PROPERTIES_TEXTURE_PROP_NAME = "_PropertiesTexture";

        private const TextureFormat DATA_TEXTURE_FORMAT = TextureFormat.RGBAHalf;

        /// <summary>
        /// The data for the material<br />
        /// Do not update this in the scriptable object, do it in the material inspector
        /// </summary>
        [HideInInspector] public RepetitionlessLayerData[] Data;
        [HideInInspector][SerializeField] private RepetitionlessLayerDataCompressed[] _dataCompressed;

        private MaterialDataManager _dataManagerCache;
        private MaterialDataManager _dataManager {
            get {
                if (_dataManagerCache != null)
                    return _dataManagerCache;

                _dataManagerCache = new MaterialDataManager(this);
                return _dataManagerCache;
            }
        }

        [HideInInspector][SerializeField] private ENoiseQuality _noiseQuality = ENoiseQuality.Medium;
        internal ENoiseQuality NoiseQuality {
            get { return _noiseQuality; }
            set { SetNoiseQuality(value); }
        }

        [HideInInspector][SerializeField] private Vector4 _globalTilingOffset = new Vector4(1, 1, 0, 0);

        /// <summary>
        /// The global tiling offset that is being used for all the data
        /// </summary>
        [HideInInspector] public Vector4 GlobalTilingOffset {
            get => _globalTilingOffset;
            set => SetGlobalTilingOffset(value);
        }

        /// <summary>
        /// This is called by other classes when they have changed properties and manually called this<br />
        /// It is not called when regular properties are changed by this class
        /// </summary>
        public System.Action OnExternalDataChanged;

        /// <summary>
        /// Initializes this with a new set of data
        /// </summary>
        /// <param name="layerCount">
        /// The max amount of terrain layers that will be used
        /// </param>
        public void Init(int layerCount)
        {
            Data = new RepetitionlessLayerData[layerCount];
            for (int i = 0; i < layerCount; i++)
                Data[i] = new RepetitionlessLayerData();
            
            _dataCompressed = new RepetitionlessLayerDataCompressed[layerCount];
            for (int i = 0; i < layerCount; i++)
                _dataCompressed[i] = new RepetitionlessLayerDataCompressed();

            Save();
        }

        public void InitNewLayerCount(int layerCount)
        {
            RepetitionlessLayerData[] oldData = Data;
            RepetitionlessLayerDataCompressed[] oldCompressedData = _dataCompressed;

            Init(layerCount);

            for (int i = 0; i < Mathf.Min(Data.Length, oldData.Length); i++) {
                Data[i] = oldData[i];
                _dataCompressed[i] = oldCompressedData[i];
            }
            
            // Recreate the material texture
            Texture2D newMaterialTexture = CreateMaterialTexture();
            MaterialProperty materialTextureProp = MaterialEditor.GetMaterialProperty(new Object[] { _dataManager.Material }, PROPERTIES_TEXTURE_PROP_NAME);
            materialTextureProp.textureValue = newMaterialTexture;

            Save();
        }

        /// <summary>
        /// Saves this object
        /// </summary>
        public void Save()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

    #if UNITY_EDITOR
        private Color? GetDataColour(int layerIndex, int compressedFieldIndex)
        {
            return RepetitionlessDataPacker.GetLayerFieldColour(_dataCompressed[layerIndex], compressedFieldIndex);
        }

        private Color[] GetLayerDataColour(int layerIndex)
        {
            Color[] dataColours = new Color[Constants.COMPRESSED_LAYER_VARIABLES_COUNT];
            for (int i = 0; i < dataColours.Length; i++) {
                dataColours[i] = GetDataColour(layerIndex, i).Value;
            }

            return dataColours;
        }

        /// <summary>
        /// Sets the noise quality
        /// </summary>
        /// <param name="noiseQuality">
        /// The new quality to use
        /// </param>
        public void SetNoiseQuality(ENoiseQuality noiseQuality)
        {
            _noiseQuality = noiseQuality;
            Save();
        }

        /// <summary>
        /// Sets the global tiling and offset for every layer
        /// </summary>
        /// <param name="tilingOffset">
        /// The new tiling offset to use
        /// </param>
        public void SetGlobalTilingOffset(Vector4 tilingOffset)
        {
            _globalTilingOffset = tilingOffset;

            foreach (RepetitionlessLayerData layerData in Data) {
                layerData.GlobalTilingOffset = _globalTilingOffset;
            }

            Save();
        }

        /// <summary>
        /// Invokes the OnForcedDataChanged callback
        /// </summary>
        public void CallOnExternalDataChanged()
        {
            OnExternalDataChanged?.Invoke();
        }

        /// <summary>
        /// Updates the properties texture with the data saved in this object
        /// </summary>
        /// <param name="material">
        /// The material that will have its texture property updated
        /// </param>
        /// <param name="layerIndex">
        /// The layer that will be updated
        /// </param>
        public void UpdateMaterialTexture(Material material, int layerIndex)
        {
            UpdateMaterialTexture(material, PROPERTIES_TEXTURE_PROP_NAME, layerIndex);
        }

        /// <summary>
        /// Updates the properties texture with the data saved in this object
        /// </summary>
        /// <param name="material">
        /// The material that will have its texture property updated
        /// </param>
        /// <param name="texturePropertyName">
        /// The name of the properties texture property
        /// </param>
        /// <param name="layerIndex">
        /// The layer that will be updated
        /// </param>
        public void UpdateMaterialTexture(Material material, string texturePropertyName, int layerIndex)
        {
            MaterialProperty textureProp = MaterialEditor.GetMaterialProperty(new Object[] { material }, texturePropertyName);
            UpdateMaterialTexture(textureProp, layerIndex);
        }

        /// <summary>
        /// Updates the properties texture with the data saved in this object
        /// </summary>
        /// <param name="property">
        /// The properties texture property that will be updated
        /// </param>
        /// <param name="layerIndex">
        /// The layer that will be updated
        /// </param>
        public void UpdateMaterialTexture(MaterialProperty property, int layerIndex)
        {
#if UNITY_6000_2_OR_NEWER
            if (property.propertyType != UnityEngine.Rendering.ShaderPropertyType.Texture)
#else
            if (property.type != MaterialProperty.PropType.Texture)
#endif
            {
                Debug.LogError("Property type must be a texture");
                return;
            }

            bool textureExists = _dataManager.AssetExists(Constants.PROPERTIES_TEXTURE_ASSET_NAME);

            Texture2D texture = null;
            if (textureExists)
                texture = _dataManager.LoadAsset<Texture2D>(Constants.PROPERTIES_TEXTURE_ASSET_NAME);

            if (textureExists && texture.height == Data.Length) {
                // Check if the width is the same, it may have less variables from a previous version
                if (texture.width != Constants.COMPRESSED_LAYER_VARIABLES_COUNT) {
                    // Recreate texture with new width
                    _dataManager.DeleteAsset(Constants.PROPERTIES_TEXTURE_ASSET_NAME);
                    UpdateMaterialTexture(property, layerIndex);
                    return;
                }

                for (int i = 0; i < Constants.COMPRESSED_LAYER_VARIABLES_COUNT; i++) {
                    int fieldChangedIndex = RepetitionlessDataPacker.UpdateCompressedLayerData(ref _dataCompressed[layerIndex], Data[layerIndex]);

                    if (fieldChangedIndex == -1)
                        break;

                    Color? dataColour = GetDataColour(layerIndex, fieldChangedIndex);
                
                    // The value has not changed
                    if (!dataColour.HasValue)
                        continue;

                    texture.SetPixel(fieldChangedIndex, layerIndex, dataColour.Value);
                    texture.Apply();
                }
            } else {
                texture = CreateMaterialTexture();
            }

            if ((Texture2D)property.textureValue != texture)
                property.textureValue = texture;
        }

        private Texture2D CreateMaterialTexture()
        {
            // Create a new texture
            Texture2D texture = new Texture2D(Constants.COMPRESSED_LAYER_VARIABLES_COUNT, Data.Length, DATA_TEXTURE_FORMAT, false);

            Color[] dataColours = new Color[Constants.COMPRESSED_LAYER_VARIABLES_COUNT * Data.Length];
            for (int i = 0; i < Data.Length; i++) {
                // Compress all values
                RepetitionlessDataPacker.UpdateCompressedLayerData(ref _dataCompressed[i], Data[i], true);
                //Color[] layerDataColours = GetLayerDataColour(layerIndex);
                Color[] layerDataColours = GetLayerDataColour(i);

                // Add colours to the main array
                int coloursOffset = Constants.COMPRESSED_LAYER_VARIABLES_COUNT * i;
                for (int x = 0; x < Constants.COMPRESSED_LAYER_VARIABLES_COUNT; x++) {
                    dataColours[coloursOffset + x] = layerDataColours[x];
                }
            }

            texture.SetPixels(dataColours);
            texture.Apply();

            if (_dataManager.AssetExists(Constants.PROPERTIES_TEXTURE_ASSET_NAME))
                _dataManager.DeleteAsset(Constants.PROPERTIES_TEXTURE_ASSET_NAME);

            _dataManager.CreateAsset(texture, Constants.PROPERTIES_TEXTURE_ASSET_NAME);

            return texture;
        }

        /// <summary>
        /// Gets the properties for a specific material
        /// </summary>
        /// <param name="layerIndex">
        /// The layer that the material is in
        /// </param>
        /// <param name="materialIndex">
        /// The index of the material:<br />
        /// 0: Base, 1: Far, 2: Blend
        /// </param>
        /// <returns>
        /// The properties for a specific material
        /// </returns>
        public RepetitionlessMaterialData GetMaterialData(int layerIndex, int materialIndex)
        {
            RepetitionlessMaterialData currentData = Data[layerIndex].BaseMaterialData;
            switch (materialIndex) {
              //case 0: currentData = Data[layerIndex].BaseMaterialData;  break;
                case 1: currentData = Data[layerIndex].FarMaterialData;   break;
                case 2: currentData = Data[layerIndex].BlendMaterialData; break; 
            }

            return currentData;
        }

        /// <summary>
        /// Updates the assigned textures and the property texture based on a texture data
        /// </summary>
        /// <param name="material">
        /// The material that will have its texture property updated 
        /// </param>
        /// <param name="textureData">
        /// The texture data that assigned textures will be read from
        /// </param>
        /// <param name="layerIndex">
        /// The layer that the material is in
        /// </param>
        /// <param name="materialIndex">
        /// The index of the material:<br />
        /// 0: Base, 1: Far, 2: Blend
        /// </param>
        public void UpdateAssignedTextures(Material material, RepetitionlessTextureDataSO textureData, int layerIndex, int materialIndex)
        {
            RepetitionlessTextureDataSO.MaterialTextureData materialTextureData = textureData.GetMaterialTextureData(layerIndex, materialIndex);

            RepetitionlessMaterialData currentData = GetMaterialData(layerIndex, materialIndex);
            bool packedTextureAssigned = currentData.PackedTexture ? materialTextureData.NSOTextures[3].Texture != null : false;

            currentData.AlbedoAssigned        = materialTextureData.AVTextures[0].Texture != null;
            currentData.MetallicAssigned      = packedTextureAssigned ? true : materialTextureData.EMTextures[1].Texture != null;
            currentData.SmoothnessAssigned    = packedTextureAssigned ? true : materialTextureData.NSOTextures[1].Texture != null;
            currentData.NormalAssigned        = materialTextureData.NSOTextures[0].Texture != null;
            currentData.OcclussionAssigned    = packedTextureAssigned ? true : materialTextureData.NSOTextures[2].Texture != null;
            currentData.EmissionAssigned      = materialTextureData.EMTextures[0].Texture != null;
            currentData.VariationAssigned     = materialTextureData.AVTextures[1].Texture != null;
            currentData.PackedTextureAssigned = packedTextureAssigned;

            Data[layerIndex].BlendMaskAssigned = textureData.LayersTextureData[layerIndex].BlendMaskTexture[0].Texture != null;

            Save();

            UpdateMaterialTexture(material, layerIndex);
        }
    #endif
    }
}
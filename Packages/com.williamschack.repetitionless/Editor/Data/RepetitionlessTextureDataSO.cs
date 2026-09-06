using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Repetitionless.Runtime.Compression;

namespace Repetitionless.Editor.Data
{
    using Utilities.GUI;
    using Utilities.Texture;

    /// <summary>
    /// Stores the textures for a RepetitionlessMaterial<br />
    /// Handles drawing and updating the texture arrays
    /// </summary>
    public class RepetitionlessTextureDataSO : ScriptableObject
    {
        private const TextureFormat TEXTURE_FORMAT = TextureFormat.RGBA64;

        /// <summary>
        /// The material property name of the array assigned textures texture
        /// </summary>
        public const string TEXTURE_PROP_NAME = "_AssignedTexturesTexture";

        private const string AV_TEXTURES_PROP_NAME  = "_AVTextures";
        private const string NSO_TEXTURES_PROP_NAME = "_NSOTextures";
        private const string EM_TEXTURES_PROP_NAME  = "_EMTextures";
        private const string BM_TEXTURES_PROP_NAME  = "_BMTextures";

        private static readonly Vector4 DEFAULT_AV_COLOUR  = new Vector4 ( 1.0f, 1.0f, 1.0f, 0.0f );
        private static readonly Vector4 DEFAULT_NSO_COLOUR = new Vector4 ( 0.0f, 0.0f, 0.0f, 1.0f );
        private static readonly Vector4 DEFAULT_EM_COLOUR  = new Vector4 ( 1.0f, 1.0f, 1.0f, 0.0f );
        private static readonly Vector4 DEFAULT_BM_COLOUR  = new Vector4 ( 1.0f, 0.0f, 0.0f, 0.0f);

        /// <summary>
        /// Contains the texture data for a repetitionless material
        /// </summary>
        [System.Serializable]
        public struct MaterialTextureData
        {
            /// <summary>
            /// The AVTextures: Albedo (rgb), Variation (a)
            /// </summary>
            public TexturePacker.TextureData[] AVTextures;
            /// <summary>
            /// The NSOTextures: Normal (rg), Smoothness (b), Occlussion (a)
            /// </summary>
            public TexturePacker.TextureData[] NSOTextures;
            /// <summary>
            /// The EMTextures: Emission (rgb), Metallic (a)
            /// </summary>
            public TexturePacker.TextureData[] EMTextures;
        }

        /// <summary>
        /// Contains the texture data for a repetitionless layer
        /// </summary>
        [System.Serializable]
        public class LayerTextureData
        {
            /// <summary>
            /// The base material textures
            /// </summary>
            public MaterialTextureData BaseMaterialTextures;

            /// <summary>
            /// The far material textures
            /// </summary>
            public MaterialTextureData FarMaterialTextures;
            /// <summary>
            /// The blend material textures
            /// </summary>
            public MaterialTextureData BlendMaterialTextures;

            /// <summary>
            /// The blend mask texture for the layer<br />
            /// Stored in an array to make it easier to pass into the texture drawer
            /// </summary>
            public TexturePacker.TextureData[] BlendMaskTexture;
        }

        /// <summary>
        /// The data for the textures
        /// </summary>
        public LayerTextureData[] LayersTextureData;

        [HideInInspector][SerializeField] private int[] _assignedAVTextures = new int[3];
        [HideInInspector][SerializeField] private int[] _assignedNSOTextures = new int[3];
        [HideInInspector][SerializeField] private int[] _assignedEMTextures = new int[3];
        [HideInInspector][SerializeField] private int _assignedBMTextures = 0;

        /// <summary>
        /// Which colour space the textures were previously packed in<br />
        /// Used when switching colour spaces to determine which textures need to be repacked
        /// </summary>
        [HideInInspector] public ColorSpace PackedColourSpace = ColorSpace.Uninitialized;

        // Non-Serializable
        private MaterialDataManager _dataManagerCache;
        private MaterialDataManager _dataManager {
            get {
                if (_dataManagerCache?.Material != null)
                    return _dataManagerCache;

                _dataManagerCache = new MaterialDataManager(this);
                return _dataManagerCache;
            }
        }

        /// <summary>
        /// The AVTextures drawer<br />
        /// Not serialized, needs to be setup with SetupTextureDrawers for each session using this
        /// </summary>
        public TextureArrayCustomChannelsGUIDrawer AVTexturesDrawer;
        /// <summary>
        /// The NSOTextures drawer<br />
        /// Not serialized, needs to be setup with SetupTextureDrawers for each session using this
        /// </summary>
        public TextureArrayCustomChannelsGUIDrawer NSOTexturesDrawer;
        /// <summary>
        /// The EMTextures drawer<br />
        /// Not serialized, needs to be setup with SetupTextureDrawers for each session using this
        /// </summary>
        public TextureArrayCustomChannelsGUIDrawer EMTexturesDrawer;
        /// <summary>
        /// The BMTextures drawer<br />
        /// Not serialized, needs to be setup with SetupTextureDrawers for each session using this
        /// </summary>
        public TextureArrayCustomChannelsGUIDrawer BMTexturesDrawer;

        /// <summary>
        /// Callback for when any of the textures are changed
        /// </summary>
        public System.Action OnDataChanged;

        /// <summary>
        /// Initializes this with a new set of texture data
        /// </summary>
        /// <param name="layersCount">
        /// The max amount of terrain layers that will be used
        /// </param>
        public void Init(int layerCount)
        {
            LayersTextureData = new LayerTextureData[layerCount];

            for (int i = 0; i < layerCount; i++) {
                SetupLayer(i);
            }

            Save();
        }

        public void InitNewLayerCount(int layerCount)
        {
            LayerTextureData[] oldLayerTextureData = LayersTextureData;

            Init(layerCount);

            for (int i = 0; i < Mathf.Min(LayersTextureData.Length, oldLayerTextureData.Length); i++) {
                LayersTextureData[i] = oldLayerTextureData[i];
            }

            Save();
        }

        /// <summary>
        /// Initializes a repetitionless layer with new texture data
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index to setup
        /// </param>
        public void SetupLayer(int layerIndex)
        {
            LayersTextureData[layerIndex] = new LayerTextureData();

            SetupMaterial(ref LayersTextureData[layerIndex].BaseMaterialTextures);
            SetupMaterial(ref LayersTextureData[layerIndex].FarMaterialTextures);
            SetupMaterial(ref LayersTextureData[layerIndex].BlendMaterialTextures);

            LayersTextureData[layerIndex].BlendMaskTexture = new TexturePacker.TextureData[1];
            LayersTextureData[layerIndex].BlendMaskTexture[0] = new TexturePacker.TextureData() {
                Disabled = false,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.R
                    )
                }
            };
        }

        /// <summary>
        /// Initializes a repetitionless material with new texture data
        /// </summary>
        /// <param name="data">
        /// A reference to the material texture data being initialized
        /// </param>
        public void SetupMaterial(ref MaterialTextureData data)
        {
            // Setup Textures

            // Includes packed texture at indexes (Disabled by default):
            // NSOTextures[3], EMTextures[2]

            data = new MaterialTextureData();

            data.AVTextures = new TexturePacker.TextureData[2];
            data.NSOTextures = new TexturePacker.TextureData[4];
            data.EMTextures = new TexturePacker.TextureData[3];

            // Albedo
            data.AVTextures[0] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = false,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.R
                    ),
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.G,
                        TexturePacker.TextureChannel.G
                    ),
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.B,
                        TexturePacker.TextureChannel.B
                    )
                }
            };
            
            // Variation
            data.AVTextures[1] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.A
                    )
                }
            };

            // Normal
            data.NSOTextures[0] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = true,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.R
                    ),
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.G,
                        TexturePacker.TextureChannel.G
                    )
                } 
            };

            // Smoothness / Roughness
            data.NSOTextures[1] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.B
                    )
                }
            };

            // Occlussion
            data.NSOTextures[2] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.A
                    )
                }
            };

            // Packed Texture (Occlussion, Smoothness)
            data.NSOTextures[3] = new TexturePacker.TextureData() {
                Disabled = true,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    // Occlussion
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.G,
                        TexturePacker.TextureChannel.A
                    ),
                    // Smoothness
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.A,
                        TexturePacker.TextureChannel.B
                    )
                }
            };

            // Emission
            data.EMTextures[0] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.R
                    ),
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.G,
                        TexturePacker.TextureChannel.G
                    ),
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.B,
                        TexturePacker.TextureChannel.B
                    )
                }
            };

            // Metallic
            data.EMTextures[1] = new TexturePacker.TextureData() {
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.A
                    )
                }
            };

            // Packed Texture (Metallic)
            data.EMTextures[2] = new TexturePacker.TextureData() {
                Disabled = true,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        TexturePacker.TextureChannel.A
                    )
                }
            };
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

        /// <summary>
        /// Gets the textures material property based on the section index
        /// </summary>
        /// <param name="texturesIndex">
        /// The index of the textures that will be used:<br />
        /// 0: AV, 1: NSO, 2: EM, 3: BM
        /// </param>
        /// <returns>
        /// The textures material property
        /// </returns>
        private MaterialProperty GetTexturesProp(int texturesIndex)
        {
            string propName = "";
            switch (texturesIndex) {
                case 0: propName = AV_TEXTURES_PROP_NAME; break;
                case 1: propName = NSO_TEXTURES_PROP_NAME; break;
                case 2: propName = EM_TEXTURES_PROP_NAME; break;
                case 3: propName = BM_TEXTURES_PROP_NAME; break;
                default: return null;
            }

            MaterialProperty prop = MaterialEditor.GetMaterialProperty(new Object[] { _dataManager.Material }, propName);
            return prop;
        }

        /// <summary>
        /// Gets a reference to the assigned textures value for a specific section and chunk
        /// </summary>
        /// <param name="sectionIndex">
        /// The index of the textures that will be used:<br />
        /// 0: AV, 1: NSO, 2: EM, 3: BM
        /// </param>
        /// <param name="chunkIndex"></param>
        /// <returns>
        /// A reference to the assigned textures value
        /// </returns>
        private ref int GetAssignedTexturesValue(int sectionIndex, int chunkIndex)
        {
            switch (sectionIndex) {
                case 0: return ref _assignedAVTextures[chunkIndex];
                case 1: return ref _assignedNSOTextures[chunkIndex];
                case 2: return ref _assignedEMTextures[chunkIndex];
                case 3: return ref _assignedBMTextures;
            }

            return ref _assignedAVTextures[0];
        }

        private int AssignedTexturesGetter(int sectionIndex, int chunkIndex)
        {
            return GetAssignedTexturesValue(sectionIndex, chunkIndex);
        }

        private void AssignedTexturesSetter(int sectionIndex, int chunkIndex, int compressedValues)
        {
            ref int assignedTexturesVal = ref GetAssignedTexturesValue(sectionIndex, chunkIndex);
            assignedTexturesVal = compressedValues;

            UpdateAssignedTexturesTexture();
            return;
        }

        private ushort[] GetAssignedTexturesData()
        {
            // Split assigned textures value into two 16 bit integers
            // Cannot store single 32 bit integers in a texture

            int channels = 4;

            ushort[] data = new ushort[8 * channels]; // 8 pixels
            for (int section = 0; section < 4; section++) {
                // x = 0 first half, x = 1 second half
                int offsetFirst  = (section * 2 + 0) * channels;
                int offsetSecond = (section * 2 + 1) * channels;

                for (int chunk = 0; chunk < 3; chunk++) {
                    int value = GetAssignedTexturesValue(section, chunk);
                    (ushort, ushort) valueSplit = BooleanCompression.Split32BitInt(value);

                    data[offsetFirst + chunk]  = valueSplit.Item1;
                    data[offsetSecond + chunk] = valueSplit.Item2;
                }

                // Alpha is unused
                // Only including an alpha because RGB48 doesnt work sometimes?
                data[offsetFirst + 3]  = 0;
                data[offsetSecond + 3] = 0;
            }

            return data;
        }

        /// <summary>
        /// Updates the array assigned textures texture
        /// </summary>
        public void UpdateAssignedTexturesTexture()
        {
            // I would use 4 properties instead of a texture but this way i can store ints in the shader graph
            // You cannot store int properties in a shader graph and floating point precision would be an issue

            MaterialProperty textureProp = MaterialEditor.GetMaterialProperty(new Object[] { _dataManager.Material }, TEXTURE_PROP_NAME);
            UpdateAssignedTexturesTexture(textureProp);
        }

        /// <summary>
        /// Updates the array assigned textures texture
        /// </summary>
        /// <param name="property">
        /// The assigned textures property that will be updated
        /// </param>
        public void UpdateAssignedTexturesTexture(MaterialProperty property)
        {
            Texture2D texture;
            if (_dataManager.AssetExists(Constants.ARRAY_ASSIGNED_TEXTURES_ASSET_NAME)) {
                // Load and modify the texture
                texture = _dataManager.LoadAsset<Texture2D>(Constants.ARRAY_ASSIGNED_TEXTURES_ASSET_NAME);
            } else {
                // Create a new texture
                texture = new Texture2D(2, 4, TEXTURE_FORMAT, false);
                _dataManager.CreateAsset(texture, Constants.ARRAY_ASSIGNED_TEXTURES_ASSET_NAME);
            }

            ushort[] pixelData = GetAssignedTexturesData();
            texture.SetPixelData(pixelData, 0);
            texture.Apply(false);

            if ((Texture2D)property.textureValue != texture)
                property.textureValue = texture;
        }

#if UNITY_EDITOR
        private void OnTextureUpdated()
        {
            OnDataChanged?.Invoke();
        }

        private void UpdatePackedColourSpace()
        {
            
            PackedColourSpace = PlayerSettings.colorSpace;
        }
#endif

        // Should be called every time before using the drawers

        /// <summary>
        /// Initializes the texture drawers<br />
        /// These are not serialized and this must be called for each session using them
        /// </summary>
        /// <param name="dataManager">
        /// The data manager to use
        /// </param>
        public void SetupTextureDrawers()
        {
#if UNITY_EDITOR
            if (AVTexturesDrawer != null) {
                AVTexturesDrawer.OnTextureUpdated -= UpdatePackedColourSpace;

                AVTexturesDrawer.OnTextureUpdated  -= OnTextureUpdated;
                NSOTexturesDrawer.OnTextureUpdated -= OnTextureUpdated;
                EMTexturesDrawer.OnTextureUpdated  -= OnTextureUpdated;
                BMTexturesDrawer.OnTextureUpdated  -= OnTextureUpdated;
            }
#endif

            MaterialProperty avTexturesProp  = GetTexturesProp(0);
            MaterialProperty nsoTexturesProp = GetTexturesProp(1);
            MaterialProperty emTexturesProp  = GetTexturesProp(2);
            MaterialProperty bmTexturesProp  = GetTexturesProp(3);

            AVTexturesDrawer  = new TextureArrayCustomChannelsGUIDrawer(_dataManager, (int i) => { return ref GetTextureDrawerTextureData(i, 0); }, Save, (int i) => { return AssignedTexturesGetter(0, i); }, (int i, int at) => { AssignedTexturesSetter(0, i, at); }, DEFAULT_AV_COLOUR,  avTexturesProp,  LayersTextureData.Length * Constants.MATERIALS_PER_LAYER_COUNT);
            NSOTexturesDrawer = new TextureArrayCustomChannelsGUIDrawer(_dataManager, (int i) => { return ref GetTextureDrawerTextureData(i, 1); }, Save, (int i) => { return AssignedTexturesGetter(1, i); }, (int i, int at) => { AssignedTexturesSetter(1, i, at); }, DEFAULT_NSO_COLOUR, nsoTexturesProp, LayersTextureData.Length * Constants.MATERIALS_PER_LAYER_COUNT);
            EMTexturesDrawer  = new TextureArrayCustomChannelsGUIDrawer(_dataManager, (int i) => { return ref GetTextureDrawerTextureData(i, 2); }, Save, (int i) => { return AssignedTexturesGetter(2, i); }, (int i, int at) => { AssignedTexturesSetter(2, i, at); }, DEFAULT_EM_COLOUR,  emTexturesProp,  LayersTextureData.Length * Constants.MATERIALS_PER_LAYER_COUNT);
            BMTexturesDrawer  = new TextureArrayCustomChannelsGUIDrawer(_dataManager, (int i) => { return ref GetBlendMaskTextureData(i);        }, Save, (int i) => { return AssignedTexturesGetter(3, i); }, (int i, int at) => { AssignedTexturesSetter(3, i, at); }, DEFAULT_BM_COLOUR,  bmTexturesProp,  LayersTextureData.Length);

            AVTexturesDrawer.TextureFormat  = TextureFormat.BC7;
            AVTexturesDrawer.ArrayLinear    = false;

            NSOTexturesDrawer.TextureFormat = TextureFormat.BC7;
            NSOTexturesDrawer.ArrayLinear   = true;

            EMTexturesDrawer.TextureFormat  = TextureFormat.DXT5;
            EMTexturesDrawer.ArrayLinear    = true;
            
            BMTexturesDrawer.TextureFormat  = TextureFormat.BC4;
            BMTexturesDrawer.ArrayLinear    = true;

#if UNITY_EDITOR
            AVTexturesDrawer.OnTextureUpdated += UpdatePackedColourSpace;

            AVTexturesDrawer.OnTextureUpdated  += OnTextureUpdated;
            NSOTexturesDrawer.OnTextureUpdated += OnTextureUpdated;
            EMTexturesDrawer.OnTextureUpdated  += OnTextureUpdated;
            BMTexturesDrawer.OnTextureUpdated  += OnTextureUpdated;
#endif
        }

        /// <summary>
        /// Gets a reference to the material texture data saved in this object
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to retrieve data from
        /// </param>
        /// <param name="materialIndex">
        /// The material to retrieve data from:<br />
        /// 0: Base material, 1: Far material, 2: Blend material
        /// </param>
        /// <returns></returns>
        public ref MaterialTextureData GetMaterialTextureData(int layerIndex, int materialIndex)
        {
            LayerTextureData layerData = LayersTextureData[layerIndex];

            switch (materialIndex) {
                case 0: return ref layerData.BaseMaterialTextures;
                case 1: return ref layerData.FarMaterialTextures;
                case 2: return ref layerData.BlendMaterialTextures; 
            }

            return ref layerData.BaseMaterialTextures;
        }

        /// <summary>
        /// Gets a reference to the texture data saved in a material data
        /// </summary>
        /// <param name="materialData">
        /// The material data to get the texture data from
        /// </param>
        /// <param name="texturesIndex">
        /// The index of the textures that will be used:<br />
        /// 0: AV, 1: NSO, 2: EM, 3: BM
        /// </param>
        /// <returns>
        /// A reference to the texture data
        /// </returns>
        public ref TexturePacker.TextureData[] GetTextureData(ref MaterialTextureData materialData, int texturesIndex)
        {
            switch(texturesIndex) {
                case 0: return ref materialData.AVTextures;
                case 1: return ref materialData.NSOTextures;
                case 2: return ref materialData.EMTextures;
            }

            return ref materialData.AVTextures;
        }

        /// <summary>
        /// Gets a reference to the texture data saved in a material data
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to retrieve data from
        /// </param>
        /// <param name="materialIndex">
        /// The index of the material:<br />
        /// 0: Base, 1: Far, 2: Blend
        /// </param>
        /// <param name="texturesIndex">
        /// The index of the textures that will be used:<br />
        /// 0: AV, 1: NSO, 2: EM, 3: BM
        /// </param>
        /// <returns>
        /// A reference to the texture data
        /// </returns>
        public ref TexturePacker.TextureData[] GetTextureData(int layerIndex, int materialIndex, int texturesIndex)
        {
            ref MaterialTextureData materialTextureData = ref GetMaterialTextureData(layerIndex, materialIndex);
            return ref GetTextureData(ref materialTextureData, texturesIndex);
        }

        private ref TexturePacker.TextureData[] GetTextureDrawerTextureData(int arrayLayerIndex, int texturesIndex)
        {
            int layerIndex    = (int)Mathf.Floor(arrayLayerIndex / 3.0f);
            int materialIndex = arrayLayerIndex % Constants.MATERIALS_PER_LAYER_COUNT;

            return ref GetTextureData(layerIndex, materialIndex, texturesIndex);
        }

        private ref TexturePacker.TextureData[] GetBlendMaskTextureData(int layerIndex)
        {
            return ref LayersTextureData[layerIndex].BlendMaskTexture;
        }

        // Assumes Init was called and texture data is in same format

        /// <summary>
        /// Updates the packed texture data and the associated arrays
        /// </summary>
        /// <param name="layerIndex">
        /// The layer to update
        /// </param>
        /// <param name="materialIndex">
        /// The index of the material:<br />
        /// 0: Base, 1: Far, 2: Blend
        /// </param>
        /// <param name="enabled">
        /// The new state of the packed texture
        /// </param>
        public void UpdatePackedTexture(int layerIndex, int materialIndex, bool enabled)
        {
            ref MaterialTextureData textureData = ref GetMaterialTextureData(layerIndex, materialIndex);

            // Update Variables
            textureData.NSOTextures[1].Disabled = enabled;
            textureData.NSOTextures[2].Disabled = enabled;
            textureData.NSOTextures[3].Disabled = !enabled;

            textureData.EMTextures[1].Disabled = enabled;
            textureData.EMTextures[2].Disabled = !enabled;

            // Update textures
            int arrayIndex = layerIndex * Constants.MATERIALS_PER_LAYER_COUNT + materialIndex;

            if (enabled) {
                // Use packed texture
                NSOTexturesDrawer.UpdateTexture(textureData.NSOTextures[3].Texture, arrayIndex, 3, true);
                EMTexturesDrawer.UpdateTexture(textureData.EMTextures[2].Texture, arrayIndex, 2, true);
            } else {
                // Use regular textures
                NSOTexturesDrawer.UpdateTexture(textureData.NSOTextures[1].Texture, arrayIndex, 1, true);
                NSOTexturesDrawer.UpdateTexture(textureData.NSOTextures[2].Texture, arrayIndex, 2, true);
                EMTexturesDrawer.UpdateTexture(textureData.EMTextures[1].Texture, arrayIndex, 1, true);
            }

            Save();
        }

        /// <summary>
        /// Assigns the texture arrays to their material properties<br />
        /// Use in the case the properties may not be assigned<br />
        /// Make sure SetupTextureDrawers is called before this
        /// </summary>
        public void UpdateTextureProperties()
        {
            MaterialProperty avTexturesProp  = GetTexturesProp(0);
            MaterialProperty nsoTexturesProp = GetTexturesProp(1);
            MaterialProperty emTexturesProp  = GetTexturesProp(2);
            MaterialProperty bmTexturesProp  = GetTexturesProp(3);

            avTexturesProp.textureValue = AVTexturesDrawer.Array;
            nsoTexturesProp.textureValue = NSOTexturesDrawer.Array;
            emTexturesProp.textureValue = EMTexturesDrawer.Array;
            bmTexturesProp.textureValue = BMTexturesDrawer.Array;

            Debug.Log(AVTexturesDrawer.Array);
        }
    }
}
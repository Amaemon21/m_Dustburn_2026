using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Data
{
    using Utilities.Texture;
    using Materials;

    /// <summary>
    /// Stores the properties for a layered Repetitionless material
    /// </summary>
    public class RepetitionlessLayeredDataSO : ScriptableObject
    {
        /// <summary>
        /// Holds the TextureData for every channel in a control texture<br />
        /// In a class for serialization
        /// </summary>
        [System.Serializable]
        public class ControlTexture
        {
            /// <summary>
            /// A length of 4 with each index assigning to a target texture channel<br />
            /// 0 = R, 1 = G, 2 = B, 3 = A 
            /// </summary>
            public TexturePacker.TextureData[] ChannelTextures;
        }

        private static readonly Vector4 DEFAULT_LAYER_COLOURS_FIRST = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
        private static readonly Vector4 DEFAULT_LAYER_COLOURS = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

        /// <summary>
        /// The layer mode
        /// </summary>
        [SerializeField] public ELayerMode LayerMode = ELayerMode.TerrainLayers;

        /// <summary>
        /// The max amount of layers allowed to be rendered
        /// </summary>
        [SerializeField] public EMaxLayers MaxLayers = EMaxLayers.Four;
        
        /// <summary>
        /// The control textures, storing 4 channels/textures per control texture
        /// </summary>
        [SerializeField] public ControlTexture[] ControlTextures = new ControlTexture[Constants.MAX_LAYERS_TERRAIN / 4];
        
        [SerializeField] private Texture2D[] _packedControlTextures = new Texture2D[Constants.MAX_LAYERS_TERRAIN / 4];

        /// <summary>
        /// Stores references to the packed control textures
        /// </summary>
        public Texture2D[] PackedControlTextures => _packedControlTextures;

        /// <summary>
        /// The holes texture for when the layer mode is set to ControlTextures
        /// </summary>
        [SerializeField] public TexturePacker.TextureData HolesTexture = new TexturePacker.TextureData();

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
        /// Saves this object
        /// </summary>
        public void Save()
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Resets the texture data fields and packs the initial textures
        /// </summary>
        public void Init()
        {
            SetupControlTextures();
            SetupHolesTexture();

            PackControlTextures();

            Save();
        }

        public void InitNewLayerCount()
        {
            ControlTexture[] oldControlTextures = ControlTextures;

            SetupControlTextures();

            for (int i = 0; i < Mathf.Min(ControlTextures.Length, oldControlTextures.Length); i++) {
                ControlTextures[i] = oldControlTextures[i];
            }

            PackControlTextures();

            Save();
        }

        /// <summary>
        /// Resets the control textures data
        /// </summary>
        public void SetupControlTextures()
        {
            ControlTextures = new ControlTexture[Constants.MAX_LAYERS_TERRAIN / 4];
            _packedControlTextures = new Texture2D[Constants.MAX_LAYERS_TERRAIN / 4]; 

            for (int i = 0; i < ControlTextures.Length; i++) {
                SetupControlTextures(i);
            }
        }

        /// <summary>
        /// Initialises the control textures array for a specific control texture
        /// </summary>
        /// <param name="controlIndex">
        /// The index to setup
        /// </param>
        public void SetupControlTextures(int controlIndex)
        {
            ControlTextures[controlIndex] = new ControlTexture { ChannelTextures = new TexturePacker.TextureData[4] };

            for (int i = 0; i < ControlTextures[controlIndex].ChannelTextures.Length; i++) {
                SetupControlChannelTexture(controlIndex, i);
            }
        }

        /// <summary>
        /// Initialises a texture channel for a control texture
        /// </summary>
        /// <param name="controlIndex">
        /// The index to setup
        /// </param>
        /// <param name="channelIndex">
        /// The channel to setup
        /// </param>
        public void SetupControlChannelTexture(int controlIndex, int channelIndex)
        {
            TexturePacker.TextureChannel[] textureChannels = {
                TexturePacker.TextureChannel.R,
                TexturePacker.TextureChannel.G,
                TexturePacker.TextureChannel.B,
                TexturePacker.TextureChannel.A
            };

            ControlTextures[controlIndex].ChannelTextures[channelIndex] = new TexturePacker.TextureData() {
                Texture = ControlTextures[controlIndex].ChannelTextures[channelIndex].Texture,
                Disabled = false,
                DataTexture = true,
                NormalMap = false,
                FromToChannels = new List<TexturePacker.FromToChannel>() {
                    new TexturePacker.FromToChannel(
                        TexturePacker.TextureChannel.R,
                        textureChannels[channelIndex]
                    )
                }
            };
        }

        /// <summary>
        /// Initialises a control texture based on a layer index
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index to setup
        /// </param>
        public void SetupControlTexture(int layerIndex)
        {
            int controlTextureIndex = GetControlIndexFromLayerIndex(layerIndex);
            int channelIndex = layerIndex % 4;

            SetupControlChannelTexture(controlTextureIndex, channelIndex);
        }

        /// <summary>
        /// Gets a reference to the control texture data from a layer index
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index used to get the control index
        /// </param>
        /// <returns>
        /// A reference to the TextureData
        /// </returns>
        public ref TexturePacker.TextureData GetControlTextureData(int layerIndex)
        {
            int controlTextureIndex = GetControlIndexFromLayerIndex(layerIndex);
            int channelIndex = layerIndex % 4;

            return ref ControlTextures[controlTextureIndex].ChannelTextures[channelIndex];
        }

        /// <summary>
        /// Resets the holes texture data
        /// </summary>
        public void SetupHolesTexture()
        {
            HolesTexture = new TexturePacker.TextureData() {
                Texture = HolesTexture.Texture,
                Disabled = false,
                DataTexture = true,
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
        /// Packs all the control textures
        /// </summary>
        public void PackControlTextures()
        {
            for (int i = 0; i < ControlTextures.Length; i++) {
                PackControlTexture(i);
            }
        }

        /// <summary>
        /// Gets a control texture index from a layer index
        /// </summary>
        /// <param name="layerIndex">
        /// The layer index used to get the control index
        /// </param>
        /// <returns>
        /// The control index
        /// </returns>
        public int GetControlIndexFromLayerIndex(int layerIndex)
        {
            return (int)Mathf.Floor(layerIndex / 4.0f);
        }

        /// <summary>
        /// Packs a control texture based on the textures set in ControlTextures
        /// </summary>
        /// <param name="controlIndex">
        /// The control texture index to pack
        /// </param>
        public void PackControlTexture(int controlIndex)
        {
            ref TexturePacker.TextureData[] textures = ref ControlTextures[controlIndex].ChannelTextures;

            // Get the highest resolution
            Vector2Int highestResolution = new Vector2Int(1, 1);
            for (int i = 0; i < textures.Length; i++) {
                Texture2D texture = textures[i].Texture;
                if (texture == null) continue;

                if (texture.width > highestResolution.x && texture.height > highestResolution.y)
                    highestResolution = new Vector2Int(texture.width, texture.height);
            }

            // Make sure all textures are the highest resolution
            TexturePacker.TextureData[] resizedTextures = (TexturePacker.TextureData[])textures.Clone();
            for (int i = 0; i < resizedTextures.Length; i++) {
                ref TexturePacker.TextureData textureData = ref resizedTextures[i];
                Texture2D texture = textureData.Texture;
                if (texture == null) continue;

                // Resize the texture if its a different resolution
                if (texture.width != highestResolution.x || texture.height != highestResolution.y)
                    textureData.Texture = TextureUtilities.ResizeTexture(texture, highestResolution.x, highestResolution.y);
            }

            // Pack textures
            Vector4 defaultColours = controlIndex == 0 ? DEFAULT_LAYER_COLOURS_FIRST : DEFAULT_LAYER_COLOURS;
            Texture2D packedTexture = TexturePacker.PackTextures(resizedTextures, defaultColours);
            if (packedTexture == null) return;

            string fileName = Constants.CONTROL_TEXTURE_FILE_NAME_PREFIX + controlIndex + ".asset";
            _dataManager.CreateAsset(packedTexture, fileName, true);
            _packedControlTextures[controlIndex] = packedTexture;
        }

        /// <summary>
        /// Assigns all the control textures to the material
        /// </summary>
        public void AssignControlTextures()
        {
            for (int i = 0; i < ControlTextures.Length; i++) {
                AssignControlTexture(i);
            }
        }

        /// <summary>
        /// Assigns a control texture to the material
        /// </summary>
        /// <param name="index">
        /// The index to assign
        /// </param>
        public void AssignControlTexture(int index)
        {
            _dataManager.Material.SetTexture($"_Control{index}", PackedControlTextures[index]);
        }

        /// <summary>
        /// Assigns the holes texture to the material
        /// </summary>
        public void AssignHolesTexture()
        {
            _dataManager.Material.SetTexture("_TerrainHolesTexture", HolesTexture.Texture);

            // Holes require _ALPHATEST_ON to work, no need for it otherwise
            if (HolesTexture.Texture != null)
                RepetitionlessMaterialUtilities.SetBoolKeyword(_dataManager.Material, "_ALPHATEST_ON", true);
            else
                RepetitionlessMaterialUtilities.SetBoolKeyword(_dataManager.Material, "_ALPHATEST_ON", false);
        }

        /// <summary>
        /// Updates the layer count in the material based on the textures assigned
        /// </summary>
        public void UpdateLayersCount()
        {
            // Get amount of control textures assigned
            int texturesAssigned = 0;
            for (int i = 0; i < ControlTextures.Length; i++) {
                for (int j = 0; j < ControlTextures[i].ChannelTextures.Length; j++) {
                    if (ControlTextures[i].ChannelTextures[j].Texture != null)
                        texturesAssigned++;
                }
            }

            texturesAssigned = Mathf.Max(1, texturesAssigned);

            // Set layers count
            _dataManager.Material.SetFloat("_LayersCount", texturesAssigned);
        }

        /// <summary>
        /// Updates the max layers keyword based on an input layer count
        /// </summary>
        /// <param name="layerCount">
        /// The layer count to transfer to EMaxLayers
        /// </param>
        public void UpdateMaxLayers(int layerCount)
        {
            MaxLayers = (EMaxLayers)Mathf.Max(4, Mathf.Min((layerCount + 3) / 4 * 4, 32));
            Save();

            RepetitionlessMaterialUtilities.UpdateMaxLayersKeyword(_dataManager.Material, MaxLayers);
        }
    }
}
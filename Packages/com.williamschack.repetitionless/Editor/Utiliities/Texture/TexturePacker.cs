#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Repetitionless.Editor.Utilities.Texture
{
    /// <summary>
    /// Used to pack multiple textures into a single texture
    /// </summary>
    public static class TexturePacker
    {
        /// <summary>
        /// The valid texture channels
        /// </summary>
        public enum TextureChannel
        {
            /// <summary>
            /// Red channel
            /// </summary>
            R = 0,
            /// <summary>
            /// Green channel
            /// </summary>
            G = 1,
            /// <summary>
            /// Blue channel
            /// </summary>
            B = 2,
            /// <summary>
            /// Alpha channel
            /// </summary>
            A = 3
        }

        /// <summary>
        /// Contains which texture channel will be transfered to which other channel
        /// </summary>
        [System.Serializable]
        public struct FromToChannel
        {
            /// <summary>
            /// The channel that will be taken from the original texture
            /// </summary>
            public TextureChannel From;
            /// <summary>
            /// The channel which will be written to on the packed texture
            /// </summary>
            public TextureChannel To;

            /// <summary>
            /// FromToChannel Constructor
            /// </summary>
            /// <param name="from">
            /// The channel that will be taken from the original texture 
            /// </param>
            /// <param name="to">
            /// The channel which will be written to
            /// </param>
            public FromToChannel(TextureChannel from, TextureChannel to)
            {
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// Contains the data for a texture
        /// </summary>
        [System.Serializable]
        public struct TextureData
        {
            /// <summary>
            /// The texture
            /// </summary>
            public Texture2D Texture;
            /// <summary>
            /// If the texture will be ignored
            /// </summary>
            public bool Disabled;
            /// <summary>
            /// If this texture is a data texture<br />
            /// If enabled it will not be treated as srgb
            /// </summary>
            public bool DataTexture;
            /// <summary>
            /// If this texture is a normal map<br />
            /// If enabled it will not be treated as srgb and will be packed as a normal
            /// </summary>
            public bool NormalMap;
            /// <summary>
            /// Which channels will be copied to which other channels
            /// </summary>
            public List<FromToChannel> FromToChannels;
        }

        /// <summary>
        /// The texture data that will be passed to the compute shader
        /// </summary>
        private struct TextureDataGPU
        {
            /// <summary>
            /// If this texture is a normal map<br />
            /// If enabled it will not be treated as srgb and will be packed as a normal
            /// </summary>
            public int NormalMap;
            /// <summary>
            /// If this texture is srgb
            /// </summary>
            public int SRGB;

            /// <summary>
            /// How many channels are used (0 - 4)
            /// </summary>
            public int ChannelsUsedAmount;
            /// <summary>
            /// The channels that will be taken from the original
            /// </summary>
            public Vector4 FromChannels;
            /// <summary>
            /// The channels that will be written to on the packed texture
            /// </summary>
            public Vector4 ToChannels;
        }

        private const string PACK_TEXTURE_COMPUTE_RESOURCES_PATH = "repetitionless_CreatePackedTexture";

        private const int THREADS_X = 8;
        private const int THREADS_Y = 8;

        /// <summary>   
        /// Packs a set of textures
        /// </summary>
        /// <param name="textureData">
        /// The textures being packed
        /// </param>
        /// <param name="defaultColours">
        /// The default colours for each channel if they are not set
        /// </param>
        /// <returns>
        /// The packed texture
        /// </returns>
        public static Texture2D PackTextures(TextureData[] textureData, Vector4 defaultColours)
        {
            if (textureData.Length > 4) {
                Debug.LogError("Texture packing can only take 0-4 input textures");
                return null;
            }

            ComputeShader shader = Resources.Load<ComputeShader>(PACK_TEXTURE_COMPUTE_RESOURCES_PATH);
            if (shader == null) {
                Debug.LogError("Could not find texture packing compute shader...");
                return null;
            }

            // Get resolution
            Vector2Int resolution = new Vector2Int(0, 0);
            for (int i = 0; i < textureData.Length; i++) {
                Texture2D currentTexture = textureData[i].Texture;
                if (currentTexture == null || textureData[i].Disabled)
                    continue;

                int currentWidth = currentTexture.width;
                int currentHeight = currentTexture.height;

                if (resolution.x == 0 && resolution.y == 0) {
                    resolution.x = currentWidth;
                    resolution.y = currentHeight;
                    continue;
                }

                if (currentWidth != resolution.x || currentHeight != resolution.y) {
                    Debug.LogError("All textures must have the same resolution to pack...");
                    return null;
                }
            }

            // If resolution is still 0x0, set it to 4x4
            if (resolution == Vector2Int.zero)
                    resolution = new Vector2Int(4, 4);

            // Convert texture data to gpu friendly
            bool anyHasSrgb = false;

            List<Texture2D> inputTextures = new List<Texture2D>();
            List<TextureDataGPU> textureDataGPU = new List<TextureDataGPU>();
            for (int i = 0; i < textureData.Length; i++) {
                TextureData currentTextureData = textureData[i];
                if (currentTextureData.Texture == null || currentTextureData.Disabled)
                    continue;

                inputTextures.Add(currentTextureData.Texture);

                TextureDataGPU gpuData;
                gpuData.NormalMap = currentTextureData.NormalMap ? 1 : 0;
                gpuData.SRGB = currentTextureData.Texture.isDataSRGB && !currentTextureData.DataTexture ? 1 : 0;
                if (gpuData.SRGB == 1 && gpuData.NormalMap == 0) anyHasSrgb = true;

                int fromToChannelsCount = currentTextureData.FromToChannels.Count;
                if (fromToChannelsCount > 4) {
                    Debug.LogWarning("Only using the first 4 channels for " + currentTextureData.Texture.name);
                    fromToChannelsCount = 4;
                }

                gpuData.ChannelsUsedAmount = fromToChannelsCount;

                gpuData.FromChannels = new Vector4();
                gpuData.ToChannels = new Vector4();

                for (int j = 0; j < fromToChannelsCount; j++) {
                    FromToChannel currentFromToChannel = currentTextureData.FromToChannels[j];
                    gpuData.FromChannels[j] = (int)currentFromToChannel.From;
                    gpuData.ToChannels[j] = (int)currentFromToChannel.To;
                }

                textureDataGPU.Add(gpuData);
            }

            // If no textures assigned, add a dummy texture data
            if (textureDataGPU.Count == 0)
                textureDataGPU.Add(new TextureDataGPU());

            // Create render texture
            bool generateMipMaps = inputTextures.Count > 0;
            RenderTextureDescriptor desc = new RenderTextureDescriptor(resolution.x, resolution.y) {
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = generateMipMaps,
                sRGB = anyHasSrgb
            };

            RenderTexture rt = new RenderTexture(desc);
            rt.Create();

            // Assign data
            int kernel = shader.FindKernel("CSMain");

            shader.SetFloat("LinearColourSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);

            shader.SetFloat("Width", resolution.x);
            shader.SetFloat("Height", resolution.y);

            Texture2D dummyTexture = new Texture2D(4, 4);
            shader.SetTexture(kernel, "Tex0", inputTextures.Count > 0 ? inputTextures[0] : dummyTexture);
            shader.SetTexture(kernel, "Tex1", inputTextures.Count > 1 ? inputTextures[1] : dummyTexture);
            shader.SetTexture(kernel, "Tex2", inputTextures.Count > 2 ? inputTextures[2] : dummyTexture);
            shader.SetTexture(kernel, "Tex3", inputTextures.Count > 3 ? inputTextures[3] : dummyTexture);

            ComputeBuffer textureDataBuffer = new ComputeBuffer(textureDataGPU.Count, System.Runtime.InteropServices.Marshal.SizeOf<TextureDataGPU>(), ComputeBufferType.Structured);
            textureDataBuffer.SetData(textureDataGPU);

            shader.SetBuffer(kernel, "Data", textureDataBuffer);
            shader.SetFloat("TextureCount", inputTextures.Count);

            shader.SetVector("DefaultColours", defaultColours);

            shader.SetTexture(kernel, "Result", rt);

            // Pack textures
            int groupsX = Mathf.CeilToInt(resolution.x  / (float)THREADS_X);
            int groupsY = Mathf.CeilToInt(resolution.y / (float)THREADS_Y);

            shader.Dispatch(kernel, groupsX, groupsY, 1);

            textureDataBuffer.Release();

            // Copy packed texture to texture2D
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D outTex = new Texture2D(resolution.x, resolution.y, TextureFormat.RGBA32, true, !anyHasSrgb);
            outTex.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
            outTex.Apply();

            RenderTexture.active = previousRT;
            rt.Release();

            return outTex;
        }
    }
}
#endif
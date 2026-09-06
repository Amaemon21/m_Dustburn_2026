// ---------------------------------------------------
// | Script created by me taken from my other asset: |
// | Texture Array Essentials (https://u3d.as/3s4d)  |
// ---------------------------------------------------

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Repetitionless.Editor.Utilities.Texture
{
    /// <summary>
    /// Contains various helper functions for modifying textures
    /// </summary>
    public static class TextureUtilities
    {
        /// <summary>
        /// List of all the compressed texture formats
        /// </summary>
        public static readonly TextureFormat[] COMPRESSED_TEXTURE_FORMATS = {
            TextureFormat.DXT1,
            TextureFormat.DXT5,
            TextureFormat.DXT1Crunched,
            TextureFormat.DXT5Crunched,
            TextureFormat.BC4,
            TextureFormat.BC5,
            TextureFormat.BC6H,
            TextureFormat.BC7,
#if !UNITY_6000_1_OR_NEWER
            TextureFormat.PVRTC_RGB2,
            TextureFormat.PVRTC_RGB4,
            TextureFormat.PVRTC_RGBA2,
            TextureFormat.PVRTC_RGBA4,
#endif
            TextureFormat.ETC_RGB4,
            TextureFormat.EAC_R,
            TextureFormat.EAC_RG,
            TextureFormat.EAC_R_SIGNED,
            TextureFormat.EAC_RG_SIGNED,
            TextureFormat.ETC2_RGB,
            TextureFormat.ETC2_RGBA1,
            TextureFormat.ETC2_RGBA8,
            TextureFormat.ETC_RGB4Crunched,
            TextureFormat.ETC2_RGBA8Crunched,
            TextureFormat.ASTC_4x4,
            TextureFormat.ASTC_5x5,
            TextureFormat.ASTC_6x6,
            TextureFormat.ASTC_8x8,
            TextureFormat.ASTC_10x10,
            TextureFormat.ASTC_12x12,
            TextureFormat.ASTC_HDR_4x4,
            TextureFormat.ASTC_HDR_5x5,
            TextureFormat.ASTC_HDR_6x6,
            TextureFormat.ASTC_HDR_8x8,
            TextureFormat.ASTC_HDR_10x10,
            TextureFormat.ASTC_HDR_12x12,
        };

        /// <summary>
        /// Scales the input texture to the desired resolution<br />
        /// Does not modify the original texture unless set, returns a new resized one<br />
        /// If the texture is the same resolution as the target, it will do nothing and return the input texture
        /// </summary>
        /// <param name="texture">
        /// The texture that will be resized
        /// </param>
        /// <param name="newWidth">
        /// The width of the output texture
        /// </param>
        /// <param name="newHeight">
        /// The height of the output texture
        /// </param>
        /// <param name="filterMode">
        /// The filter mode used when resizing the texture
        /// </param>
        /// <param name="modifyOriginal">
        /// If the function modifies the original texture<br />
        /// When this is set, no need to use the returned texture
        /// </param>
        /// <returns>
        /// The texture resized to the input resolution
        /// </returns>
        public static Texture2D ResizeTexture(Texture2D texture, int newWidth, int newHeight, FilterMode filterMode = FilterMode.Bilinear, bool modifyOriginal = false)
        {
            // Dont resize if not required/possible 
            if (texture == null) return texture;
            if (newWidth == 0 || newHeight == 0) return texture;
            if (texture.width == newWidth && texture.height == newHeight) return texture;

            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            rt.filterMode = filterMode;

            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(texture, rt);

            Texture2D result = texture;
            if (modifyOriginal) {
                texture.Reinitialize(newWidth, newHeight);
            } else {
                result = new Texture2D(newWidth, newHeight);
            }

            result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            result.Apply();

            RenderTexture.active = previousRT;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        /// <summary>
        /// Re-Imports the input texture as readable if necessary
        /// </summary>
        /// <param name="texture">
        /// The texture that will set to readable
        /// </param>
        /// <param name="logChanges">
        /// Debugs if the texture is set to readable if it is not already
        /// </param>
        public static void SetReadable(Texture2D texture, bool logChanges = false)
        {
            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (texturePath == "") return;

            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (ti != null && !ti.isReadable) {
                if(logChanges) Debug.LogWarning($"{texture.name} is not readable, setting read to true...");
                
                ti.isReadable = true;
                ti.SaveAndReimport();
            }
        }

        /// <summary>
        /// Re-Imports the input textures as readable if necessary
        /// </summary>
        /// <param name="textures">
        /// The array of textures that will set to readable
        /// </param>
        /// <param name="logChanges">
        /// Debugs if the texture is set to readable if it is not already
        /// </param>
        public static void SetReadable(Texture2D[] textures, bool logChanges = false)
        {
            for (int i = 0; i < textures.Length; i++) {
                SetReadable(textures[i], logChanges);
            }
        }

        private static TextureImporter GetTextureImporter(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(path);

            return importer;
        }

        /// <summary>
        /// Returns if the inputted texture is imported as a normal map
        /// </summary>
        /// <param name="texture">
        /// The texture to check
        /// </param>
        /// <returns>
        /// If the texture is a normal map
        /// </returns>
        public static bool TextureIsNormal(Texture2D texture)
        {
            TextureImporter importer = GetTextureImporter(texture);
            
            if (importer == null) return false;
            return importer.textureType == TextureImporterType.NormalMap;
        }

        /// <summary>
        /// Reimports a texture as a normal map
        /// </summary>
        /// <param name="texture">
        /// The texture to update
        /// </param>
        public static void SetTextureToNormal(Texture2D texture)
        {
            TextureImporter importer = GetTextureImporter(texture);
            if (importer == null) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
    }
}
#endif
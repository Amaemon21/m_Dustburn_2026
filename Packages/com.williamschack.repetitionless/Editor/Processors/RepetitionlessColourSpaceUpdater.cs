#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Repetitionless.Editor.Processors
{
    using Data;

    /// <summary>
    /// Checks for when the colour space is changed then handles update any repetitionless materials that require repacking
    /// </summary>
    public static class RepetitionlessColourSpaceUpdater
    {
        private static ColorSpace _colourSpace = ColorSpace.Uninitialized;

        /// <summary>
        /// Called on project open in PostProjectOpen
        /// </summary>
        internal static void Initialize()
        {
            _colourSpace = PlayerSettings.colorSpace;
            EditorApplication.projectChanged += ProjectChanged;
        }

        private static void ProjectChanged()
        {
            if (PlayerSettings.colorSpace == _colourSpace)
                return;

            _colourSpace = PlayerSettings.colorSpace;
            ColourSpaceChanged();
        }

        private static void ColourSpaceChanged()
        {
            List<Material> repetitionlessMaterials = RepetitionlessMaterialFinder.GetAll();
            if (repetitionlessMaterials.Count == 0) return;

            RepackMaterialsIfColourSpaceChanged(repetitionlessMaterials);
        }

        /// <summary>
        /// Repacks the textures on a set of repetitionless materials if they require it
        /// </summary>
        /// <param name="materials">
        /// The materials that will be checked and updated
        /// </param>
        /// <param name="prompt">
        /// If a dialog will show to confirm the change
        /// </param>
        public static void RepackMaterialsIfColourSpaceChanged(List<Material> materials, bool prompt = true)
        {
            bool prompted = !prompt;

            foreach(Material mat in materials) {
                // Check each for what colour space it was packed in
                MaterialDataManager materialData = new MaterialDataManager(mat);
                RepetitionlessTextureDataSO textureData = materialData.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);
                if (textureData == null) continue;

                // Check the texture data if it was made in this colour space
                if (textureData.PackedColourSpace == _colourSpace || textureData.PackedColourSpace == ColorSpace.Uninitialized)
                    continue;

                // Check if we want to update the textures
                if (!prompted) {
                    bool updatingTextures = EditorUtility.DisplayDialog(
                        "Colour Space Mismatch",
                        $"Repetitionless materials have been found with textures that were packed in the {textureData.PackedColourSpace} colour space. These materials will look incorrect until they have been repacked. Would you like to automatically repack these textures?",
                        $"Update textures to {_colourSpace}",
                        "No"
                    );

                    // Abort
                    if (!updatingTextures)
                        return;

                    prompted = true;
                }

                // Repack AV Textures, others dont matter they are always linear
                textureData.SetupTextureDrawers();
                for (int i = 0; i < textureData.LayersTextureData.Length; i++) {
                    Texture2D albedoTexture = textureData.GetTextureData(i, 0, 0)[0].Texture;
                    if (albedoTexture == null) continue;

                    int arrayIndex = i * Constants.MATERIALS_PER_LAYER_COUNT;
                    textureData.AVTexturesDrawer.UpdateTexture(albedoTexture, arrayIndex, 0, true);
                }
            }
        }
    }
}
#endif
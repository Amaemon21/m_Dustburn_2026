#if UNITY_EDITOR
using UnityEngine;

using Repetitionless.Runtime.Variables;
using Repetitionless.Runtime.Compression;

namespace Repetitionless.Editor.Data
{
    /// <summary>
    /// Compresses repetitionless material data
    /// </summary>
    public static class RepetitionlessDataPacker
    {
        /// <summary>
        /// Converts a vector4 to a colour
        /// </summary>
        /// <param name="vector">
        /// The vector to convert
        /// </param>
        /// <returns>
        /// The colour
        /// </returns>
        public static Color Vector4ToColour(Vector4 vector)
        {
            return new Color(vector.x, vector.y, vector.z, vector.w);
        }

        /// <summary>
        /// Converts a vector3 to a colour
        /// </summary>
        /// <param name="vector">
        /// The vector to convert
        /// </param>
        /// <returns>
        /// The colour
        /// </returns>
        public static Color Vector3ToColour(Vector3 vector)
        {
            return new Color(vector.x, vector.y, vector.z);
        }

        // Returns if the value was changed
        private static bool UpdateGenericIfChanged<T>(ref T target, T value)
        {
            if (target.Equals(value))
                return false;

            target = value;
            return true;
        }

        // Returns if the value was changed
        private static bool UpdateColourIfChanged(ref Vector3 target, Color value)
        {
            if (target.x == value.r && target.y == value.g && target.y == value.b)
                return false;

            target = new Vector3(value.r, value.g, value.b);
            return true;
        }

        // Returns if the value was changed
        private static bool UpdateColourIfChanged(ref Vector4 target, Color value)
        {
            if (target.x == value.r && target.y == value.g && target.y == value.b && target.w == value.a)
                return false;

            target = new Vector4(value.r, value.g, value.b, value.a);
            return true;
        }

        // Returns if the value was changed
        private static bool UpdateBoolsIfChanged(ref float target, params bool[] values)
        {
            bool valueChanged = false;

            bool[] targetBools = BooleanCompression.GetValues((int)target, values.Length);
            for (int i = 0; i < values.Length; i++) {
                if (targetBools[i] == values[i])
                    continue;
                
                target = BooleanCompression.AddValue((int)target, i, values[i]);
                valueChanged = true;
            }

            return valueChanged;
        }

        // Returns the changed variable index in the struct
        // By default returns on the first changed value

        /// <summary>
        /// Updates changed values in the compressed material data based on the given material data
        /// </summary>
        /// <param name="compressedData">
        /// Reference to the compressed data that will be updated
        /// </param>
        /// <param name="newData">
        /// The new data to compress
        /// </param>
        /// <param name="globalTilingOffset">
        /// The global tiling offset for the material
        /// </param>
        /// <param name="updateAll">
        /// If all the values will be updated if changed<br />
        /// If disabled only the first changed value will update
        /// </param>
        /// <returns>
        /// Returns the changed variable index in the struct<br />
        /// If updateAll is enabled it will return the default -1
        /// </returns>
        public static int UpdateCompressedMaterialData(ref RepetitionlessMaterialDataCompressed compressedData, RepetitionlessMaterialData newData, Vector4 globalTilingOffset, bool updateAll = false)
        {
            if (UpdateBoolsIfChanged(ref compressedData.Settings1.x,
                newData.NoiseEnabled,
                newData.RandomiseNoiseScaling,
                newData.RandomiseNoiseRotation,
                newData.SmoothnessEnabled,
                newData.VariationEnabled,
                newData.PackedTexture,
                newData.EmissionEnabled
            ) && !updateAll) return 0;


            if (UpdateBoolsIfChanged(ref compressedData.Settings1.y,
                newData.AlbedoAssigned,
                newData.MetallicAssigned,
                newData.SmoothnessAssigned,
                newData.NormalAssigned,
                newData.OcclussionAssigned,
                newData.EmissionAssigned,
                newData.VariationAssigned,
                newData.PackedTextureAssigned
            ) && !updateAll) return 0;

            if (UpdateGenericIfChanged<float>(ref compressedData.Settings1.z, newData.Metallic) && !updateAll)            return 0;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings1.w, newData.SmoothnessRoughness) && !updateAll) return 0;

            if (UpdateGenericIfChanged<float>(ref compressedData.Settings2.x, newData.NormalScale) && !updateAll)        return 1;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings2.y, newData.OcclussionStrength) && !updateAll) return 1;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings2.z, newData.AlphaClipping) && !updateAll)      return 1;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings2.w, newData.NoiseAngleOffset) && !updateAll)   return 1;

            if (UpdateGenericIfChanged<float>(ref compressedData.Settings3.x, newData.NoiseScale) && !updateAll)          return 2;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings3.y, (int)newData.VariationMode) && !updateAll)  return 2;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings3.z, newData.VariationOpacity) && !updateAll)    return 2;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings3.w, newData.VariationBrightness) && !updateAll) return 2;
            
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings4.x, newData.VariationSmallScale) && !updateAll)    return 3;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings4.y, newData.VariationMediumScale) && !updateAll)   return 3;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings4.z, newData.VariationLargeScale) && !updateAll)    return 3;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings4.w, newData.VariationNoiseStrength) && !updateAll) return 3;

            if (UpdateGenericIfChanged<float>(ref compressedData.Settings5.x, newData.NoiseScalingMinMax.x) && !updateAll)           return 4;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings5.y, newData.NoiseScalingMinMax.y) && !updateAll)           return 4;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings5.z, newData.NoiseRandomiseRotationMinMax.x) && !updateAll) return 4;
            if (UpdateGenericIfChanged<float>(ref compressedData.Settings5.w, newData.NoiseRandomiseRotationMinMax.y) && !updateAll) return 4;

            if (UpdateColourIfChanged(ref compressedData.AlbedoTint, newData.AlbedoTint) && !updateAll)         return 5;
            if (UpdateColourIfChanged(ref compressedData.EmissionColour, newData.EmissionColour) && !updateAll) return 6;

            // Tiling is multiplied, offset is added
            Vector4 compressedTilingOffset = newData.TilingOffset;
            compressedTilingOffset.x *= globalTilingOffset.x;
            compressedTilingOffset.y *= globalTilingOffset.y;
            compressedTilingOffset.z += globalTilingOffset.z;
            compressedTilingOffset.w += globalTilingOffset.w;

            if (UpdateGenericIfChanged<Vector4>(ref compressedData.TilingOffset, compressedTilingOffset) && !updateAll) return 7;

            if (newData.VariationMode == EVariationType.CustomTexture) {
                if (UpdateGenericIfChanged<Vector4>(ref compressedData.VariationTO, newData.VariationTextureTO) && !updateAll) return 8;
            } else {
                // All of these should be updated, return if changed afterwards
                bool updatedAny = false;

                if (UpdateGenericIfChanged<float>(ref compressedData.VariationTO.x, newData.VariationNoiseScale)) updatedAny = true;
                compressedData.VariationTO.y = 0;
                if (UpdateGenericIfChanged<float>(ref compressedData.VariationTO.z, newData.VariationNoiseOffset.x)) updatedAny = true;
                if (UpdateGenericIfChanged<float>(ref compressedData.VariationTO.w, newData.VariationNoiseOffset.y)) updatedAny = true;

                if (updatedAny && !updateAll)
                    return 8;
            }

            return -1;
        }

        /// <summary>
        /// Updates changed values in the compressed layer data based on the given layer data
        /// </summary>
        /// <param name="compressedData">
        /// Reference to the compressed data that will be updated
        /// </param>
        /// <param name="newData">
        /// The new data to compress
        /// </param>
        /// <param name="updateAll">
        /// If all the values will be updated if changed<br />
        /// If disabled only the first changed value will update
        /// </param>
        /// <returns>
        /// Returns the changed variable index in the struct, incrementing for each material<br />
        /// If updateAll is enabled it will return the default -1
        /// </returns>
        public static int UpdateCompressedLayerData(ref RepetitionlessLayerDataCompressed compressedData, RepetitionlessLayerData newData, bool updateAll = false)
        {
            int baseMaterialChangedIndex = UpdateCompressedMaterialData(ref compressedData.BaseMaterialData, newData.BaseMaterialData, newData.GlobalTilingOffset, updateAll);
            if (baseMaterialChangedIndex != -1 && !updateAll) return baseMaterialChangedIndex;

            int farMaterialChangedIndex = UpdateCompressedMaterialData(ref compressedData.FarMaterialData, newData.FarMaterialData, newData.GlobalTilingOffset, updateAll);
            if (farMaterialChangedIndex != -1 && !updateAll) return farMaterialChangedIndex + Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT;

            int blendMaterialChangedIndex = UpdateCompressedMaterialData(ref compressedData.BlendMaterialData, newData.BlendMaterialData, newData.GlobalTilingOffset, updateAll);
            if (blendMaterialChangedIndex != -1 && !updateAll) return blendMaterialChangedIndex + Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT * 2;

            int startingChangedIndex = Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT * Constants.MATERIALS_PER_LAYER_COUNT;

            if (UpdateGenericIfChanged<float>(ref compressedData.DistanceBlendSettings.x, newData.DistanceBlendEnabled ? 1.0f : 0.0f) && !updateAll) return startingChangedIndex + 0;
            if (UpdateGenericIfChanged<float>(ref compressedData.DistanceBlendSettings.y, (int)newData.DistanceBlendMode) && !updateAll)             return startingChangedIndex + 0;
            if (UpdateGenericIfChanged<float>(ref compressedData.DistanceBlendSettings.z, newData.DistanceBlendMinMax.x) && !updateAll)              return startingChangedIndex + 0;
            if (UpdateGenericIfChanged<float>(ref compressedData.DistanceBlendSettings.w, newData.DistanceBlendMinMax.y) && !updateAll)              return startingChangedIndex + 0;

            if (UpdateGenericIfChanged<Vector4>(ref compressedData.BlendMaskDistanceTO, newData.BlendMaskDistanceTO) && !updateAll) return startingChangedIndex + 1;

            if (UpdateBoolsIfChanged(ref compressedData.MaterialBlendSettings.x,
                newData.MaterialBlendEnabled,
                newData.BlendMaskAssigned,
                newData.OverrideDistanceBlend,
                newData.OverrideDistanceBlendTO
            ) && !updateAll) return startingChangedIndex + 2;

            if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendSettings.y, (int)newData.BlendMaskType) && !updateAll) return startingChangedIndex + 2;
            if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendSettings.z, newData.BlendMaskOpacity) && !updateAll)   return startingChangedIndex + 2;
            if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendSettings.w, newData.BlendMaskStrength) && !updateAll)  return startingChangedIndex + 2;

            switch (newData.BlendMaskType) {
                case EMaskType.PerlinNoise:
                case EMaskType.SimplexNoise: {
                    // All of these should be updated, return if changed afterwards
                    bool updatedAny = false;

                    if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendMaskTO.x, newData.BlendMaskNoiseScale)) updatedAny = true;
                    compressedData.MaterialBlendMaskTO.y = 0;
                    if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendMaskTO.z, newData.BlendMaskNoiseOffset.x)) updatedAny = true;
                    if (UpdateGenericIfChanged<float>(ref compressedData.MaterialBlendMaskTO.w, newData.BlendMaskNoiseOffset.y)) updatedAny = true;

                    if (updatedAny && !updateAll)
                        return startingChangedIndex + 3;

                    break;
                }
                case EMaskType.CustomTexture: {
                    if (UpdateGenericIfChanged<Vector4>(ref compressedData.MaterialBlendMaskTO, newData.BlendMaskTextureTO) && !updateAll)
                        return startingChangedIndex + 3;

                    break;
                }
                case EMaskType.VertexColour: {
                    if (UpdateColourIfChanged(ref compressedData.MaterialBlendMaskTO, newData.BlendMaskVertexColour))
                        return startingChangedIndex + 3;

                    if (UpdateGenericIfChanged<Vector4>(ref compressedData.MaterialBlendMaskExtraSettings, new Vector4(newData.BlendMaskVertexColourThreshold.x, newData.BlendMaskVertexColourThreshold.y, 0, 0)))
                        return startingChangedIndex + 4;

                    break;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets a material field colour based on the given index
        /// </summary>
        /// <param name="compressedData">
        /// The compressed data to get the value from
        /// </param>
        /// <param name="compressedFieldIndex">
        /// The index of the compressed field to get:<br />
        /// 0: Settings1<br />
        /// 1: Settings2<br />
        /// 2: Settings3<br />
        /// 3: Settings4<br />
        /// 4: Settings5<br />
        /// 5: AlbedoTint<br />
        /// 6: EmissionColour<br />
        /// 7: TilingOffset<br />
        /// 8: VariationTO
        /// </param>
        /// <returns>
        /// The nullable field colour, will be null if compressedFieldIndex was outside the range of values
        /// </returns>
        public static Color? GetMaterialFieldColour(RepetitionlessMaterialDataCompressed compressedData, int compressedFieldIndex)
        {
            switch(compressedFieldIndex) {
                case 0: return Vector4ToColour(compressedData.Settings1);
                case 1: return Vector4ToColour(compressedData.Settings2);
                case 2: return Vector4ToColour(compressedData.Settings3);
                case 3: return Vector4ToColour(compressedData.Settings4);
                case 4: return Vector4ToColour(compressedData.Settings5);
                case 5: return Vector3ToColour(compressedData.AlbedoTint);
                case 6: return Vector3ToColour(compressedData.EmissionColour);
                case 7: return Vector4ToColour(compressedData.TilingOffset);
                case 8: return Vector4ToColour(compressedData.VariationTO);
            }

            return null;
        }

        /// <summary>
        /// Gets a layer field colour based on the given index
        /// </summary>
        /// <param name="compressedData">
        /// The compressed data to get the value from
        /// </param>
        /// <param name="compressedFieldIndex">
        /// The index of the compressed field to get:<br />
        /// 0-8: Base Material<br />
        /// 9-17: Far Material<br />
        /// 18-26: Blend Material<br />
        /// 27: DistanceBlendSettings<br />
        /// 28: BlendMaskDistanceTO<br />
        /// 29: MaterialBlendSettings<br />
        /// 30: MaterialBlendMaskTO
        /// </param>
        /// <returns>
        /// The nullable field colour, will be null if compressedFieldIndex was outside the range of values
        /// </returns>
        public static Color? GetLayerFieldColour(RepetitionlessLayerDataCompressed compressedData, int compressedFieldIndex)
        {
            int materialFieldIndex = compressedFieldIndex % Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT;

            if (compressedFieldIndex < Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT)
                return GetMaterialFieldColour(compressedData.BaseMaterialData, materialFieldIndex);

            if (compressedFieldIndex < Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT * 2)
                return GetMaterialFieldColour(compressedData.FarMaterialData, materialFieldIndex);

            if (compressedFieldIndex < Constants.COMPRESSED_MATERIAL_VARIABLES_COUNT * 3)
                return GetMaterialFieldColour(compressedData.BlendMaterialData, materialFieldIndex);

            switch (materialFieldIndex) {
                case 0: return Vector4ToColour(compressedData.DistanceBlendSettings);
                case 1: return Vector4ToColour(compressedData.BlendMaskDistanceTO);
                case 2: return Vector4ToColour(compressedData.MaterialBlendSettings);
                case 3: return Vector4ToColour(compressedData.MaterialBlendMaskTO);
                case 4: return Vector4ToColour(compressedData.MaterialBlendMaskExtraSettings);
            }

            return null;
        }
    }
}
#endif
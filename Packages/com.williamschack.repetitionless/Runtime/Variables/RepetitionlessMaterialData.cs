using UnityEngine;

namespace Repetitionless.Runtime.Variables
{
    /// <summary>
    /// The material data for a repetitionless material
    /// </summary>
    [System.Serializable]
    public class RepetitionlessMaterialData
    {
        // Features

        /// <summary>
        /// If noise is enabled
        /// </summary>
        public bool NoiseEnabled = true;
        /// <summary>
        /// If each cell will randomly scale
        /// </summary>
        public bool RandomiseNoiseScaling = true;
        /// <summary>
        /// If each cell will randomly rotate
        /// </summary>
        public bool RandomiseNoiseRotation = true;
        /// <summary>
        /// If smoothness or roughness is used
        /// </summary>
        public bool SmoothnessEnabled = true;
        /// <summary>
        /// If texture variation is enabled
        /// </summary>
        public bool VariationEnabled = false;
        /// <summary>
        /// If a packed texture is used
        /// </summary>
        public bool PackedTexture = false;
        /// <summary>
        /// If emission is enabled
        /// </summary>
        public bool EmissionEnabled = false;

        // Assigned Textures

        /// <summary>
        /// If the albedo texture is assigned
        /// </summary>
        public bool AlbedoAssigned = false;
        /// <summary>
        /// If the metallic texture is assigned
        /// </summary>
        public bool MetallicAssigned = false;
        /// <summary>
        /// If the smoothness texture is assigned
        /// </summary>
        public bool SmoothnessAssigned = false;
        /// <summary>
        /// If the normal texture is assigned
        /// </summary>
        public bool NormalAssigned = false;
        /// <summary>
        /// If the occlussion texture is assigned
        /// </summary>
        public bool OcclussionAssigned = false;
        /// <summary>
        /// If the emission texture is assigned
        /// </summary>
        public bool EmissionAssigned = false;
        /// <summary>
        /// If the variation texture is assigned
        /// </summary>
        public bool VariationAssigned = false;
        /// <summary>
        /// If the packed texture is assigned
        /// </summary>
        public bool PackedTextureAssigned = false;

        // Material Properties

        /// <summary>
        /// The albedo tint
        /// </summary>
        public Color AlbedoTint = Color.white;
        /// <summary>
        /// The emission colour
        /// </summary>
        public Color EmissionColour = Color.black;

        /// <summary>
        /// The tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 TilingOffset = new Vector4(1, 1, 0, 0);

        /// <summary>
        /// The metallic value
        /// </summary>
        public float Metallic = 0;
        /// <summary>
        /// The smoothness/roughness value
        /// </summary>
        public float SmoothnessRoughness = 0.5f;
        /// <summary>
        /// The normal scale
        /// </summary>
        public float NormalScale = 1.0f;
        /// <summary>
        /// The occlussion strength
        /// </summary>
        public float OcclussionStrength = 1.0f;
        /// <summary>
        /// The alpha clipping value
        /// </summary>
        public float AlphaClipping = 0.5f;

        // Noise Settings

        /// <summary>
        /// The angle offset for the noise
        /// </summary>
        public float NoiseAngleOffset = 2.0f;
        /// <summary>
        /// The scale of the noise
        /// </summary>
        public float NoiseScale = 2.0f;
        /// <summary>
        /// The random noise scaling min max
        /// </summary>
        public Vector2 NoiseScalingMinMax = new Vector2(0.8f, 1.2f);
        /// <summary>
        /// The random noise rotation min max
        /// </summary>
        public Vector2 NoiseRandomiseRotationMinMax = new Vector2(0, 360);

        // Variation Settings

        /// <summary>
        /// The variation mode used
        /// </summary>
        public EVariationType VariationMode = EVariationType.CustomTexture;
        /// <summary>
        /// The variation opacity
        /// </summary>
        public float VariationOpacity = 0.5f;
        /// <summary>
        /// The variation brightness
        /// </summary>
        public float VariationBrightness = 0.3f;
        /// <summary>
        /// The variation small sample scale
        /// </summary>
        public float VariationSmallScale = 2.0f;
        /// <summary>
        /// The variation medium sample scale
        /// </summary>
        public float VariationMediumScale = 1.0f;
        /// <summary>
        /// The variation large sample scale
        /// </summary>
        public float VariationLargeScale = 0.5f;
        /// <summary>
        /// The variation noise strength
        /// </summary>
        public float VariationNoiseStrength = 0.4f;
        /// <summary>
        /// The variation noise scale
        /// </summary>
        public float VariationNoiseScale = 100.0f;
        /// <summary>
        /// The variation noise offset
        /// </summary>
        public Vector2 VariationNoiseOffset = Vector2.zero;
        /// <summary>
        /// The variation texture tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 VariationTextureTO = new Vector4(5, 5, 0, 0);
    }

    /// <summary>
    /// The compressed material data that will be passed to the shader for a repetitionless material
    /// </summary>
    [System.Serializable]
    public struct RepetitionlessMaterialDataCompressed
    {
        /// <summary>
        /// x: Compressed Bools<br />
        ///   0 > NoiseEnabled<br />
        ///   1 > RandomiseNoiseScaling<br />
        ///   2 > RandomiseNoiseRotation<br />
        ///   3 > SmoothnessEnabled<br />
        ///   4 > VariationEnabled<br />
        ///   5 > PackedTexture<br />
        ///   6 > EmissionEnabled<br />
        /// y: Compressed Bools<br />
        ///   0 > AlbedoAssigned<br />
        ///   1 > MetallicAssigned<br />
        ///   2 > SmoothnessAssigned<br />
        ///   3 > NormalAssigned<br />
        ///   4 > OcclussionAssigned<br />
        ///   5 > EmissionAssigned<br />
        ///   6 > VarationAssigned<br />
        ///   7 > PackedTextureAssigned<br />
        /// z: Metallic<br />
        /// w: Smoothness/Roughness
        /// </summary>
        public Vector4 Settings1;

        /// <summary>
        /// x: NormalScale<br />
        /// y: OcclussionStrength<br />
        /// z: AlphaClipping<br />
        /// w: NoiseAngleOffset
        /// </summary>
        public Vector4 Settings2;

        /// <summary>
        /// x: NoiseScale<br />
        /// y: VariationMode<br />
        /// z: VariationOpacity<br />
        /// w: VariationBrightness
        /// </summary>
        public Vector4 Settings3;

        /// <summary>
        /// x: VariationSmallScale<br />
        /// y: VariationMediumScale<br />
        /// z: VariationLargeScale<br />
        /// w: VariationNoiseStrength
        /// </summary>
        public Vector4 Settings4;

        /// <summary>
        /// xy: NoiseScalingMinMax<br />
        /// zw: NoiseRandomiseRotationMinMax
        /// </summary>
        public Vector4 Settings5;

        /// <summary>
        /// The albedo tint
        /// </summary>
        public Vector3 AlbedoTint;

        /// <summary>
        /// The emission colour
        /// </summary>
        public Vector3 EmissionColour;

        /// <summary>
        /// The tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 TilingOffset;

        /// <summary>
        /// <b>IF USING NOISE:</b><br />
        /// x: VariationNoiseScale<br />
        /// zw: VariationNoiseOffset<br />
        /// <b>IF USING TEXTURE:</b><br />
        /// xyzw: VariationTextureTO
        /// </summary>
        public Vector4 VariationTO;
    }
}
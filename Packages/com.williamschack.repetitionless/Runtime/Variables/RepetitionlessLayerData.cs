using UnityEngine;

namespace Repetitionless.Runtime.Variables
{
    /// <summary>
    /// The layer data for a repetitionless material
    /// </summary>
    [System.Serializable]
    public class RepetitionlessLayerData
    {
        // Materials

        /// <summary>
        /// The base material data
        /// </summary>
        [SerializeField] public RepetitionlessMaterialData BaseMaterialData = new RepetitionlessMaterialData();
        /// <summary>
        /// The far material data
        /// </summary>
        [SerializeField] public RepetitionlessMaterialData FarMaterialData = new RepetitionlessMaterialData();
        /// <summary>
        /// The blend material data
        /// </summary>
        [SerializeField] public RepetitionlessMaterialData BlendMaterialData = new RepetitionlessMaterialData();

        // Global Settings

        /// <summary>
        /// When packing this will update all the materials tiling offset
        /// </summary>
        public Vector4 GlobalTilingOffset = new Vector4(1, 1, 0, 0);

        // Distance Blend Settings

        /// <summary>
        /// If distance blend is enabled
        /// </summary>
        public bool DistanceBlendEnabled = false;
        /// <summary>
        /// The distance blend mode used
        /// </summary>
        public EDistanceBlendMode DistanceBlendMode = EDistanceBlendMode.TilingOffset;
        /// <summary>
        /// The distance blend min max
        /// </summary>
        public Vector2 DistanceBlendMinMax = new Vector2(100, 150);

        // Blend Material Settings

        /// <summary>
        /// If material blend is enabled
        /// </summary>
        public bool MaterialBlendEnabled = false;
        /// <summary>
        /// If the blend mask texture is assigned
        /// </summary>
        public bool BlendMaskAssigned = false;
        /// <summary>
        /// If the blend material will be drawn at a distance instead of the distance material
        /// </summary>
        public bool OverrideDistanceBlend = true;
        /// <summary>
        /// If the blend material at a distance will have a different tiling offset
        /// </summary>
        public bool OverrideDistanceBlendTO = true;
        /// <summary>
        /// The blend mask type used
        /// </summary>
        public EMaskType BlendMaskType = EMaskType.PerlinNoise;
        /// <summary>
        /// The blend mask distance tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 BlendMaskDistanceTO = new Vector4(1, 1, 0, 0);
        /// <summary>
        /// The blend mask opacity
        /// </summary>
        public float BlendMaskOpacity = 1.0f;
        /// <summary>
        /// The blend mask strength
        /// </summary>
        public float BlendMaskStrength = 1.0f;
        /// <summary>
        /// The blend mask noise scale
        /// </summary>
        public float BlendMaskNoiseScale = 10.0f;
        /// <summary>
        /// The blend mask noise offset
        /// </summary>
        public Vector2 BlendMaskNoiseOffset = Vector3.zero;
        /// <summary>
        /// The blend mask texture tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 BlendMaskTextureTO = new Vector4(1, 1, 0, 0);
        /// <summary>
        /// The colour used as a mask in the vertex colour
        /// </summary>
        public Color BlendMaskVertexColour = Color.black;
        /// <summary>
        /// The smoothstep min max for the colour
        /// </summary>
        public Vector2 BlendMaskVertexColourThreshold = new Vector2(0.01f, 0.2f);
    }

    /// <summary>
    /// The compressed layer data that will be passed to the shader fro a repetitionless material
    /// </summary>
    [System.Serializable]
    public struct RepetitionlessLayerDataCompressed
    {
        /// <summary>
        /// The compressed base material data
        /// </summary>
        public RepetitionlessMaterialDataCompressed BaseMaterialData;
        /// <summary>
        /// The compressed far material data
        /// </summary>
        public RepetitionlessMaterialDataCompressed FarMaterialData;
        /// <summary>
        /// The compressed blend material data
        /// </summary>
        public RepetitionlessMaterialDataCompressed BlendMaterialData;

        /// <summary>
        /// x: DistanceBlendEnabled<br />
        /// y: DistanceBlendMode<br />
        /// zw: DistanceBlendMinMax
        /// </summary>
        public Vector4 DistanceBlendSettings;

        /// <summary>
        /// The blend mask distance tiling offset:<br />
        /// xy: Tiling, zw: Offset
        /// </summary>
        public Vector4 BlendMaskDistanceTO;

        /// <summary>
        /// x: Compressed Bools<br />
        ///   0 > MaterialBlendEnabled<br />
        ///   1 > BlendMaskAssigned<br />
        ///   2 > OverrideDistanceBlend<br />
        ///   3 > OverrideDistanceBlendTO<br />
        /// y: BlendMaskType<br />
        /// z: BlendMaskOpacity<br />
        /// w: BlendMaskStrength
        /// </summary>
        public Vector4 MaterialBlendSettings;

        /// <summary>
        /// <b>IF USING NOISE MASK:</b><br />
        /// x: BlendMaskNoiseScale<br />
        /// zw: BlendMaskNoiseOffset<br />
        /// <b>IF USING TEXTURE MASK:</b><br />
        /// xyzw: BlendMaskTextureTO<br />
        /// <b>IF USING VERTEX COLOUR MASK:</b><br />
        /// xyzw: BlendMaskVertexColour
        /// </summary>
        public Vector4 MaterialBlendMaskTO;

        /// <summary>
        /// <b>IF USING VERTEX COLOUR:</b><br />
        /// xy: BlendMaskVertexColourThreshold
        /// </summary>
        public Vector4 MaterialBlendMaskExtraSettings;
    }
}
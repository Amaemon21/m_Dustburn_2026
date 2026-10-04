namespace Repetitionless.Runtime.Variables
{
    /// <summary>
    /// Available surface types for Repetitionless Materials
    /// </summary>
    public enum ESurfaceType
    {
        /// <summary>
        /// Opaque surface with no transparency
        /// </summary>
        Opaque,

        /// <summary>
        /// TransparentCutout surface (Opaque with alpha clipping)
        /// </summary>
        Cutout,

        /// <summary>
        /// Transparent surface
        /// </summary>
        Transparent
    }
    
    /// <summary>
    /// Available UV spaces
    /// </summary>
    public enum EUVSpace
    {
        /// <summary>
        /// Local uvs from the object
        /// </summary>
        Local,

        /// <summary>
        /// World uvs from the world position
        /// </summary>
        World
    }

    /// <summary>
    /// Available noise qualities
    /// </summary>
    public enum ENoiseQuality
    {
        /// <summary>
        /// Dynamically creates the noise on the fly<br />
        /// Required to adjust the Noise Angle Offset
        /// </summary>
        High,

        /// <summary>
        /// Uses the 4k pre-rendered noise texture
        /// </summary>
        Medium,

        /// <summary>
        /// Uses the 1k pre-rendered noise texture
        /// </summary>
        Low
    }

    /// <summary>
    /// Available variation texture types for Repetitionless Materials
    /// </summary>
    public enum EVariationType
    {
        /// <summary>
        /// Procedurally generated with perlin noise
        /// </summary>
        PerlinNoise,

        /// <summary>
        /// Procedurally generated with simplex noise
        /// </summary>
        SimplexNoise,

        /// <summary>
        /// Uses a custom input texture
        /// </summary>
        CustomTexture
    }

    /// <summary>
    /// Available mask texture types for Repetitionless Materials
    /// </summary>
    public enum EMaskType
    {
        /// <summary>
        /// Procedurally generated with perlin noise
        /// </summary>
        PerlinNoise,

        /// <summary>
        /// Procedurally generated with simplex noise
        /// </summary>
        SimplexNoise,

        /// <summary>
        /// Uses a custom input texture
        /// </summary>
        CustomTexture,
        
        /// <summary>
        /// Uses a defined colour from the vertex data
        /// </summary>
        VertexColour
    }

    /// <summary>
    /// Available distance blend modes for Repetitionless Materials
    /// </summary>
    public enum EDistanceBlendMode
    {
        /// <summary>
        /// Blends into the same material with a different tiling and offset
        /// </summary>
        TilingOffset,

        /// <summary>
        /// Blends into a different material
        /// </summary>
        Material
    }

    /// <summary>
    /// Available blend modes for the vertex colour
    /// </summary>
    public enum EVertexColourBlendMode
    {
        /// <summary>
        /// Doesnt use the vertex colour
        /// </summary>
        Off,

        /// <summary>
        /// Multiplies the vertex colour with the output
        /// </summary>
        Multiply,

        /// <summary>
        /// Adds the vertex colour to the output
        /// </summary>
        Additive,

        /// <summary>
        /// Subtracts the vertex colour to the output
        /// </summary>
        Subtractive,

        /// <summary>
        /// Overwrites the output colour
        /// </summary>
        Overwrite
    }

    /// <summary>
    /// Available options for how layers are used
    /// </summary>
    public enum ELayerMode
    {
        /// <summary>
        /// Uses automatically synced terrain textures and its terrain layers to assign textures and settings to each layer
        /// </summary>
        TerrainLayers,

        /// <summary>
        /// Uses manually set textures to specify where each layer is
        /// </summary>
        ControlTextures
    }

    /// <summary>
    /// Available options for the max layers keyword in a Repetitionless material<br />
    /// Each value has an int assigned being the same as its name<br />
    /// Can be directly translated to the keyword _MAX_LAYERS_X
    /// </summary>
    public enum EMaxLayers
    {
        /// <summary>
        /// = 4
        /// </summary>
        Four = 4,

        /// <summary>
        /// = 8
        /// </summary>
        Eight = 8,

        /// <summary>
        /// = 12
        /// </summary>
        Twelve = 12,

        /// <summary>
        /// = 16
        /// </summary>
        Sixteen = 16,

        /// <summary>
        /// = 20
        /// </summary>
        Twenty = 20,

        /// <summary>
        /// = 24
        /// </summary>
        TwentyFour = 24,

        /// <summary>
        /// = 28
        /// </summary>
        TwentyEight = 28,

        /// <summary>
        /// = 32
        /// </summary>
        ThirtyTwo = 32
    }
}
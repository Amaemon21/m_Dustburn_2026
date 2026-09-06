using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "VoxelConfig", menuName = "World/Voxel Config")]
public class VoxelConfig : ScriptableObject
{
    [field: SerializeField, BoxGroup("Grid"), MinValue(0.25f)]
    [field: Tooltip("Edge of one voxel in metres. This is the digging resolution: 7 Days to Die works at one metre.")]
    public float VoxelSize { get; private set; } = 1f;

    [field: SerializeField, BoxGroup("Grid"), MinValue(8)]
    [field: Tooltip("Voxels per side of a chunk. The mesher rebuilds a whole chunk on every edit, so this is the unit of runtime cost.")]
    public int ChunkSize { get; private set; } = 32;

    [field: SerializeField, BoxGroup("Grid"), MinValue(0f)]
    [field: Tooltip("Metres of solid rock kept below the lowest point of the terrain. Digging deeper than this hits the bottom of the world.")]
    public float Bedrock { get; private set; } = 64f;

    [field: SerializeField, BoxGroup("Decor"), Range(0, 5)]
    [field: Tooltip("Coarsest ring that still gets grass. Zero keeps it in the nearest ring only, which is what detailObjectDistance did for the old terrain.")]
    public int GrassMaxLod { get; private set; }

    [field: SerializeField, BoxGroup("Decor"), MinValue(0f)]
    [field: Tooltip("Metres from the camera a patch of grass is still drawn. The ring it lives in is far wider than grass is worth, so this is the knob that decides how much of it reaches the screen. Zero draws the whole ring.")]
    public float GrassDistance { get; private set; } = 96f;

    [field: SerializeField, BoxGroup("Decor"), Range(0, 5)]
    [field: Tooltip("Coarsest ring that still gets rocks.")]
    public int RockMaxLod { get; private set; } = 1;

    [field: SerializeField, BoxGroup("Decor"), Range(0, 5)]
    [field: Tooltip("Coarsest ring that still gets trees. They read as the silhouette of the landscape, so they reach further than grass.")]
    public int TreeMaxLod { get; private set; } = 2;

    public int MaxLod(DecorKind kind)
    {
        return kind switch
        {
            DecorKind.Grass => GrassMaxLod,
            DecorKind.Rock => RockMaxLod,
            _ => TreeMaxLod
        };
    }

    [field: SerializeField, BoxGroup("Streaming"), MinValue(0f)]
    [field: Tooltip("Depth of the skirt hung from the border of a coarse chunk, in voxels of that level. It hides the crack where two levels of detail meet. Zero turns it off, the finest level never gets one because it has no coarser neighbour below it.")]
    public float SkirtDepth { get; private set; } = 2f;

    [ShowNativeProperty] public float ChunkMetres => VoxelSize * ChunkSize;

    public int ChunksPerSide(int worldSize)
    {
        return Mathf.Max(1, Mathf.CeilToInt(worldSize / ChunkMetres));
    }
}

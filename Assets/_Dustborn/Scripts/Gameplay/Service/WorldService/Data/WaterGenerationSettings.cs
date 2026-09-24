using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class WaterGenerationSettings
{
    [field: SerializeField] public bool Enabled { get; private set; } = true;

    [field: SerializeField, Range(4f, 16f)]
    [field: Tooltip("Metres per hydrology cell. Drainage is a feature of hundreds of metres, so 8 m keeps an 8 km world at a million cells.")]
    public float CellSize { get; private set; } = 8f;

    [field: SerializeField, MinValue(0.05f)]
    [field: Tooltip("Square kilometres of catchment above a point before a stream appears there.")]
    public float RiverStartArea { get; private set; } = 0.8f;

    [field: SerializeField, Range(0.25f, 3f)]
    [field: Tooltip("Multiplier on channel width. At the start area a stream is 2.5 m wide and it widens by 3.2 m per doubling of catchment.")]
    public float RiverWidthScale { get; private set; } = 1f;

    [field: SerializeField, Range(0.25f, 3f)]
    [field: Tooltip("Multiplier on water depth. At the start area a stream is 0.5 m deep and it deepens by 0.45 m per doubling of catchment.")]
    public float RiverDepthScale { get; private set; } = 1f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres over which a river bank climbs from the water's edge back to the untouched ground.")]
    public float RiverBankWidth { get; private set; } = 10f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Lateral wander added to the drainage path, as a share of what the channel corridor allows. The water level is still forced downhill afterwards.")]
    public float Meander { get; private set; } = 0.5f;

    [field: SerializeField, Range(0f, 1f), Tooltip("Chance that a closed basin big and deep enough for a lake holds one. Basins a river runs through always do.")]
    public float LakeDensity { get; private set; } = 0.4f;

    [field: SerializeField, Range(0f, 1f), Tooltip("Chance that a small closed hollow holds a pond.")]
    public float PondDensity { get; private set; } = 0.15f;

    [field: SerializeField, MinValue(0f), Tooltip("Smallest lake in square metres. Smaller basins can only be ponds.")]
    public float MinLakeArea { get; private set; } = 20000f;

    [field: SerializeField, MinValue(0f), Tooltip("Largest lake a basin with no river may hold, in square metres.")]
    public float MaxLakeArea { get; private set; } = 2500000f;

    [field: SerializeField, MinValue(0f), Tooltip("Smallest pond in square metres.")]
    public float MinPondArea { get; private set; } = 800f;

    [field: SerializeField, MinValue(0f), Tooltip("Depth in metres from spill point to basin floor a lake needs. A pond needs a third of it.")]
    public float MinLakeDepth { get; private set; } = 2f;

    [field: SerializeField, MinValue(0f), Tooltip("Metres between the centres of two standalone lakes. Ponds keep half of it.")]
    public float LakeSpacing { get; private set; } = 600f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres a river cuts its way out of a lake it runs through, capped at half the basin depth. The lake settles that much below its spill point and the river below it carves through the rim, which is what keeps deep noise basins from turning every valley floor into water.")]
    public float OutletErosion { get; private set; } = 3f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres from any water within which ground only ShoreMargin above the water counts as wet for settlements, lots, buildings and roads.")]
    public float ShoreReach { get; private set; } = 24f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres from the waterline over which a biome's ShoreLayer fades into its usual ground.")]
    public float ShoreWidth { get; private set; } = 10f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Extra cost of a road step that crosses a river, as a share of the step per 10 m of river width at a right angle. Along the flow it is three times as much, and lakes and the sea cost the full ford penalty.")]
    public float RiverCrossingPenalty { get; private set; } = 0.6f;

    [field: SerializeField]
    [field: Tooltip("Authored river, lake and pond shapes laid over the drainage. Hydrology still decides where water goes; the stamps decide what it looks like.")]
    public WaterStampSettings Stamps { get; private set; } = new();
}

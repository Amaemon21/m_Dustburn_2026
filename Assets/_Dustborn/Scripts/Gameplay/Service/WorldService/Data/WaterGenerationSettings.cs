using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class WaterGenerationSettings
{
    [field: SerializeField] public bool Enabled { get; private set; } = true;

    [field: SerializeField]
    [field: Tooltip("Pick one main river from the drainage valley network: the natural stem that runs farthest through MainRiver biomes. It keeps its real catchment and is only drawn wider, up to MainRiverWidth at its mouth. A world whose MainRiver biomes hold no stem long enough logs why and generates without one.")]
    public bool ThroughRiver { get; private set; } = true;

    [field: SerializeField, Range(20f, 48f)]
    [field: Tooltip("Width in metres the main river is drawn at its mouth. The widening grows with the square root of the share of its mouth catchment drained so far, on top of the width its own drainage gives it.")]
    public float MainRiverWidth { get; private set; } = 34f;

    [field: SerializeField, Range(0.1f, 0.9f)]
    [field: Tooltip("Shortest run through MainRiver biomes, as a share of the world side, that a drainage stem needs to become the main river.")]
    public float MainRiverMinLength { get; private set; } = 0.3f;

    [field: SerializeField, Range(0.1f, 1f)]
    [field: Tooltip("Share of its region's extent along the source-to-mouth axis the main river must cross to be accepted.")]
    public float MainRiverMinCoverage { get; private set; } = 0.25f;

    [field: SerializeField]
    [field: Tooltip("Shape the world border into a continuous sea coast: the ground falls to CoastDepth under SeaLevel at the border, the coastline wanders with noise, and the sea is drawn on past the border to OffshoreReach. Needs SeaLevel above zero.")]
    public bool Coast { get; private set; } = true;

    [field: SerializeField, MinValue(64f)]
    [field: Tooltip("Metres from the world border over which the ground climbs from the sea floor back to the untouched terrain, capped at 12% of the world side. The coastline lands inside it: nearer the border under high ground, farther in on lowland.")]
    public float CoastWidth { get; private set; } = 520f;

    [field: SerializeField, MinValue(2f)]
    [field: Tooltip("Metres under SeaLevel the sea floor lies at the world border and on past it.")]
    public float CoastDepth { get; private set; } = 24f;

    [field: SerializeField, Range(0f, 0.8f)]
    [field: Tooltip("How far the coastline wanders in and out along the border, as a share of CoastWidth. Zero is a ring parallel to the border.")]
    public float CoastVariation { get; private set; } = 0.45f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres of open sea and sea floor drawn past the world border, so the horizon is water rather than the edge of the map. Keep it past the camera far plane.")]
    public float OffshoreReach { get; private set; } = 4000f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres a pond, or a lake no river runs through, keeps between its shore and the shore of any other lake, pond or the sea. A closer one is left dry. Lakes on a river keep their place.")]
    public float MinShoreGap { get; private set; } = 120f;

    [field: SerializeField]
    [field: Tooltip("Before rivers are traced, solve drainage on a coarse grid from the real sea outlets and cut a valley wherever runoff reaches RiverStartArea: every valley floor falls monotonically to the sea, a junction or a lake, so rivers follow valleys instead of stringing noise hollows together.")]
    public bool DrainageValleys { get; private set; } = true;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of rain that runs off in biomes whose Water is None, against 1 elsewhere, when drainage valleys are carved. Hydrology itself treats dry land as a sink: a river ends where it reaches it, and no river, lake or pond lies inside it.")]
    public float DryBiomeRunoff { get; private set; } = 0.1f;

    [field: SerializeField, Range(16f, 64f)]
    [field: Tooltip("Metres per cell of the coarse drainage grid the valley network is solved on.")]
    public float ValleyCellSize { get; private set; } = 32f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Deepest cut in metres a valley floor may take through a ridge on its way down. A saddle that needs more dams the valley above it, which then holds a lake at the saddle level.")]
    public float ValleyMaxCut { get; private set; } = 14f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres a valley floor may be raised over a hollow to keep it draining. A deeper hollow on the valley line is left open and becomes a lake.")]
    public float ValleyMaxFill { get; private set; } = 4f;

    [field: SerializeField, Range(0.05f, 0.6f)]
    [field: Tooltip("Rise per metre of a valley wall where it leaves the valley floor. The wall steepens further out so a cut never ends in a step.")]
    public float ValleyWallGrade { get; private set; } = 0.2f;

    [field: SerializeField, Range(4f, 16f)]
    [field: Tooltip("Metres per hydrology cell. Drainage is a feature of hundreds of metres, so 8 m keeps an 8 km world at a million cells.")]
    public float CellSize { get; private set; } = 8f;

    [field: SerializeField, MinValue(0.05f)]
    [field: Tooltip("Square kilometres of runoff-weighted catchment above a point before a stream appears there. A source is the cell where the catchment first reaches it, so every headwater starts at this width and grows with real drainage.")]
    public float RiverStartArea { get; private set; } = 0.8f;

    [field: SerializeField, Range(0.25f, 3f)]
    [field: Tooltip("Multiplier on channel width. At the start area a stream is 2.5 m wide and it widens by 3.2 m per doubling of catchment.")]
    public float RiverWidthScale { get; private set; } = 1f;

    [field: SerializeField, Range(0.25f, 3f)]
    [field: Tooltip("Multiplier on water depth. At the start area a stream is 0.5 m deep and it deepens by 0.45 m per doubling of catchment.")]
    public float RiverDepthScale { get; private set; } = 1f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres over which a river bank climbs from the water's edge back to the untouched ground.")]
    public float RiverBankWidth { get; private set; } = 16f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Lateral wander added to the drainage path, as a share of what the channel corridor allows. The water level is still forced downhill afterwards.")]
    public float Meander { get; private set; } = 0.8f;

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

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Rivers at least this wide in metres are crossed by a bridge over an open channel; narrower ones run through a culvert pipe under the embankment.")]
    public float BridgeMinWidth { get; private set; } = 5f;

    [field: SerializeField]
    [field: Tooltip("Authored river, lake and pond shapes laid over the drainage. Hydrology still decides where water goes; the stamps decide what it looks like.")]
    public WaterStampSettings Stamps { get; private set; } = new();
}

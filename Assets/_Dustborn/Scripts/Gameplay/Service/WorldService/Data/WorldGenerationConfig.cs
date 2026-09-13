using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "WorldGenerationConfig", menuName = "World/World Generation Config")]
public class WorldGenerationConfig : ScriptableObject
{
    public const string RESOURCES_PATH = "WorldGenerationConfig";

    [field: SerializeField, BoxGroup("World")] public int Seed { get; private set; } = 1337;
    [field: SerializeField, BoxGroup("World"), MinValue(256)] public int WorldSize { get; private set; } = 8192;
    [field: SerializeField, BoxGroup("World"), MinValue(1)] public int BiomeCellSize { get; private set; } = 8;
    [field: SerializeField, BoxGroup("World"), MinValue(1)] public int HeightCellSize { get; private set; } = 1;

    [field: SerializeField, Foldout("Biome regions"), Range(1, 16), Tooltip("Voronoi seeds per biome. One gives four regions over the whole map; more breaks them into a mosaic")] public int SeedsPerBiome { get; private set; } = 1;
    [field: SerializeField, Foldout("Biome regions"), Range(0f, 1f)] public float SeedJitter { get; private set; } = 0.8f;
    [field: SerializeField, Foldout("Biome regions"), MinValue(0f)] public float WarpStrength { get; private set; } = 0.18f;
    [field: SerializeField, Foldout("Biome regions"), MinValue(0.1f)] public float WarpFrequency { get; private set; } = 2.6f;
    [field: SerializeField, Foldout("Biome regions"), Range(1, 8)] public int WarpOctaves { get; private set; } = 5;
    [field: SerializeField, Foldout("Biome regions"), Range(1f, 4f)] public float WarpLacunarity { get; private set; } = 2.3f;
    [field: SerializeField, Foldout("Biome regions"), Range(0.1f, 1f)] public float WarpPersistence { get; private set; } = 0.62f;
    [field: SerializeField, Foldout("Biome regions"), Range(0, 12)] public int SmoothingPasses { get; private set; } = 1;
    [field: SerializeField, Foldout("Biome regions"), MinValue(0)] public int MinRegionCells { get; private set; } = 250;

    [field: SerializeField, Foldout("Relief"), MinValue(1f)] public float MaxHeight { get; private set; } = 384f;
    [field: SerializeField, Foldout("Relief"), Range(0f, 2f)] public float ReliefScale { get; private set; } = 0.85f;
    [field: SerializeField, Foldout("Relief"), MinValue(0f), Tooltip("Width of the blend between biome height profiles, in metres. Independent of BiomeCellSize")] public float BiomeBlendRadius { get; private set; } = 28f;
    [field: SerializeField, Foldout("Relief"), MinValue(0f)] public float ContinentAmplitude { get; private set; } = 0.20f;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float ContinentFrequency { get; private set; } = 2f;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float HillFrequency { get; private set; } = 5.6f;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float RidgeFrequency { get; private set; } = 5f;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float DuneFrequency { get; private set; } = 16f;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float DetailFrequency { get; private set; } = 52f;
    [field: SerializeField, Foldout("Relief"), Range(1, 8)] public int ContinentOctaves { get; private set; } = 4;
    [field: SerializeField, Foldout("Relief"), Range(1, 8)] public int HillOctaves { get; private set; } = 2;
    [field: SerializeField, Foldout("Relief"), Range(1, 8)] public int RidgeOctaves { get; private set; } = 4;
    [field: SerializeField, Foldout("Relief"), Range(1, 8)] public int DuneOctaves { get; private set; } = 4;
    [field: SerializeField, Foldout("Relief"), Range(1, 8)] public int DetailOctaves { get; private set; } = 2;
    [field: SerializeField, Foldout("Relief"), MinValue(0.1f)] public float MountainMaskFrequency { get; private set; } = 3.6f;
    [field: SerializeField, Foldout("Relief"), Range(0f, 1f)] public float MountainMaskLow { get; private set; } = 0.35f;
    [field: SerializeField, Foldout("Relief"), Range(0f, 1f)] public float MountainMaskHigh { get; private set; } = 0.66f;

    [field: SerializeField, Foldout("Erosion"), Range(0, 64)]
    [field: Tooltip("Thermal erosion passes over the finished height map. Every pass moves material one cell downhill wherever the drop is steeper than the talus angle, so ridges gain scree slopes and hollows fill. Zero turns the layer off.")]
    public int ErosionPasses { get; private set; } = 64;

    [field: SerializeField, Foldout("Relief"), Range(0, 128), Tooltip("Flow accumulation iterations for hydraulic erosion. Zero disables it")] public int HydraulicPasses { get; private set; } = 48;
    [field: SerializeField, Foldout("Relief"), MinValue(1f), Tooltip("Metres per flow cell. Valleys are a large-scale feature, so this is deliberately coarser than HeightCellSize")] public float HydraulicCellSize { get; private set; } = 8f;
    [field: SerializeField, Foldout("Relief"), MinValue(0f), Tooltip("Metres cut at reference flow on a 45 degree slope")] public float HydraulicStrength { get; private set; } = 16f;
    [field: SerializeField, Foldout("Relief"), Range(0.1f, 1f)] public float HydraulicFlowExponent { get; private set; } = 0.55f;
    [field: SerializeField, Foldout("Relief"), MinValue(0f)] public float MaxHydraulicCut { get; private set; } = 34f;

    [field: SerializeField, Foldout("Water"), MinValue(0f), Tooltip("Sea level in metres. Everything below it floods. Zero disables water")] public float SeaLevel { get; private set; } = 103f;
    [field: SerializeField, Foldout("Water"), MinValue(0f), Tooltip("Metres of dry ground kept above the water before a settlement, road or building may stand there")] public float ShoreMargin { get; private set; } = 6f;

    [field: SerializeField, Foldout("Erosion"), Range(10f, 60f)]
    [field: Tooltip("Angle of repose. Slopes gentler than this are left alone, steeper ones are worn down toward it, so this is the steepest loose slope the world will hold.")]
    public float ErosionTalusAngle { get; private set; } = 30f;

    [field: SerializeField, Foldout("Erosion"), Range(0.05f, 0.5f)]
    [field: Tooltip("Share of the excess moved in one pass. Half is the stable ceiling for a four-neighbour scheme; lower values need more passes for the same result.")]
    public float ErosionStrength { get; private set; } = 0.5f;

    [field: SerializeField, Foldout("Hubs"), MinValue(2)]
    [field: Tooltip("Ceiling on the number of settlements. One candidate is taken per sector of a sqrt(HubCount) grid and MinHubDistance culls the rest, so fewer come out than this.")]
    public int HubCount { get; private set; } = 81;

    [field: SerializeField, Foldout("Hubs"), MinValue(8f)] public float HubCandidateStep { get; private set; } = 64f;
    [field: SerializeField, Foldout("Hubs"), MinValue(8f)] public float HubSampleRadius { get; private set; } = 110f;
    [field: SerializeField, Foldout("Hubs"), MinValue(0f)] public float MinHubDistance { get; private set; } = 460f;
    [field: SerializeField, Foldout("Hubs"), MinValue(0f)] public float HubEdgeMargin { get; private set; } = 400f;
    [field: SerializeField, Foldout("Hubs"), MinValue(1f)] public float MaxHubRelief { get; private set; } = 45f;
    [field: SerializeField, Foldout("Hubs"), MinValue(0f)] public float MaxHubFill { get; private set; } = 12f;
    [field: SerializeField, Foldout("Hubs"), MinValue(0f)] public float MaxHubCut { get; private set; } = 25f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1)]
    [field: Tooltip("Fewest houses a settlement is drawn with.")]
    public int MinHouses { get; private set; } = 6;

    [field: SerializeField, Foldout("Settlements"), MinValue(1)]
    [field: Tooltip("Most houses a settlement is drawn with. The flattest site gets the largest draw.")]
    public int MaxHouses { get; private set; } = 120;

    [field: SerializeField, Foldout("Settlements"), Range(0.2f, 5f)]
    [field: Tooltip("Exponent on the random house count. One draws evenly between MinHouses and MaxHouses; higher makes most settlements small and a few large.")]
    public float HouseCountBias { get; private set; } = 2.4f;

    [field: SerializeField, Foldout("Settlements"), MinValue(24f)]
    [field: Tooltip("Shortest side of a block in metres, street axis to street axis. Every row and column of the grid draws its own size between this and BlockSizeMax.")]
    public float BlockSizeMin { get; private set; } = 56f;

    [field: SerializeField, Foldout("Settlements"), MinValue(24f)]
    [field: Tooltip("Longest side of a block in metres, street axis to street axis.")]
    public float BlockSizeMax { get; private set; } = 84f;

    [field: SerializeField, Foldout("Settlements"), MinValue(0f)]
    [field: Tooltip("Metres the street grid is bent by smooth noise, so blocks are not perfect rectangles and long streets curve gently.")]
    public float BlockWarp { get; private set; } = 9f;

    [field: SerializeField, Foldout("Settlements"), MinValue(16f)]
    [field: Tooltip("Metres per period of the grid bend. Several blocks long keeps each street straight between junctions.")]
    public float BlockWarpPeriod { get; private set; } = 260f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1f)]
    [field: Tooltip("Height difference in metres across one block above which the block is not built. Refused blocks leave gaps and ragged edges.")]
    public float MaxBlockRelief { get; private set; } = 16f;

    [field: SerializeField, Foldout("Settlements"), Range(0f, 1f)]
    [field: Tooltip("Chance to keep a block side as a street once the spanning tree has connected every junction. Lower merges blocks into larger irregular ones.")]
    public float StreetLoopChance { get; private set; } = 0.8f;

    [field: SerializeField, Foldout("Settlements"), Range(0.01f, 1f)]
    [field: Tooltip("Grade above which a block side gets a street only when the settlement would otherwise fall apart.")]
    public float MaxStreetSlope { get; private set; } = 0.22f;

    [field: SerializeField, Foldout("Settlements"), Range(0f, 1f)]
    [field: Tooltip("Chance that the outer side of a street on the settlement edge is built up as well.")]
    public float OuterFrontageChance { get; private set; } = 0.45f;

    [field: SerializeField, Foldout("Settlements"), MinValue(0f)]
    [field: Tooltip("Metres a block must stay closer to its own settlement centre than to any other, so neighbours never merge and highways have room between them.")]
    public float SettlementGap { get; private set; } = 140f;

    [field: SerializeField, Foldout("Settlements"), Range(0f, 0.6f)]
    [field: Tooltip("Share of the blocks nearest the centre zoned Downtown.")]
    public float DowntownShare { get; private set; } = 0.2f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1)]
    [field: Tooltip("Blocks a settlement needs before it gets a Downtown.")]
    public int DowntownMinBlocks { get; private set; } = 5;

    [field: SerializeField, Foldout("Settlements"), Range(0f, 0.6f)]
    [field: Tooltip("Share of the blocks zoned Industrial, taken from one side of the settlement.")]
    public float IndustrialShare { get; private set; } = 0.15f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1)]
    [field: Tooltip("Blocks a settlement needs before it gets an Industrial side.")]
    public int IndustrialMinBlocks { get; private set; } = 8;

    [field: SerializeField, Foldout("Settlements"), MinValue(0f)]
    [field: Tooltip("Blur radius in metres of the ground a settlement is levelled to. Small follows the terrain, large flattens the whole settlement.")]
    public float SettlementSmoothing { get; private set; } = 36f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1f)]
    [field: Tooltip("Metres past the outer blocks over which the levelled ground blends back into the terrain.")]
    public float SettlementPadSkirt { get; private set; } = 36f;

    [field: SerializeField, Foldout("Settlements"), MinValue(1f)] public float StreetHalfWidth { get; private set; } = 3f;
    [field: SerializeField, Foldout("Settlements"), MinValue(1f)] public float StreetShoulder { get; private set; } = 6f;
    [field: SerializeField, Foldout("Settlements"), Range(0, 40)] public int StreetProfileSmoothing { get; private set; } = 6;
    [field: SerializeField, Foldout("Settlements"), MinValue(0f)] public float MaxStreetFill { get; private set; } = 2.5f;
    [field: SerializeField, Foldout("Settlements"), MinValue(0f)] public float MaxStreetCut { get; private set; } = 8f;

    [field: SerializeField, Foldout("Roads"), MinValue(4f)] public float RoadCellSize { get; private set; } = 32f;
    [field: SerializeField, Foldout("Roads"), MinValue(0f)] public float RoadSlopePenalty { get; private set; } = 20f;
    [field: SerializeField, Foldout("Roads"), MinValue(0f)] public float RoadCrossSlopePenalty { get; private set; } = 16f;
    [field: SerializeField, Foldout("Roads"), Range(0.05f, 1f)] public float RoadReuseDiscount { get; private set; } = 0.35f;

    [field: SerializeField, Foldout("Roads"), Range(0, 128)]
    [field: Tooltip("Shortest links added on top of the minimum spanning tree between settlements, so the highway network has loops.")]
    public int RoadExtraEdges { get; private set; } = 60;

    [field: SerializeField, Foldout("Roads"), MinValue(1f)]
    [field: Tooltip("A* cost multiplier for a cell inside a settlement. Highways stop at the settlement edge and go round other settlements instead of through them.")]
    public float SettlementRoadPenalty { get; private set; } = 10f;

    [field: SerializeField, Foldout("Roads"), MinValue(1f)] public float RoadHalfWidth { get; private set; } = 4f;
    [field: SerializeField, Foldout("Roads"), MinValue(1f)] public float RoadShoulder { get; private set; } = 9f;
    [field: SerializeField, Foldout("Roads"), Range(0, 40)] public int RoadProfileSmoothing { get; private set; } = 24;
    [field: SerializeField, Foldout("Roads"), MinValue(0f)] public float MaxRoadFill { get; private set; } = 5f;
    [field: SerializeField, Foldout("Roads"), MinValue(0f)] public float MaxRoadCut { get; private set; } = 14f;
    [field: SerializeField, Foldout("Roads"), MinValue(0f)] public float RoadEmbankmentSlope { get; private set; } = 5f;

    [field: SerializeField, Foldout("Road shape"), MinValue(0f)]
    [field: Tooltip("Metres of cost per radian of turn in the A* search. Zero brings back the old zig-zag roads, 40 to 80 hold long straights and sweeping curves.")]
    public float RoadTurnPenalty { get; private set; } = 55f;

    [field: SerializeField, Foldout("Road shape"), MinValue(0f)]
    [field: Tooltip("Douglas-Peucker tolerance in metres. Drops the raster staircase left by the grid search while keeping the real turns.")]
    public float RoadSimplifyTolerance { get; private set; } = 14f;

    [field: SerializeField, Foldout("Road shape"), Range(0, 6)]
    [field: Tooltip("Chaikin corner-cutting passes run after the simplification.")]
    public int RoadSmoothPasses { get; private set; } = 3;

    [field: SerializeField, Foldout("Road shape"), Range(0, 64)]
    [field: Tooltip("Curvature relaxation passes. Each pass pulls every point toward the midpoint of its neighbours.")]
    public int RoadRelaxPasses { get; private set; } = 24;

    [field: SerializeField, Foldout("Road shape"), Range(0f, 1f)]
    [field: Tooltip("How far a point travels toward that midpoint in one pass.")]
    public float RoadRelaxStrength { get; private set; } = 0.4f;

    [field: SerializeField, Foldout("Road shape"), MinValue(0f)]
    [field: Tooltip("How far in metres relaxation may drift from the path the search found. Without this cap the road would straighten into a line and walk off the pass it was routed through.")]
    public float RoadCorridor { get; private set; } = 36f;

    [field: SerializeField, Foldout("Road shape"), Range(0.01f, 0.5f)]
    [field: Tooltip("Grade a relaxation step must not exceed. It only blocks steps that make the grade both too steep and worse than it was.")]
    public float RoadMaxGrade { get; private set; } = 0.1f;

    [field: SerializeField, Foldout("Road shape"), MinValue(2f)]
    [field: Tooltip("Spacing in metres the finished polyline is resampled to.")]
    public float RoadPointSpacing { get; private set; } = 12f;

    [field: SerializeField, Foldout("POI"), MinValue(0f)]
    [field: Tooltip("Constant gap in metres kept on each side of a building inside its lot.")]
    public float LotMargin { get; private set; } = 2.5f;

    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float LotSetback { get; private set; } = 3f;
    [field: SerializeField, Foldout("POI"), MinValue(0.5f)] public float LotFrontGap { get; private set; } = 2.5f;
    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float LotGap { get; private set; } = 6f;
    [field: SerializeField, Foldout("POI"), MinValue(2f)] public float LotProbeStep { get; private set; } = 8f;
    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float MaxPoiFill { get; private set; } = 6f;
    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float MaxPoiCut { get; private set; } = 10f;
    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float PoiPadMargin { get; private set; } = 2f;
    [field: SerializeField, Foldout("POI"), MinValue(0.5f)] public float PoiPadSkirt { get; private set; } = 5f;
    [field: SerializeField, Foldout("POI"), MinValue(20f)] public float RuralSpacing { get; private set; } = 180f;
    [field: SerializeField, Foldout("POI"), MinValue(0f)] public float RuralOffset { get; private set; } = 26f;
    [field: SerializeField, Foldout("POI"), Range(0f, 1f)] public float RuralChance { get; private set; } = 0.6f;
    [field: SerializeField, Foldout("POI"), MinValue(1f)] public float RuralClearance { get; private set; } = 1.25f;

    [field: SerializeField, Foldout("Terrain layers"), Range(2, 32)]
    [field: Tooltip("Free Repetitionless holds 4 ground layers, Pro holds 32. Layers past the budget are not registered: cliff and road take their slots first, then biome grounds in order of world coverage.")]
    public int MaxTerrainLayers { get; private set; } = 32;

    [field: SerializeField, Foldout("Terrain layers")]
    [field: Tooltip("Paint each biome with the first ground of its list and nothing else, ignoring the slope, height and patch rules. The shared cliff and road layers still apply, since they are not a biome's own ground. A flat look for working on something else, and it costs one layer slot per biome instead of five or six.")]
    public bool OneGroundPerBiome { get; private set; }

    [field: SerializeField, Foldout("Terrain layers")] public TerrainLayer CliffLayer { get; private set; }
    [field: SerializeField, Foldout("Terrain layers")] public TerrainLayer RoadLayer { get; private set; }

    [field: SerializeField, Foldout("Terrain layers")]
    [field: Tooltip("Ground of the verge beside the roadway, RoadVergeWidth metres wide. Leave it empty and the verge is painted with RoadLayer too.")]
    public TerrainLayer RoadEdgeLayer { get; private set; }

    [field: SerializeField, Foldout("Terrain layers"), MinValue(0f)]
    [field: Tooltip("Metres of verge painted past the edge of the carriageway.")]
    public float RoadVergeWidth { get; private set; } = 2.5f;

    [field: SerializeField, Foldout("Terrain layers"), MinValue(0.5f)]
    [field: Tooltip("Metres over which the carriageway fades into the verge and the verge into the ground. Never taken below two control texels, where the edge would stair-step on the texel grid.")]
    public float RoadEdgeSoftness { get; private set; } = 4f;

    [field: SerializeField, Foldout("Terrain layers"), Range(0f, 90f)] public float CliffSlopeStart { get; private set; } = 28f;
    [field: SerializeField, Foldout("Terrain layers"), Range(0f, 90f)] public float CliffSlopeFull { get; private set; } = 48f;

    [field: SerializeField, Foldout("Terrain layers"), Range(1f, 64f)]
    [field: Tooltip("Metres over which one biome's ground, grass, trees and rocks give way to the next. Height profiles keep blending over BiomeBlendRadius; only the surface switches this fast.")]
    public float BiomeBorderWidth { get; private set; } = 6f;

    [field: SerializeField, Foldout("Terrain layers"), MinValue(0f)]
    [field: Tooltip("Metres the painted biome border is pushed back and forth by noise, so it winds instead of tracing the blurred edge of the biome map. Capped at BiomeBlendRadius, past which the blend weights carry nothing to warp.")]
    public float BiomeBorderWarp { get; private set; } = 24f;

    [field: SerializeField, Foldout("Terrain layers"), MinValue(4f)]
    [field: Tooltip("Metres per period of the border warp noise. Smaller gives a ragged edge, larger long sweeping bays.")]
    public float BiomeBorderWarpPeriod { get; private set; } = 120f;

    [field: SerializeField, Foldout("Output")] public string BiomeMapAssetPath { get; private set; } = "Assets/_Dustborn/Generated/BiomeMap.png";
    [field: SerializeField, Foldout("Output")] public string HeightMapAssetPath { get; private set; } = "Assets/_Dustborn/Generated/HeightMap.bytes";
    [field: SerializeField, Foldout("Output")] public string HeightMapPreviewPath { get; private set; } = "Assets/_Dustborn/Generated/HeightMapPreview.png";
    [field: SerializeField, Foldout("Output")] public string RoadMaskAssetPath { get; private set; } = "Assets/_Dustborn/Generated/RoadMask.png";
    [field: SerializeField, Foldout("Output")] public string RoadNetworkAssetPath { get; private set; } = "Assets/_Dustborn/Generated/RoadNetwork.asset";
    [field: SerializeField, Foldout("Output")] public string PoiPlacementAssetPath { get; private set; } = "Assets/_Dustborn/Generated/PoiPlacement.asset";

    [ShowNativeProperty] public string SettlementMix => $"up to {HubCount} settlements of {MinHouses} to {MaxHouses} houses, {Mathf.RoundToInt(MeanHouses)} on average";

    [ShowNativeProperty] public float LargestSettlementRadius => BlockLattice.EstimateRadius(this, Mathf.Max(MinHouses, MaxHouses));

    [ShowNativeProperty] public int BiomeMapResolution => Mathf.Max(1, WorldSize / BiomeCellSize);

    [ShowNativeProperty] public int HeightMapResolution => Mathf.Max(2, WorldSize / HeightCellSize) + 1;

    private float MeanHouses => Mathf.Lerp(MinHouses, MaxHouses, 1f / (1f + Mathf.Max(0.01f, HouseCountBias)));

    [Button("Reroll Seed")]
    private void RerollSeed()
    {
        Seed = Random.Range(int.MinValue, int.MaxValue);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

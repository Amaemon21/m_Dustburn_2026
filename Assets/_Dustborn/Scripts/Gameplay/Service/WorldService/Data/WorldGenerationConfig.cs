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

    [field: SerializeField, Foldout("Settlement mix")]
    [field: Tooltip("Large settlements with a Downtown core, Commercial ring, Industrial side and Rural outskirts. Placed first, on the largest flat sites.")]
    public SettlementTypeProfile CityProfile { get; private set; } =
        new(3, 18, 30, 2200f, 5, 6, 4f, 3, 0.8f, SettlementShape.Compact, 0.15f, 0.2f, 0.15f, 0.1f, 1f);

    [field: SerializeField, Foldout("Settlement mix")]
    [field: Tooltip("Mixed grids around a Commercial main street with little or no Downtown.")]
    public SettlementTypeProfile TownProfile { get; private set; } =
        new(7, 8, 14, 1300f, 4, 4, 2.5f, 2, 0.55f, SettlementShape.Compact, 0f, 0.25f, 0.1f, 0.15f, 0.85f);

    [field: SerializeField, Foldout("Settlement mix")]
    [field: Tooltip("One or two crossing streets with a store at the crossing.")]
    public SettlementTypeProfile CountryTownProfile { get; private set; } =
        new(12, 3, 6, 800f, 3, 3, 1.5f, 1, 0.1f, SettlementShape.Cross, 0f, 0.2f, 0f, 0.3f, 0.6f);

    [field: SerializeField, Foldout("Settlement mix")]
    [field: Tooltip("A few sparse buildings along a single main street.")]
    public SettlementTypeProfile GhostTownProfile { get; private set; } =
        new(16, 1, 3, 600f, 2, 2, 1f, 1, 0f, SettlementShape.Linear, 0f, 0f, 0f, 0.5f, 0.35f);

    [field: SerializeField, Foldout("Settlement mix"), MinValue(0f)]
    [field: Tooltip("Metres kept between the estimated footprints of two settlements, so neighbours never merge and highways have room between them.")]
    public float SettlementGap { get; private set; } = 140f;

    [field: SerializeField, Foldout("Settlement mix"), MinValue(0f)]
    [field: Tooltip("Metres kept between the world border and the estimated footprint of a settlement.")]
    public float HubEdgeMargin { get; private set; } = 160f;

    [field: SerializeField, Foldout("Settlement mix"), MinValue(8f)]
    [field: Tooltip("Radius in metres of the relief probe around a candidate settlement centre.")]
    public float HubSampleRadius { get; private set; } = 110f;

    [field: SerializeField, Foldout("Settlement mix"), MinValue(1f)]
    [field: Tooltip("Height difference in metres inside HubSampleRadius above which a site is not a settlement centre.")]
    public float MaxHubRelief { get; private set; } = 45f;

    [field: SerializeField, Foldout("Settlement mix"), Range(0f, 1f)]
    [field: Tooltip("Share of buildable tile-sized ground a site needs inside its estimated radius. Cities take the sites where this share is largest.")]
    public float SiteBuildableShare { get; private set; } = 0.35f;

    [field: SerializeField, Foldout("Tile topology"), MinValue(40f)]
    [field: Tooltip("Side of one settlement tile in metres. Streets enter a tile at the middle of its sides and meet in its centre, so this is also the street spacing.")]
    public float TileSize { get; private set; } = 150f;

    [field: SerializeField, Foldout("Tile topology"), MinValue(1f)]
    [field: Tooltip("Height difference in metres across one tile above which the tile is not built.")]
    public float MaxTileRelief { get; private set; } = 26f;

    [field: SerializeField, Foldout("Tile topology"), Range(0.005f, 0.3f)]
    [field: Tooltip("Steepest grade of the prepared ground between two neighbouring tile corners. Terrain is levelled per tile to a surface that respects this, not flattened to one height.")]
    public float MaxTileGrade { get; private set; } = 0.06f;

    [field: SerializeField, Foldout("Tile topology"), Range(0f, 1f)]
    [field: Tooltip("Largest share of dead-end tiles a City or Town keeps. The solver connects the rest to a neighbour.")]
    public float DeadEndShare { get; private set; } = 0.25f;

    [field: SerializeField, Foldout("Tile topology"), Range(1, 12)]
    [field: Tooltip("Deterministic restarts of the tile port solver when a shape rule is violated. The attempt with the fewest violations wins.")]
    public int TopologyAttempts { get; private set; } = 4;

    [field: SerializeField, Foldout("Tile topology"), Range(0f, 90f)]
    [field: Tooltip("Degrees within which two regional links leaving a settlement share one gateway and split outside town.")]
    public float GatewayMergeAngle { get; private set; } = 40f;

    [field: SerializeField, Foldout("Regional graph"), Range(1f, 3f)]
    [field: Tooltip("Planned regional links per settlement. The spanning backbone gives just under one, loops raise it toward this ceiling while they still shorten real detours.")]
    public float TargetLinkRatio { get; private set; } = 1.3f;

    [field: SerializeField, Foldout("Regional graph"), MinValue(1f)]
    [field: Tooltip("A loop is only planned where the network path between its ends is at least this many times longer than the direct link.")]
    public float MinLoopDetour { get; private set; } = 1.4f;

    [field: SerializeField, Foldout("Regional graph"), Range(0f, 0.9f)]
    [field: Tooltip("Share of the old network distance a routed loop must save, or it is dropped after routing.")]
    public float MinLoopGain { get; private set; } = 0.2f;

    [field: SerializeField, Foldout("Regional graph"), MinValue(0f)]
    [field: Tooltip("Metres between the midpoints of two loops, so redundancy is spread over the map instead of piling up around one cluster.")]
    public float LoopSpacing { get; private set; } = 1500f;

    [field: SerializeField, Foldout("Regional graph"), Range(0f, 90f)]
    [field: Tooltip("Smallest angle in degrees between two links leaving the same settlement. Shallower links would run as twin corridors.")]
    public float MinLinkAngle { get; private set; } = 38f;

    [field: SerializeField, Foldout("Regional graph"), MinValue(100f)]
    [field: Tooltip("Longest loop link in metres. The backbone ignores it when nothing shorter connects a settlement.")]
    public float MaxLinkLength { get; private set; } = 3200f;

    [field: SerializeField, Foldout("Regional graph"), MinValue(0f)]
    [field: Tooltip("Cost multiplier per share of a candidate link that runs under water.")]
    public float LinkWaterPenalty { get; private set; } = 6f;

    [field: SerializeField, Foldout("Regional graph"), MinValue(0f)]
    [field: Tooltip("Cost multiplier per metre of mean cut or fill a straight road along the candidate link would need.")]
    public float LinkEarthworkWeight { get; private set; } = 0.08f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(4f)] public float RoadCellSize { get; private set; } = 32f;
    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)] public float RoadSlopePenalty { get; private set; } = 20f;
    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)] public float RoadCrossSlopePenalty { get; private set; } = 16f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Metres of cost per radian of turn in the A* search. Zero brings back the old zig-zag roads, 40 to 80 hold long straights and sweeping curves.")]
    public float RoadTurnPenalty { get; private set; } = 55f;

    [field: SerializeField, Foldout("Highway routing"), Range(0.01f, 0.5f)]
    [field: Tooltip("Steepest highway grade. A search step steeper than this is refused unless no route exists, relaxation never steepens past it, and the carved road is validated against it.")]
    public float RoadMaxGrade { get; private set; } = 0.12f;

    [field: SerializeField, Foldout("Highway routing"), Range(0.05f, 2f)]
    [field: Tooltip("Steepest ground across a highway. A search step over a steeper hillside is refused, since the carve would leave a shelf.")]
    public float HighwayMaxCrossSlope { get; private set; } = 0.55f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Extra cost for ground above its surroundings, so highways follow valleys and cross ridges at passes.")]
    public float ValleyPreference { get; private set; } = 0.5f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(16f)]
    [field: Tooltip("Metres over which the surroundings of a cell are averaged for ValleyPreference.")]
    public float ValleyRadius { get; private set; } = 240f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Metres of cost for ending a new highway on an existing one. Higher keeps routes direct, lower merges them into shared trunks sooner.")]
    public float JunctionPenalty { get; private set; } = 160f;

    [field: SerializeField, Foldout("Highway routing"), Range(10f, 90f)]
    [field: Tooltip("Smallest angle in degrees at which a road may meet or cross another one. Shallower meetings are refused, because they read as two roads lying on top of each other.")]
    public float JunctionAngle { get; private set; } = 40f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(1f)]
    [field: Tooltip("Cost multiplier for running along an existing road, or beside it within two search cells.")]
    public float ParallelPenalty { get; private set; } = 12f;

    [field: SerializeField, Foldout("Highway routing"), Range(0.05f, 1f)]
    [field: Tooltip("Cost multiplier for riding along an existing highway. A link that shares a corridor follows the existing road and leaves it at explicit junctions instead of running beside it.")]
    public float RoadReuseDiscount { get; private set; } = 0.35f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(20f)]
    [field: Tooltip("Metres a highway runs straight out of a gateway along the arterial it continues, before it may turn.")]
    public float GatewayApproachLength { get; private set; } = 120f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Metres a highway keeps from any settlement tile other than its own gateway.")]
    public float HighwaySettlementClearance { get; private set; } = 28f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Tightest highway curve radius in metres after smoothing.")]
    public float HighwayMinCurveRadius { get; private set; } = 70f;

    [field: SerializeField, Foldout("Highway routing"), Range(1, 6)]
    [field: Tooltip("Routes tried for one link. A route that crosses a road at a shallow angle, touches a settlement or runs as a twin is rerouted around the offending cells.")]
    public int RouteAttempts { get; private set; } = 3;

    [field: SerializeField, Foldout("Highway routing"), Range(0f, 1f)]
    [field: Tooltip("Largest share of a loop that may run under water. The backbone keeps its route regardless, since connectivity comes first.")]
    public float MaxWaterExposure { get; private set; } = 0.1f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(1f)] public float RoadHalfWidth { get; private set; } = 4f;
    [field: SerializeField, Foldout("Highway routing"), MinValue(1f)] public float RoadShoulder { get; private set; } = 9f;
    [field: SerializeField, Foldout("Highway routing"), Range(0, 40)] public int RoadProfileSmoothing { get; private set; } = 24;
    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)] public float MaxRoadFill { get; private set; } = 5f;
    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)] public float MaxRoadCut { get; private set; } = 14f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("Douglas-Peucker tolerance in metres. Drops the raster staircase left by the grid search while keeping the real turns.")]
    public float RoadSimplifyTolerance { get; private set; } = 14f;

    [field: SerializeField, Foldout("Highway routing"), Range(0, 6)]
    [field: Tooltip("Chaikin corner-cutting passes run after the simplification.")]
    public int RoadSmoothPasses { get; private set; } = 3;

    [field: SerializeField, Foldout("Highway routing"), Range(0, 64)]
    [field: Tooltip("Curvature relaxation passes. Each pass pulls every point toward the midpoint of its neighbours.")]
    public int RoadRelaxPasses { get; private set; } = 24;

    [field: SerializeField, Foldout("Highway routing"), Range(0f, 1f)]
    [field: Tooltip("How far a point travels toward that midpoint in one pass.")]
    public float RoadRelaxStrength { get; private set; } = 0.4f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(0f)]
    [field: Tooltip("How far in metres relaxation may drift from the path the search found. Without this cap the road would straighten into a line and walk off the pass it was routed through.")]
    public float RoadCorridor { get; private set; } = 36f;

    [field: SerializeField, Foldout("Highway routing"), MinValue(2f)]
    [field: Tooltip("Spacing in metres the finished polyline is resampled to.")]
    public float RoadPointSpacing { get; private set; } = 12f;

    [field: SerializeField, Foldout("Local streets"), MinValue(1f)]
    [field: Tooltip("Half width in metres of the arterial that carries a highway from a gateway into the settlement core.")]
    public float ArterialHalfWidth { get; private set; } = 4f;

    [field: SerializeField, Foldout("Local streets"), MinValue(1f)] public float ArterialShoulder { get; private set; } = 7f;
    [field: SerializeField, Foldout("Local streets"), MinValue(1f)] public float StreetHalfWidth { get; private set; } = 3f;
    [field: SerializeField, Foldout("Local streets"), MinValue(1f)] public float StreetShoulder { get; private set; } = 6f;
    [field: SerializeField, Foldout("Local streets"), Range(0, 40)] public int StreetProfileSmoothing { get; private set; } = 6;
    [field: SerializeField, Foldout("Local streets"), MinValue(0f)] public float MaxStreetFill { get; private set; } = 2.5f;
    [field: SerializeField, Foldout("Local streets"), MinValue(0f)] public float MaxStreetCut { get; private set; } = 8f;

    [field: SerializeField, Foldout("Local streets"), Range(0.01f, 1f)]
    [field: Tooltip("Steepest grade of an arterial or local street, validated on the carved terrain.")]
    public float MaxStreetSlope { get; private set; } = 0.22f;

    [field: SerializeField, Foldout("Local streets"), MinValue(0f)]
    [field: Tooltip("Radius in metres of the curve a street takes through a corner tile. Also the smallest street curve radius.")]
    public float StreetCornerRadius { get; private set; } = 16f;

    [field: SerializeField, Foldout("Local streets"), MinValue(0f)]
    [field: Tooltip("Metres a court or cul-de-sac from a tile template runs into its quadrant. Zero leaves tiles with their port streets only.")]
    public float CourtDepth { get; private set; } = 44f;

    [field: SerializeField, Foldout("Dirt access roads"), MinValue(1f)] public float DirtHalfWidth { get; private set; } = 2.5f;
    [field: SerializeField, Foldout("Dirt access roads"), MinValue(1f)] public float DirtShoulder { get; private set; } = 4f;
    [field: SerializeField, Foldout("Dirt access roads"), Range(0, 40)] public int DirtProfileSmoothing { get; private set; } = 8;
    [field: SerializeField, Foldout("Dirt access roads"), MinValue(0f)] public float MaxDirtFill { get; private set; } = 2f;
    [field: SerializeField, Foldout("Dirt access roads"), MinValue(0f)] public float MaxDirtCut { get; private set; } = 5f;

    [field: SerializeField, Foldout("Dirt access roads"), Range(0.01f, 1f)]
    [field: Tooltip("Steepest grade of a dirt access road.")]
    public float DirtMaxGrade { get; private set; } = 0.2f;

    [field: SerializeField, Foldout("Dirt access roads"), Range(0.05f, 2f)]
    [field: Tooltip("Steepest ground across a dirt access road.")]
    public float DirtMaxCrossSlope { get; private set; } = 0.6f;

    [field: SerializeField, Foldout("Dirt access roads"), MinValue(0f)]
    [field: Tooltip("Tightest curve radius of a dirt access road in metres.")]
    public float DirtMinCurveRadius { get; private set; } = 20f;

    [field: SerializeField, Foldout("Dirt access roads"), MinValue(20f)]
    [field: Tooltip("Metres of highway between two candidate spur junctions.")]
    public float DirtSpacing { get; private set; } = 240f;

    [field: SerializeField, Foldout("Dirt access roads"), Range(0f, 1f)]
    [field: Tooltip("Chance that a candidate junction grows a spur to a rural building.")]
    public float DirtChance { get; private set; } = 0.6f;

    [field: SerializeField, Foldout("Dirt access roads"), MinValue(10f)] public float DirtMinLength { get; private set; } = 40f;
    [field: SerializeField, Foldout("Dirt access roads"), MinValue(10f)] public float DirtMaxLength { get; private set; } = 150f;

    [field: SerializeField, Foldout("Dirt access roads"), MinValue(0f)]
    [field: Tooltip("Metres between a spur and any settlement tile, so rural buildings stay out of town.")]
    public float DirtSettlementClearance { get; private set; } = 160f;

    [field: SerializeField, Foldout("Dirt access roads"), Range(0f, 1f)]
    [field: Tooltip("Chance that a spur ends at a farmstead of two or three buildings instead of one.")]
    public float FarmsteadChance { get; private set; } = 0.35f;

    [field: SerializeField, Foldout("Terrain integration"), MinValue(0f)] public float MaxHubFill { get; private set; } = 12f;
    [field: SerializeField, Foldout("Terrain integration"), MinValue(0f)] public float MaxHubCut { get; private set; } = 25f;

    [field: SerializeField, Foldout("Terrain integration"), MinValue(0f)]
    [field: Tooltip("Blur radius in metres of the ground the tile corners are levelled to. Small follows the terrain, large flattens each neighbourhood more.")]
    public float SettlementSmoothing { get; private set; } = 36f;

    [field: SerializeField, Foldout("Terrain integration"), MinValue(1f)]
    [field: Tooltip("Metres past the outer tiles over which the prepared ground blends back into the terrain.")]
    public float SettlementPadSkirt { get; private set; } = 36f;

    [field: SerializeField, Foldout("Terrain integration"), MinValue(0f)] public float RoadEmbankmentSlope { get; private set; } = 5f;

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

    [ShowNativeProperty] public string SettlementMix => $"{Count(CityProfile)} cities, {Count(TownProfile)} towns, {Count(CountryTownProfile)} country towns, {Count(GhostTownProfile)} ghost towns";

    [ShowNativeProperty] public float LargestSettlementRadius => CityProfile == null ? 0f : CityProfile.EstimateRadius(TileSize);

    [ShowNativeProperty] public int BiomeMapResolution => Mathf.Max(1, WorldSize / BiomeCellSize);

    [ShowNativeProperty] public int HeightMapResolution => Mathf.Max(2, WorldSize / HeightCellSize) + 1;

    public SettlementTypeProfile Profile(SettlementType type)
    {
        return type switch
        {
            SettlementType.City => CityProfile,
            SettlementType.Town => TownProfile,
            SettlementType.CountryTown => CountryTownProfile,
            _ => GhostTownProfile
        };
    }

    private static int Count(SettlementTypeProfile profile)
    {
        return profile == null ? 0 : profile.Count;
    }

    [Button("Reroll Seed")]
    private void RerollSeed()
    {
        Seed = Random.Range(int.MinValue, int.MaxValue);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

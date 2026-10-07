using System.Collections.Generic;
using UnityEngine;

public sealed class HydrologyGrid
{
    public int Resolution;
    public float CellSize;
    public float[] Height;
    public float[] Filled;
    public float[] Routing;
    public int[] Receiver;
    public float[] Accumulation;
    public float[] Runoff;
    public int[] Order;
    public int[] Basin;
    public bool[] Outlet;
    public bool[] Sink;
    public bool[] Ocean;

    public float Area(int cell)
    {
        return Accumulation[cell] * CellSize * CellSize;
    }
}

public sealed class HydrologyBasin
{
    public int Id;
    public List<int> Cells = new();
    public float Spill = float.MaxValue;
    public float Depth;
    public bool River;
    public Vector2 Center;
}

public sealed partial class Hydrology
{
    private const float FLOOD_EPSILON = 2e-3f;
    private const float DRY_RISE = 2f;
    private const float DEPRESSION = 0.15f;
    private const float LAKE_DROP = 0.1f;
    private const float LAKE_HOLD = 0.3f;
    private const float RIVER_POND_GAP = 56f;
    private const float PERCHED_RISE = 1f;
    public const float RESAMPLE_STEP = 4f;
    private const float MIN_RIVER_CELLS = 4;
    private const int RECONCILE_PASSES = 12;
    private const float LOWER_MARGIN = 0.02f;
    private const float INFLOW_FREEBOARD = 0.1f;
    private const int BRIDGE_GAP = 6;
    private const float MAX_WATER_GRADE = 0.12f;
    public const float RIVER_PAD = 10f;
    public const float MERGE_TOLERANCE = 0.3f;
    private const int SETTLE_TAPS = 4;
    private const float MAX_JOIN_GRADE = 0.05f;
    private const float OUTFLOW_GRADE = 0.01f;
    private const float OUTFLOW_CLEARANCE = 0.1f;
    private const float SIMPLIFY_CELLS = 0.5f;
    private const int CHAIKIN_PASSES = 3;
    private const int BEND_PASSES = 6;
    private const float BEND_MIN_RADIUS = 10f;
    private const float BEND_WIDTHS = 1.5f;
    private const int SETTLE_SMOOTHING = 3;
    private const float MEANDER_CELLS = 1.2f;
    private const float MEANDER_WIDTHS = 1f;
    private const float MEANDER_WAVE_CELLS = 5f;
    private const float MEANDER_WAVE_WIDTHS = 8f;
    private const float CONFINED_GRADE = 0.15f;
    private const float OPEN_GRADE = 0.03f;
    public const float WIDTH_RAMP = 0.15f;
    private const int JOIN_BLEND_POINTS = 8;
    private const int CONFLUENCE_ROUNDS = 4;
    private const float CONFLUENCE_TOLERANCE = 0.05f;
    private const float STREAM_WIDTH = 10f;
    public const float SOURCE_WIDTH = 2.5f;
    private const float SOURCE_DEPTH = 1.2f;
    private const float HEADWATER_DOUBLINGS = 2f;
    private const float HEADWATER_WIDTH = 13f;
    private const float CHANNEL_FREEBOARD = 0.4f;
    private const float CHANNEL_FREEBOARD_PER_WIDTH = 0.03f;
    private const float MAX_CHANNEL_FREEBOARD = 1f;
    private const float MAX_DEEPEN = 1.5f;
    private const float DEEPEN_GRADE = 0.03f;
    private const float POOL_WIDTHS = 6f;
    private const float POOL_MIN_SPACING = 16f;
    private const float POOL_MAX_SPACING = 60f;
    private const float POOL_MIN_GRADE = 0.015f;
    private const float POOL_MIN_LENGTH = 6f;
    private const float RIFFLE_GRADE = 0.14f;
    private const float MAX_RIFFLE = 32f;
    private const float STEEPEST_RIFFLE = 0.145f;
    private const float POOL_DROP = 0.3f;
    private const float POOL_DROP_PER_WIDTH = 0.05f;
    private const float MAX_POOL_DROP = 1f;
    private const float WIDTH_PER_DOUBLING = 3f;
    private const float MAX_WIDTH = 24f;
    private const float STREAM_DEPTH = 3.5f;
    private const float DEPTH_PER_DOUBLING = 0.5f;
    private const float MAX_DEPTH = 6f;
    private const float FREEBOARD_DEPTH = 1f;
    private const int MIN_COURSE_POINTS = 8;
    private const int EXTEND_CELLS = 96;
    private const float JOIN_SLACK = 8f;
    private const int BASIN_DRY_REACH = 3;
    private const float MAIN_REACH = 40f;
    private const int MIN_MAIN_CELLS = 64;
    private const int OUTLET_CELLS = 2;
    private const float ESTUARY_BAND = 0.5f;
    private const float BACKWATER_MIN = 12f;
    private const float BACKWATER_WIDTHS = 2f;
    public const int SHORE_SLACK = 2;

    private static readonly float[] BANK_TAPS = { 0.25f, 0.5f };
    private static readonly float[] CHANNEL_REACHES = { 2f, 6f };

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly WorldGenerationConfig _config;
    private readonly WaterGenerationSettings _settings;
    private readonly HeightMap _map;
    private readonly WaterStampLibrary _stamps;
    private readonly WaterClimate _climate;
    private readonly DrainageValleyReport _valleys;
    private bool[] _main;
    private float[] _floors;
    private readonly Dictionary<int, string> _lowerReasons = new();
    private int _levelling = -1;
    private HydrologyGrid _grid;

    private sealed class RiverCourse
    {
        public List<Vector2> Points;
        public List<float> Areas;
        public int JoinPath;
        public int JoinCell;
        public List<int> Cells;
        public WaterStampCourse Stamp;
        public float MaxWidth = MAX_WIDTH;
        public bool Main;
    }

    public List<HydrologyBasin> Basins { get; } = new();
    public MainRiverPlan MainRiver { get; private set; }
    public WaterStampLayout Stamps { get; }
    public int Lakes { get; private set; }
    public int Ponds { get; private set; }
    public int Reconciled { get; private set; }
    public int DroppedCourses { get; private set; }
    public int ShortenedCourses { get; private set; }
    public int ExtendedCourses { get; private set; }
    public int CrowdedBodies { get; private set; }
    public int PerchedBodies { get; private set; }
    public int MainCells { get; private set; }
    public HydrologyLog Log { get; } = new();
    public DrainageValleyReport Valleys => _valleys;

    public Hydrology(WorldGenerationConfig config, HeightMap map, WaterStampLibrary stamps = null, WaterClimate climate = null, DrainageValleyReport valleys = null)
    {
        _config = config;
        _settings = config.Water;
        _map = map;
        _stamps = stamps;
        _climate = climate;
        _valleys = valleys;
        MainRiver = valleys?.MainPlan;

        if (stamps != null)
            Stamps = new WaterStampLayout();
    }

    public WaterMap Build(HydrologyGrid grid)
    {
        var water = new WaterMap(grid.Resolution, grid.CellSize, _config.WorldSize, _config.SeaLevel) { ShoreReach = _settings.ShoreReach, Filled = grid.Filled };
        var owners = new List<HydrologyBasin>();

        _main = MainMask(grid);

        string analysed = $"grid {GenerationChecksum.Of(grid.Height)}, receivers {GenerationChecksum.Of(grid.Receiver)}, accumulation {GenerationChecksum.Of(grid.Accumulation)}";

        SelectLakes(grid, water, owners);

        if (_stamps != null)
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapWaterStampLakes))
                new WaterStampLakes(_config, _stamps, _map, grid, water, Stamps).Apply(owners);
        }

        LakeRouting.Apply(grid, water, owners);

        List<(List<int> Cells, int JoinPath, int JoinCell)> paths = TracePaths(grid);
        string traced = $"lakes {GenerationChecksum.Of(_map.Heights)}, bodies {GenerationChecksum.Of(water.BodyIds)}, paths {GenerationChecksum.Of(paths.ConvertAll(path => path.Cells))}";
        List<RiverCourse> courses = BuildCourses(grid, paths, water);
        UnityEngine.Debug.Log($"Hydrology checksums: {analysed}, {traced}, courses {GenerationChecksum.Of(courses.ConvertAll(course => course.Points))}");

        Capture(courses, water);
        MergeHeads(courses);

        _floors = Floors(grid, water);
        ResolveEnds(grid, water, courses, true);
        DropStubs(courses);
        JoinTributaries(courses);
        CarryFlow(courses);
        PruneCrowded(grid, water, courses);

        for (int pass = 0; pass <= RECONCILE_PASSES; pass++)
        {
            var lowered = new Dictionary<int, float>();

            Profile(grid, water, courses, lowered);
            Touching(grid, water, lowered);

            if (lowered.Count == 0 || pass == RECONCILE_PASSES)
                break;

            foreach (KeyValuePair<int, float> pair in lowered)
            {
                WaterBody before = water.Bodies[pair.Key];
                Lower(grid, water, owners[pair.Key], pair.Key, Mathf.Max(pair.Value - LOWER_MARGIN, _config.SeaLevel));
                WaterBody after = water.Bodies[pair.Key];

                if (after.Surface < before.Surface)
                    Log.Note(HydrologyAction.BodyLowered, -1, before.Center, $"body {pair.Key} lowered from {before.Surface:0.00} to {after.Surface:0.00} m: {_lowerReasons[pair.Key]}; {before.Area:0} to {after.Area:0} m2 flooded");
            }

            ExtendToWater(grid, water, courses);
            ResolveEnds(grid, water, courses, false);
            MergeHeads(courses);
            CarryFlow(courses);
            Reconciled++;
        }

        DropPerched(water);
        water.InvalidateIndex();
        Merge(water);
        SettleMouths(water);
        MeetTrunks(water);
        PondRiverEnds(grid, water);
        water.Stamps = Stamps;

        return water;
    }
}

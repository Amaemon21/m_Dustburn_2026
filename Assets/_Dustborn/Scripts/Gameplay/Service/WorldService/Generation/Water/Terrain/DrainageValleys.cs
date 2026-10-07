using System.Collections.Generic;

public static partial class DrainageValleys
{
    private const float MIN_GRADE = 0.001f;
    private const float PIN_WEIGHT = 1000f;
    private const float WALL_CURVE = 0.002f;
    private const float MIN_FLOOR = 10f;
    private const float MAX_FLOOR = 60f;
    private const float FLOOR_PER_DOUBLING = 8f;
    private const float BLEND = 3f;
    private const float MOUTH_DEPTH = 1f;
    private const float SEA_CLEARANCE = 0.5f;
    private const float FILL_EDGE = 0.5f;
    private const float FILL_CLEARANCE = 0.05f;
    private const float SHAPE_STEP = 4f;
    private const int TILE_NODES = 16;
    private const int SAMPLES = 4;
    private const int CHAIKIN_PASSES = 2;
    private const float RESAMPLE = 16f;
    private const float DRY_FREEBOARD = 1.5f;
    private const float TERMINAL_MIN_RADIUS = 28f;
    private const float TERMINAL_MAX_RADIUS = 80f;
    private const float TERMINAL_RADIUS_PER_DOUBLING = 10f;
    private const float TERMINAL_CLEARANCE = 48f;
    private const float TERMINAL_DEPTH = 4f;
    private const float TERMINAL_SEA_CLEARANCE = 1f;
    private const int TERMINAL_MIN_CELLS = 3;

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public static bool Active(WorldGenerationConfig config)
    {
        WaterGenerationSettings water = config.Water;

        return water != null && water.Enabled && water.DrainageValleys;
    }

    public static DrainageValleyReport Shape(HeightMap map, WorldGenerationConfig config, WaterClimate climate)
    {
        var report = new DrainageValleyReport();

        if (!Active(config))
            return report;

        Grid grid = Sample(map, config, climate);
        Outlets(grid, config, report);
        Flood(grid);
        Route(grid);
        Accumulate(grid);

        float[] levels = Solve(grid, config, report);

        if (config.Water.ThroughRiver)
            ChooseMain(config, climate, report);
        List<Segment> segments = Segments(grid, config, report, levels);
        Carve(map, segments, config);

        if (CoastShaper.Active(config))
            report.LiftedCells = CoastShaper.LiftInland(map, config.SeaLevel, CoastShaper.INLAND_MARGIN, out report.Inlets);

        return report;
    }
}

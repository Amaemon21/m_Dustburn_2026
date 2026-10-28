public static partial class WaterShore
{
    public const float SHELF = 0.35f;
    public const float DRY_LIFT = 0.05f;
    public const float SUNK_DEPTH = 0.1f;
    public const float SHELF_FADE_START = 1f;
    public const float SHELF_FADE_END = 2.5f;
    public const float DAM_REACH = 6f;
    public const int RESURFACE_PASSES = 3;
    public const float RESURFACE_TOLERANCE = 0.1f;
    public const float SETTLE_GRADE = 0.12f;
    public const float MIN_OPEN_LENGTH = 16f;

    public static StandingWaterRegions Finish(WaterMap water, HeightMap map)
    {
        var regions = new StandingWaterRegions(water, map);

        BuildRegions(regions);

        for (int pass = 0; pass < RESURFACE_PASSES && ResurfaceRivers(water); pass++)
            BuildRegions(regions);

        using (WorldGenProbe.Measure(WorldGenStage.MapShoreResurface))
        {
            Hydrology.MeetTrunks(water);
            TaperSources(water);
        }

        MeasureDistances(water);

        using (WorldGenProbe.Measure(WorldGenStage.MapShoreShelf))
            Shelf(water, map, regions);

        using (WorldGenProbe.Measure(WorldGenStage.MapShoreRegions))
            regions.Publish();

        MeasureDistances(water);
        water.MarkShoreReady();

        return regions;
    }

    private static void BuildRegions(StandingWaterRegions regions)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.MapShoreRegions);
        regions.Build(SHELF);
        regions.Publish();
    }

    private static bool ResurfaceRivers(WaterMap water)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.MapShoreResurface);
        return Resurface(water) | DropDeadRivers(water);
    }

    private static void MeasureDistances(WaterMap water)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.MapShoreDistances);
        Distances(water);
    }
}

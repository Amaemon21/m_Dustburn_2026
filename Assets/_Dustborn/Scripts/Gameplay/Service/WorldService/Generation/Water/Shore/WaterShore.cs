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

        regions.Build(SHELF);
        regions.Publish();

        for (int pass = 0; pass < RESURFACE_PASSES && (Resurface(water) | DropDeadRivers(water)); pass++)
        {
            regions.Build(SHELF);
            regions.Publish();
        }

        Hydrology.MeetTrunks(water);
        TaperSources(water);
        Distances(water);

        Shelf(water, map, regions);

        regions.Publish();
        Distances(water);
        water.MarkShoreReady();

        return regions;
    }
}

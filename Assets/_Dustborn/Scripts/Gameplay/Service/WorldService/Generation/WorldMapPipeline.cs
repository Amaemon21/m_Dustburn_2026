using System;
using System.Collections.Generic;
using System.Threading;

public sealed class WorldMapResult
{
    public BiomeMap Biomes { get; }
    public HeightMap Heights { get; }
    public RoadNetwork Roads { get; }
    public float[] RoadMask { get; }
    public List<PoiPlacement> Placements { get; }
    public List<SettlementLayout> Settlements { get; }

    public WorldMapResult(BiomeMap biomes, HeightMap heights, RoadNetwork roads, float[] roadMask, List<PoiPlacement> placements,
        List<SettlementLayout> settlements)
    {
        Biomes = biomes;
        Heights = heights;
        Roads = roads;
        RoadMask = roadMask;
        Placements = placements;
        Settlements = settlements;
    }
}

public sealed class WorldMapPipeline
{
    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;
    private readonly PoiDatabase _pois;

    public WorldMapPipeline(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois)
    {
        _config = config;
        _biomes = biomes;
        _pois = pois;
    }

    public WorldMapResult Generate(Action<string, float> progress = null, CancellationToken cancellationToken = default)
    {
        void Report(string stage, float fraction)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(stage, fraction);
            cancellationToken.ThrowIfCancellationRequested();
        }

        long total = WorldGenProbe.Now;

        Report("Карта биомов", 0.02f);
        BiomeMap biomes;

        using (WorldGenProbe.Measure(WorldGenStage.MapBiomes))
            biomes = new BiomeMapGenerator(_config, _biomes).Generate();

        Report("Рельеф и эрозия", 0.08f);
        HeightMap heights;

        using (WorldGenProbe.Measure(WorldGenStage.MapHeights))
            heights = new HeightMapGenerator(_config, _biomes).Generate(biomes);

        Report("Поселения и кварталы", 0.32f);
        var roads = new RoadNetwork();

        using (WorldGenProbe.Measure(WorldGenStage.MapHubs))
        {
            roads.Hubs.AddRange(new HubPlacer(_config, heights).Place());
            roads.Links.AddRange(RoadGraph.Link(roads.Hubs, _config.RoadExtraEdges));
        }

        var planner = new SettlementPlanner(_config, _pois, heights);
        List<SettlementLayout> settlements;

        using (WorldGenProbe.Measure(WorldGenStage.MapCities))
            settlements = planner.Plan(roads.Hubs, roads.Links);

        foreach (SettlementLayout settlement in settlements)
            roads.Streets.AddRange(settlement.Streets);

        Report("Дороги между поселениями", 0.38f);
        var carver = new TerrainCarver(_config);

        using (WorldGenProbe.Measure(WorldGenStage.MapSettlementPads))
            heights = carver.CarveSettlements(heights, settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadPlan))
            roads.Roads.AddRange(new RoadPlanner(_config, heights).Plan(roads.Hubs, roads.Links, settlements));

        Report("Врезка дорог и улиц", 0.42f);
        float[] mask;

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveStreets))
            heights = carver.CarveStreets(heights, settlements, out mask);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveTrunk))
            heights = carver.CarveHighways(heights, roads.Roads, mask);

        Report("Участки и здания", 0.47f);
        RoadProximity proximity;

        using (WorldGenProbe.Measure(WorldGenStage.MapProximity))
        {
            proximity = new RoadProximity(roads.Roads, _config.WorldSize, _config.RoadCellSize);
            proximity.AddRange(roads.Streets);
        }

        using (WorldGenProbe.Measure(WorldGenStage.MapLots))
            planner.CutLots(settlements, proximity);

        var placer = new PoiPlacer(_config, _pois, heights, proximity);
        List<PoiPlacement> placements;

        using (WorldGenProbe.Measure(WorldGenStage.MapPoiPlace))
            placements = placer.Place(settlements, roads);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarvePads))
            heights = carver.CarvePads(heights, placements);

        using (WorldGenProbe.Measure(WorldGenStage.MapApplyHeights))
            placer.ApplyHeights(heights);

        Report("Карты готовы", 0.53f);
        WorldGenProbe.Record(WorldGenStage.MapTotal, total, WorldGenProbe.Now, placements.Count);

        return new WorldMapResult(biomes, heights, roads, mask, placements, settlements);
    }
}

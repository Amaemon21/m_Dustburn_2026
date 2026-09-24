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
    public WaterMap Water { get; }
    public List<TerrainStampPlacement> Stamps { get; }

    public WorldMapResult(BiomeMap biomes, HeightMap heights, RoadNetwork roads, float[] roadMask, List<PoiPlacement> placements,
        List<SettlementLayout> settlements, WaterMap water, List<TerrainStampPlacement> stamps)
    {
        Biomes = biomes;
        Heights = heights;
        Roads = roads;
        RoadMask = roadMask;
        Placements = placements;
        Settlements = settlements;
        Water = water;
        Stamps = stamps;
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

        Report("Рельеф, штампы и эрозия", 0.08f);
        HeightMap heights;
        var relief = new HeightMapGenerator(_config, _biomes);

        using (WorldGenProbe.Measure(WorldGenStage.MapHeights))
            heights = relief.Generate(biomes);

        Report("Реки и озёра", 0.26f);
        heights = Hydrate(heights);
        WaterMap water = heights.Water;

        Report("Поселения и кварталы", 0.32f);
        var roads = new RoadNetwork();

        using (WorldGenProbe.Measure(WorldGenStage.MapHubs))
            roads.Hubs.AddRange(new HubPlacer(_config, heights).Place());

        using (WorldGenProbe.Measure(WorldGenStage.MapRegionalPlan))
            roads.SetPlan(new RegionalGraphPlanner(_config, heights).Plan(roads.Hubs));

        var planner = new SettlementPlanner(_config, _pois, heights);
        List<SettlementLayout> settlements;

        using (WorldGenProbe.Measure(WorldGenStage.MapCities))
            settlements = planner.Plan(roads.Hubs, roads.RegionalLinks);

        roads.Publish(_config, settlements);

        Report("Дороги между поселениями", 0.38f);
        var carver = new TerrainCarver(_config);

        using (WorldGenProbe.Measure(WorldGenStage.MapSettlementPads))
            heights = carver.CarveSettlements(heights, settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadPlan))
            roads.Graph = new RoadPlanner(_config, heights).Plan(roads.Hubs, roads.RegionalLinks, settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapDirtAccess))
            roads.RuralSites.AddRange(new DirtAccessPlanner(_config, heights).Plan(roads.Graph, settlements, roads.Streets));

        roads.Publish(_config, settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterCrossings))
            water.FindCrossings(roads.Roads);

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

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Refresh(water, heights);

        Report("Карты готовы", 0.53f);
        WorldGenProbe.Record(WorldGenStage.MapTotal, total, WorldGenProbe.Now, placements.Count);

        return new WorldMapResult(biomes, heights, roads, mask, placements, settlements, water, relief.Stamps);
    }

    public HeightMap Hydrate(HeightMap heights)
    {
        return Hydrate(_config, heights, out _);
    }

    public static HeightMap Hydrate(WorldGenerationConfig config, HeightMap heights, out Hydrology hydrology)
    {
        WaterGenerationSettings settings = config.Water;
        WaterMap water;
        hydrology = null;

        if (settings == null || !settings.Enabled)
        {
            float cell = settings == null ? 8f : settings.CellSize;
            int resolution = Math.Max(2, (int)Math.Ceiling(config.WorldSize / cell));

            water = new WaterMap(resolution, config.WorldSize / (float)resolution, config.WorldSize, config.SeaLevel)
            {
                ShoreReach = settings == null ? 0f : settings.ShoreReach
            };

            using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
                WaterShore.Finish(water, heights);

            heights.Water = water;
            return heights;
        }

        WaterStampLibrary stamps = WaterStampLibrary.Create(settings);
        hydrology = new Hydrology(config, heights, stamps);

        using (WorldGenProbe.Measure(WorldGenStage.MapHydrology))
            water = hydrology.Build(hydrology.Analyze());

        HeightMap carved;

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveRivers))
            carved = RiverCarver.Carve(heights, water, settings.RiverBankWidth);

        if (stamps != null)
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapWaterStampCarve))
                WaterStampCarver.Carve(carved, heights, water, stamps);

            UnityEngine.Debug.Log(water.Stamps.Summary());
        }

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Finish(water, carved);

        return carved;
    }
}

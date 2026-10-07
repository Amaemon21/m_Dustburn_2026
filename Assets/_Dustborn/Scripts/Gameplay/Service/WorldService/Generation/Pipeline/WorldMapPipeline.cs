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
    private const string DONE = "Карты готовы";
    private const float DONE_PROGRESS = 0.53f;
    private const int SETTLE_ROUNDS = 4;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;
    private readonly PoiDatabase _pois;

    private sealed class Context
    {
        public WorldGenerationConfig Config;
        public BiomeDatabase BiomeSet;
        public PoiDatabase Pois;
        public BiomeMap Biomes;
        public HeightMapGenerator Relief;
        public HeightMap Heights;
        public WaterMap Water;
        public RoadNetwork Roads;
        public SettlementPlanner Planner;
        public List<SettlementLayout> Settlements;
        public TerrainCarver Carver;
        public float[] RoadMask;
        public List<PoiPlacement> Placements;
    }

    private readonly struct Stage
    {
        public readonly string Title;
        public readonly float Progress;
        public readonly Action<Context> Run;

        public Stage(string title, float progress, Action<Context> run)
        {
            Title = title;
            Progress = progress;
            Run = run;
        }
    }

    private static readonly Stage[] Stages =
    {
        new("Карта биомов", 0.02f, Biomes),
        new("Рельеф, штампы и эрозия", 0.08f, Relief),
        new("Реки и озёра", 0.26f, Water),
        new("Поселения и кварталы", 0.32f, Settlements),
        new("Дороги между поселениями", 0.38f, Highways),
        new("Врезка дорог и улиц", 0.42f, CarveRoads),
        new("Участки и здания", 0.47f, Buildings)
    };

    public WorldMapPipeline(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois)
    {
        _config = config;
        _biomes = biomes;
        _pois = pois;
    }

    public WorldMapResult Generate(Action<string, float> progress = null, CancellationToken cancellationToken = default, HeightMap relief = null)
    {
        void Report(string stage, float fraction)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(stage, fraction);
            cancellationToken.ThrowIfCancellationRequested();
        }

        long total = WorldGenProbe.Now;
        var context = new Context { Config = _config, BiomeSet = _biomes, Pois = _pois, Heights = relief };

        foreach (Stage stage in Stages)
        {
            Report(stage.Title, stage.Progress);
            stage.Run(context);
        }

        Report(DONE, DONE_PROGRESS);
        WorldGenProbe.Record(WorldGenStage.MapTotal, total, WorldGenProbe.Now, context.Placements.Count);

        return new WorldMapResult(context.Biomes, context.Heights, context.Roads, context.RoadMask, context.Placements, context.Settlements, context.Water, context.Relief.Stamps);
    }

    private static void Biomes(Context context)
    {
        using (WorldGenProbe.Measure(WorldGenStage.MapBiomes))
            context.Biomes = new BiomeMapGenerator(context.Config, context.BiomeSet).Generate();
    }

    private static void Relief(Context context)
    {
        context.Relief = new HeightMapGenerator(context.Config, context.BiomeSet);

        if (context.Heights != null)
            return;

        using (WorldGenProbe.Measure(WorldGenStage.MapHeights))
            context.Heights = context.Relief.Generate(context.Biomes);
    }

    private static void Water(Context context)
    {
        context.Heights = Hydrate(context.Config, context.Heights, out context.Water, out _, WaterClimate.From(context.Biomes, context.BiomeSet));
    }

    private static void Settlements(Context context)
    {
        WorldGenerationConfig config = context.Config;
        RoadNetwork roads = context.Roads = new RoadNetwork();

        using (WorldGenProbe.Measure(WorldGenStage.MapHubs))
            roads.Hubs.AddRange(new HubPlacer(config, context.Heights, context.Water).Place());

        using (WorldGenProbe.Measure(WorldGenStage.MapRegionalPlan))
            roads.SetPlan(new RegionalGraphPlanner(config, context.Heights, context.Water).Plan(roads.Hubs));

        context.Planner = new SettlementPlanner(config, context.Pois, context.Heights, context.Water);

        using (WorldGenProbe.Measure(WorldGenStage.MapCities))
            context.Settlements = context.Planner.Plan(roads.Hubs, roads.RegionalLinks);

        roads.Publish(config, context.Settlements);
    }

    private static void Highways(Context context)
    {
        WorldGenerationConfig config = context.Config;
        RoadNetwork roads = context.Roads;
        context.Carver = new TerrainCarver(config, context.Water);

        using (WorldGenProbe.Measure(WorldGenStage.MapSettlementPads))
            context.Heights = context.Carver.CarveSettlements(context.Heights, context.Settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapRoadPlan))
            roads.Graph = new RoadPlanner(config, context.Heights, context.Water).Plan(roads.Hubs, roads.RegionalLinks, context.Settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapDirtAccess))
            roads.RuralSites.AddRange(new DirtAccessPlanner(config, context.Heights, context.Water).Plan(roads.Graph, context.Settlements, roads.Streets));

        roads.Publish(config, context.Settlements);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterCrossings))
            context.Water.FindCrossings(Combined(roads.Roads, roads.Streets));
    }

    private static void CarveRoads(Context context)
    {
        using (WorldGenProbe.Measure(WorldGenStage.MapCarveStreets))
            context.Heights = context.Carver.CarveStreets(context.Heights, context.Settlements, out context.RoadMask);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveTrunk))
            context.Heights = context.Carver.CarveHighways(context.Heights, context.Roads.Roads, context.RoadMask);
    }

    private static void Buildings(Context context)
    {
        WorldGenerationConfig config = context.Config;
        RoadProximity proximity;

        using (WorldGenProbe.Measure(WorldGenStage.MapProximity))
        {
            proximity = new RoadProximity(context.Roads.Roads, config.WorldSize, config.RoadCellSize);
            proximity.AddRange(context.Roads.Streets);
        }

        using (WorldGenProbe.Measure(WorldGenStage.MapLots))
            context.Planner.CutLots(context.Settlements, proximity);

        var placer = new PoiPlacer(config, context.Pois, context.Heights, context.Water, proximity);

        using (WorldGenProbe.Measure(WorldGenStage.MapPoiPlace))
            context.Placements = placer.Place(context.Settlements, context.Roads);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarvePads))
            context.Heights = context.Carver.CarvePads(context.Heights, context.Placements);

        using (WorldGenProbe.Measure(WorldGenStage.MapApplyHeights))
            placer.ApplyHeights(context.Heights);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveRivers))
            RiverCarver.Restore(context.Heights, context.Water, config.Water.BridgeMinWidth, context.RoadMask);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Refresh(context.Water, context.Heights);

        if (config.Water != null)
            RiverEnds.Label(context.Water, config.Water.RiverStartArea * 1e6f);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterCrossings))
            WaterCrossings.Finish(context.Water, context.Heights, config, context.Carver.Bridges);
    }

    private static List<Road> Combined(IEnumerable<Road> first, IEnumerable<Road> second)
    {
        var roads = new List<Road>(first);
        roads.AddRange(second);
        return roads;
    }

    public static HeightMap Hydrate(WorldGenerationConfig config, HeightMap heights, out WaterMap water, out Hydrology hydrology, WaterClimate climate = null)
    {
        string relief = GenerationChecksum.Of(heights.Heights);
        climate?.LiftAboveSea(heights, config.SeaLevel);
        string lifted = GenerationChecksum.Of(heights.Heights);

        using (WorldGenProbe.Measure(WorldGenStage.MapCoast))
            CoastShaper.Shape(heights, config);

        string coast = GenerationChecksum.Of(heights.Heights);

        DrainageValleyReport valleys = null;

        if (DrainageValleys.Active(config))
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapDrainageValleys))
                valleys = DrainageValleys.Shape(heights, config, climate);

            UnityEngine.Debug.Log(valleys.Describe());
        }

        UnityEngine.Debug.Log($"Water input checksums: relief {relief}, arid lift {lifted}, coast {coast}, valleys {GenerationChecksum.Of(heights.Heights)}");

        WaterGenerationSettings settings = config.Water;
        hydrology = null;

        if (settings == null || !settings.Enabled)
        {
            float cell = settings == null ? 8f : settings.CellSize;
            int resolution = Math.Max(2, (int)Math.Ceiling(config.WorldSize / cell));

            water = new WaterMap(resolution, config.WorldSize / (float)resolution, config.WorldSize, config.SeaLevel)
            {
                ShoreReach = settings == null ? 0f : settings.ShoreReach
            };

            water.ApplyClimate(climate);

            using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
                WaterShore.Finish(water, heights);

            return heights;
        }

        WaterStampLibrary stamps = WaterStampLibrary.Create(settings);
        hydrology = new Hydrology(config, heights, stamps, climate, valleys);

        using (WorldGenProbe.Measure(WorldGenStage.MapHydrology))
            water = hydrology.Build(hydrology.Analyze());

        water.ApplyClimate(climate);

        HeightMap carved = SettleWater(heights, water, stamps, settings);

        if (stamps != null)
            UnityEngine.Debug.Log(water.Stamps.Summary());

        LakeBeds.Deepen(carved, water);

        RiverEnds.Label(water, settings.RiverStartArea * 1e6f);
        UnityEngine.Debug.Log($"Water output checksums: heights {GenerationChecksum.Of(carved.Heights)}, bodies {GenerationChecksum.Of(water.BodyIds)}");

        return carved;
    }

    private static HeightMap SettleWater(HeightMap heights, WaterMap water, WaterStampLibrary stamps, WaterGenerationSettings settings)
    {
        (byte[] Kinds, short[] BodyIds) ownership = water.Ownership();
        (bool Start, bool End)[] loose = RiverEnds.Loose(water);
        var cache = new RiverCarver.CarveCache();

        for (int round = 1; ; round++)
        {
            WaterLevels levels = WaterLevels.Of(water, loose);
            HeightMap carved = CarveWater(heights, water, stamps, settings, loose, cache);
            loose = RiverEnds.Loose(water);
            int changes = levels.Changes(water, loose);

            if (changes == 0)
            {
                if (round > 1)
                    UnityEngine.Debug.Log($"Water levels settled after {round} carving rounds, {cache.Reused} of {cache.Reused + cache.Computed} river tiles reused");

                return carved;
            }

            if (round == SETTLE_ROUNDS)
            {
                UnityEngine.Debug.LogWarning($"Water levels still moved after {round} carving rounds ({changes} changes, e.g. {levels.Describe(water, loose, 3)}); the channels were cut for the previous levels");
                return carved;
            }

            water.ResetOwnership(ownership);
        }
    }

    private static HeightMap CarveWater(HeightMap heights, WaterMap water, WaterStampLibrary stamps, WaterGenerationSettings settings, (bool Start, bool End)[] loose, RiverCarver.CarveCache cache)
    {
        HeightMap carved;

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveRivers))
            carved = RiverCarver.Carve(heights, water, settings.RiverBankWidth, loose, cache);

        if (stamps != null)
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapWaterStampCarve))
                WaterStampCarver.Carve(carved, heights, water, stamps);
        }

        LakeBeds.SmoothShores(carved, water);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Finish(water, carved);

        return carved;
    }
}

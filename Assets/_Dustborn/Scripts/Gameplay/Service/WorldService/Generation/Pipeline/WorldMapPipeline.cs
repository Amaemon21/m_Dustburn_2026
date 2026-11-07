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
    public List<PropPlacement> Props { get; }
    public WorldGameplayPlan Gameplay { get; }

    public WorldMapResult(BiomeMap biomes, HeightMap heights, RoadNetwork roads, float[] roadMask, List<PoiPlacement> placements,
        List<SettlementLayout> settlements, WaterMap water, List<TerrainStampPlacement> stamps, List<PropPlacement> props,
        WorldGameplayPlan gameplay = null)
    {
        Biomes = biomes;
        Heights = heights;
        Roads = roads;
        RoadMask = roadMask;
        Placements = placements;
        Settlements = settlements;
        Water = water;
        Stamps = stamps;
        Props = props ?? new List<PropPlacement>();
        Gameplay = gameplay;
    }
}

public readonly struct WorldMapStep
{
    public readonly string Title;
    public readonly float Progress;
    public readonly WorldMapResult Result;

    public WorldMapStep(string title, float progress, WorldMapResult result)
    {
        Title = title;
        Progress = progress;
        Result = result;
    }
}

public sealed class WorldMapPipeline
{
    private const string DONE = "\u041A\u0430\u0440\u0442\u044B \u0433\u043E\u0442\u043E\u0432\u044B";
    private const float DONE_PROGRESS = 0.53f;
    private const int SETTLE_ROUNDS = 4;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;
    private readonly PoiDatabase _pois;
    private readonly PropDatabase _props;

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
        public PropDatabase PropSet;
        public List<PropPlacement> Props;
        public WorldGameplayPlan Gameplay;
        public CancellationToken Token;
        public UnityEngine.Vector2? Start;
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
        new("\u0421\u043E\u0437\u0434\u0430\u043D\u0438\u0435 \u043A\u0430\u0440\u0442\u044B \u0431\u0438\u043E\u043C\u043E\u0432", 0.02f, Biomes),
        new("\u0424\u043E\u0440\u043C\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435 \u0440\u0435\u043B\u044C\u0435\u0444\u0430", 0.08f, Relief),
        new("\u0421\u043E\u0437\u0434\u0430\u043D\u0438\u0435 \u0440\u0435\u043A \u0438 \u043E\u0437\u0451\u0440", 0.26f, Water),
        new("\u041F\u043B\u0430\u043D\u0438\u0440\u043E\u0432\u043A\u0430 \u043F\u043E\u0441\u0435\u043B\u0435\u043D\u0438\u0439", 0.32f, Settlements),
        new("\u041F\u0440\u043E\u043A\u043B\u0430\u0434\u043A\u0430 \u043C\u0430\u0433\u0438\u0441\u0442\u0440\u0430\u043B\u0435\u0439", 0.38f, Highways),
        new("\u0412\u044B\u0440\u0430\u0432\u043D\u0438\u0432\u0430\u043D\u0438\u0435 \u0434\u043E\u0440\u043E\u0433 \u0438 \u0443\u043B\u0438\u0446", 0.42f, CarveRoads),
        new("\u0420\u0430\u0437\u043C\u0435\u0449\u0435\u043D\u0438\u0435 \u0437\u0434\u0430\u043D\u0438\u0439", 0.47f, Buildings),
        new("\u041D\u0430\u0441\u0442\u0440\u043E\u0439\u043A\u0430 \u0443\u0440\u043E\u0432\u043D\u044F \u0432\u043E\u0434\u044B", 0.5f, ReconcileWater),
        new("\u041F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0430 \u0441\u0442\u0430\u0440\u0442\u043E\u0432\u043E\u0439 \u0437\u043E\u043D\u044B", 0.515f, Gameplay),
        new("\u0420\u0430\u0437\u043C\u0435\u0449\u0435\u043D\u0438\u0435 \u043E\u0431\u044A\u0435\u043A\u0442\u043E\u0432", 0.52f, Props)
    };

    public static IReadOnlyList<string> StageTitles { get; } = Array.ConvertAll(Stages, stage => stage.Title);

    public WorldMapPipeline(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois, PropDatabase props = null)
    {
        _config = config;
        _biomes = biomes;
        _pois = pois;
        _props = props;
    }

    public WorldMapResult Generate(Action<string, float> progress = null, CancellationToken cancellationToken = default, HeightMap relief = null)
    {
        foreach (WorldMapStep step in Steps(relief, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(step.Title, step.Progress);
            cancellationToken.ThrowIfCancellationRequested();

            if (step.Result != null)
                return step.Result;
        }

        throw new InvalidOperationException("The map pipeline ended without a result");
    }

    public IEnumerable<WorldMapStep> Steps(HeightMap relief = null, CancellationToken cancellationToken = default)
    {
        long total = WorldGenProbe.Now;
        var context = new Context { Config = _config, BiomeSet = _biomes, Pois = _pois, PropSet = _props, Heights = relief, Token = cancellationToken };

        foreach (Stage stage in Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new WorldMapStep(stage.Title, stage.Progress, null);
            cancellationToken.ThrowIfCancellationRequested();
            stage.Run(context);
        }

        WorldGenProbe.Record(WorldGenStage.MapTotal, total, WorldGenProbe.Now, context.Placements.Count);

        yield return new WorldMapStep(DONE, DONE_PROGRESS, new WorldMapResult(context.Biomes, context.Heights, context.Roads,
            context.RoadMask, context.Placements, context.Settlements, context.Water, context.Relief.Stamps, context.Props, context.Gameplay));
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
            roads.Hubs.AddRange(new HubPlacer(config, context.Heights, context.Water, context.BiomeSet, context.Biomes).Place());

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

        if (config.Gameplay?.Enabled == true)
        {
            using WorldGenProbe.Span reservation = WorldGenProbe.Measure(WorldGenStage.MapStartReservation);
            context.Start = GameplayLandmarkPlanner.Reserve(config, context.BiomeSet, context.Biomes, context.Heights,
                context.Water, context.Settlements, context.Pois, proximity, context.Token);
        }

        var placer = new PoiPlacer(config, context.Pois, context.Heights, context.Water, proximity, context.BiomeSet, context.Biomes, context.Start);

        using (WorldGenProbe.Measure(WorldGenStage.MapPoiPlace))
            context.Placements = placer.Place(context.Settlements, context.Roads);

        using (WorldGenProbe.Measure(WorldGenStage.MapCarvePads))
            context.Heights = context.Carver.CarvePads(context.Heights, context.Placements);

        using (WorldGenProbe.Measure(WorldGenStage.MapApplyHeights))
            placer.ApplyHeights(context.Heights);
    }

    private static void ReconcileWater(Context context)
    {
        WorldGenerationConfig config = context.Config;

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveRivers))
            RiverCarver.Restore(context.Heights, context.Water, config.Water.BridgeMinWidth, context.RoadMask);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Refresh(context.Water, context.Heights);

        if (config.Water != null)
            RiverEnds.Label(context.Water, config.Water.RiverStartArea * 1e6f);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterCrossings))
            WaterCrossings.Finish(context.Water, context.Heights, config, context.Carver.Bridges);
    }

    private static void Props(Context context)
    {
        if (context.PropSet == null)
        {
            context.Props = new List<PropPlacement>();
            return;
        }

        using (WorldGenProbe.Measure(WorldGenStage.MapProps))
            context.Props = new PropPlacer(context.Config, context.PropSet, context.BiomeSet, context.Biomes, context.Heights,
                context.Water, context.Roads, context.Placements, context.Gameplay == null ? null : new UnityEngine.Vector2(context.Gameplay.Spawn.x, context.Gameplay.Spawn.z))
                .Place(context.Gameplay, context.Token);
    }

    private static void Gameplay(Context context)
    {
        if (context.Config.Gameplay == null || !context.Config.Gameplay.Enabled)
            return;

        using WorldGenProbe.Span gameplay = WorldGenProbe.Measure(WorldGenStage.MapGameplay);
        var roads = new RoadProximity(context.Roads.Paved(), context.Config.WorldSize, 64f);
        context.Gameplay = new GameplayLandmarkPlanner(context.Config, context.BiomeSet, context.Biomes,
            context.Heights.SampleWorldSmooth, context.Water, context.Placements, roads).Plan(cancellationToken: context.Token, preferred: context.Start);
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

        LakeBeds.Level(carved, water);
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
        var stampCache = new WaterStampCarver.CarveCache();

        for (int round = 1; ; round++)
        {
            WaterLevels levels = WaterLevels.Of(water, loose);
            HeightMap carved = CarveWater(heights, water, stamps, settings, loose, cache, stampCache);
            loose = RiverEnds.Loose(water);
            int changes = levels.Changes(water, loose);

            if (changes == 0)
            {
                if (round > 1)
                    UnityEngine.Debug.Log($"Water levels settled after {round} carving rounds, {cache.Reused} of {cache.Reused + cache.Computed} river tiles and {stampCache.Reused} of {stampCache.Reused + stampCache.Computed} stamp tiles reused");

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

    private static HeightMap CarveWater(HeightMap heights, WaterMap water, WaterStampLibrary stamps, WaterGenerationSettings settings, (bool Start, bool End)[] loose, RiverCarver.CarveCache cache, WaterStampCarver.CarveCache stampCache)
    {
        HeightMap carved;

        using (WorldGenProbe.Measure(WorldGenStage.MapCarveRivers))
            carved = RiverCarver.Carve(heights, water, settings.RiverBankWidth, loose, cache);

        if (stamps != null)
        {
            using (WorldGenProbe.Measure(WorldGenStage.MapWaterStampCarve))
                WaterStampCarver.Carve(carved, heights, water, stamps, stampCache);
        }

        LakeBeds.SmoothShores(carved, water);

        using (WorldGenProbe.Measure(WorldGenStage.MapWaterShore))
            WaterShore.Finish(water, carved);

        return carved;
    }
}

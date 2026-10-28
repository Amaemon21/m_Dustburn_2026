using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WorldGenerationService
{
    private const string ID_PREFIX = "world_";
    private const string ID_TIME_FORMAT = "yyyyMMdd_HHmmss";
    private const string DEFAULT_NAME = "Мир {0}";
    private const string PREPARE_STATUS = "Подготовка генератора";
    private const string PAINT_STATUS = "Раскраска земли";
    private const string PREVIEW_STATUS = "Карта мира";
    private const string PACK_STATUS = "Подготовка файлов мира";
    private const string WRITE_STATUS = "Запись мира на диск";
    private const float MAPS_SHARE = 0.75f;
    private const float MAPS_DONE = 0.53f;
    private const float PAINT_PROGRESS = 0.78f;
    private const float PREVIEW_PROGRESS = 0.86f;
    private const float PACK_PROGRESS = 0.9f;
    private const float WRITE_PROGRESS = 0.95f;
    private const double PUMP_MILLISECONDS = 8.0;

    private sealed class Status
    {
        private readonly object _gate = new();
        private WorldGenerationProgress _current = new(PREPARE_STATUS, 0f);

        public WorldGenerationProgress Current
        {
            get { lock (_gate) return _current; }
        }

        public void Set(string status, float fraction)
        {
            lock (_gate)
                _current = new WorldGenerationProgress(status, fraction);
        }
    }

    private sealed class Payload
    {
        public byte[] Heights;
        public byte[] Water;
        public byte[] BiomeMap;
        public byte[] RoadMask;
        public byte[][] Controls;
        public byte[] Preview;
        public string PlacementJson;
        public string Fingerprint;
    }

    public async UniTask<WorldInfo> GenerateAsync(WorldBuildSettings settings, WorldVariation variation, string name,
        IProgress<WorldGenerationProgress> progress, CancellationToken token)
    {
        GroundLayerSet ground = settings.Ground;
        if (ground == null || !ground.IsValid)
            throw new InvalidOperationException("The shipped ground layer set is missing; rebake the ground in the editor with «Мир → Перепечь землю мира»");

        WorldGenerationConfig config = Object.Instantiate(settings.Config);
        var host = new MainThreadJobHost(token);
        var status = new Status();
        CancellationTokenRegistration exit = Application.exitCancellationToken.Register(host.Stop);

        try
        {
            config.Apply(variation);
            WorldBuildBudget.Validate(settings, config, Center(config), true);
            StampBytes.Preload(config);
            var prefabs = new WorldPrefabIndex(settings.Pois, settings.Props);

            WorldMapResult maps;
            Payload payload;
            GenerationJobs.Attach(host);

            try
            {
                maps = await OnWorker(() => Maps(settings, config, status, token), host, status, progress);
                await Report(progress, PAINT_STATUS, PAINT_PROGRESS);

                var weights = new BiomeWeightField(maps.Biomes, settings.Biomes.Count, config.BiomeBlendRadius);
                using var painter = new GroundSplatPainter(config, settings.Biomes, weights, maps.Roads.Paved(), ground.Layers) { Water = maps.Water };
                payload = await OnWorker(() => Pack(settings, ground, config, maps, painter, prefabs, status, token), host, status, progress);
            }
            finally
            {
                GenerationJobs.Detach(host);
            }

            await Report(progress, WRITE_STATUS, WRITE_PROGRESS);
            string id = NewId();
            string worldName = string.IsNullOrWhiteSpace(name) ? string.Format(DEFAULT_NAME, variation.Seed) : name.Trim();
            WorldDiskContent content = Content(ground, config, maps, payload, id, worldName);
            string directory = Path.Combine(WorldCatalog.UserRoot, id);

            await UniTask.RunOnThreadPool(() => WorldDiskFormat.Write(directory, content), cancellationToken: token);
            return WorldInfo.FromManifest(content.Manifest, false, directory);
        }
        finally
        {
            exit.Dispose();
            host.Dispose();
            StampBytes.Clear();
            WorldData.Release(config);
        }
    }

    private static async UniTask<T> OnWorker<T>(Func<T> work, MainThreadJobHost host, Status status, IProgress<WorldGenerationProgress> progress)
    {
        Task<T> task = Task.Run(work);

        while (!task.IsCompleted)
        {
            host.Pump(PUMP_MILLISECONDS);
            progress?.Report(status.Current);
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        return await task;
    }

    private static WorldMapResult Maps(WorldBuildSettings settings, WorldGenerationConfig config, Status status, CancellationToken token)
    {
        var pipeline = new WorldMapPipeline(config, settings.Biomes, settings.Pois, settings.Props);

        foreach (WorldMapStep step in pipeline.Steps())
        {
            token.ThrowIfCancellationRequested();

            if (step.Result != null)
                return step.Result;

            status.Set(step.Title, step.Progress / MAPS_DONE * MAPS_SHARE);
        }

        throw new InvalidOperationException("The map pipeline ended without a result");
    }

    private static Payload Pack(WorldBuildSettings settings, GroundLayerSet ground, WorldGenerationConfig config, WorldMapResult maps,
        GroundSplatPainter painter, WorldPrefabIndex prefabs, Status status, CancellationToken token)
    {
        WorldBuildBudget.Validate(settings, config, Center(config), true, maps.Water);

        status.Set(PAINT_STATUS, PAINT_PROGRESS);
        Color32[][] controls = VoxelSplatBaker.BakePixels(config, painter, maps.Heights, ground.ControlResolution);
        token.ThrowIfCancellationRequested();

        status.Set(PREVIEW_STATUS, PREVIEW_PROGRESS);
        byte[] preview = WorldPreviewImage.Paint(settings.Biomes, maps.Biomes, maps.Heights, maps.Water, maps.Roads.Paved(), maps.Placements);
        token.ThrowIfCancellationRequested();

        status.Set(PACK_STATUS, PACK_PROGRESS);
        byte[] heights = maps.Heights.ToRaw16();
        byte[] water = WaterMapFormat.Write(maps.Water);
        var controlBytes = new byte[controls.Length][];

        for (int i = 0; i < controls.Length; i++)
        {
            controlBytes[i] = WorldPixels.Rgba(controls[i]);
            controls[i] = null;
        }

        return new Payload
        {
            Heights = heights,
            Water = water,
            BiomeMap = WorldPixels.Rgba(BiomeMapTexture.Pixels(maps.Biomes, settings.Biomes)),
            RoadMask = MaskTexture.ToGray(maps.RoadMask),
            Controls = controlBytes,
            Preview = preview,
            PlacementJson = WorldPlacementFile.Write(maps.Placements, maps.Props, prefabs),
            Fingerprint = WorldFingerprint.Of(BakedWorld.GENERATOR_VERSION, config.Seed, config.WorldSize, heights, water,
                maps.Placements.Count, maps.Props.Count)
        };
    }

    private static WorldDiskContent Content(GroundLayerSet ground, WorldGenerationConfig config, WorldMapResult maps, Payload payload,
        string id, string name)
    {
        var manifest = new WorldManifest
        {
            GeneratorVersion = BakedWorld.GENERATOR_VERSION,
            Id = id,
            Name = name,
            Fingerprint = payload.Fingerprint,
            CreatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Seed = config.Seed,
            WorldSize = config.WorldSize,
            HeightResolution = config.HeightMapResolution,
            BiomeResolution = maps.Biomes.Resolution,
            RoadMaskResolution = maps.Heights.Resolution,
            WaterLength = payload.Water.Length,
            ControlResolution = ground.ControlResolution,
            ControlCount = payload.Controls.Length,
            LayersCount = ground.Layers.Count,
            Placements = maps.Placements.Count,
            Props = maps.Props.Count
        };

        return new WorldDiskContent
        {
            Manifest = manifest,
            ConfigJson = WorldConfigJson.Write(config),
            Heights = payload.Heights,
            Water = payload.Water,
            BiomeMap = payload.BiomeMap,
            RoadMask = payload.RoadMask,
            RoadsJson = RoadsJson(maps.Roads),
            PlacementJson = payload.PlacementJson,
            Controls = payload.Controls,
            Preview = payload.Preview
        };
    }

    private static string RoadsJson(RoadNetwork network)
    {
        RoadNetworkAsset asset = RoadNetworkAsset.Create(network);

        try
        {
            return JsonUtility.ToJson(asset);
        }
        finally
        {
            WorldData.Release(asset);
        }
    }

    private static Vector2 Center(WorldGenerationConfig config) => Vector2.one * (config.WorldSize * 0.5f);

    private static string NewId()
    {
        string stem = ID_PREFIX + DateTime.Now.ToString(ID_TIME_FORMAT, CultureInfo.InvariantCulture);
        string id = stem;

        for (int suffix = 2; Directory.Exists(Path.Combine(WorldCatalog.UserRoot, id)); suffix++)
            id = $"{stem}_{suffix}";

        return id;
    }

    private static async UniTask Report(IProgress<WorldGenerationProgress> progress, string status, float fraction)
    {
        progress?.Report(new WorldGenerationProgress(status, fraction));
        await UniTask.Yield(PlayerLoopTiming.Update);
    }
}

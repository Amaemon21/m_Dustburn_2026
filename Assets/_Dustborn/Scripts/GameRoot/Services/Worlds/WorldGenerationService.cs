using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public sealed class WorldGenerationService
{
    private const string ID_PREFIX = "world_";
    private const string ID_TIME_FORMAT = "yyyyMMdd_HHmmss";
    private const string PREPARE_STATUS = "\u041F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0430 \u0433\u0435\u043D\u0435\u0440\u0430\u0442\u043E\u0440\u0430";
    private const string PAINT_STATUS = "\u041E\u043A\u0440\u0430\u0441\u043A\u0430 \u043F\u043E\u0432\u0435\u0440\u0445\u043D\u043E\u0441\u0442\u0438";
    private const string PREVIEW_STATUS = "\u0421\u043E\u0437\u0434\u0430\u043D\u0438\u0435 \u043F\u0440\u0435\u0432\u044C\u044E \u043A\u0430\u0440\u0442\u044B";
    private const string PACK_STATUS = "\u041F\u043E\u0434\u0433\u043E\u0442\u043E\u0432\u043A\u0430 \u0444\u0430\u0439\u043B\u043E\u0432 \u043C\u0438\u0440\u0430";
    internal const string WRITE_STATUS = "\u0421\u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435 \u043A\u0430\u0440\u0442\u044B";
    private const float MAPS_SHARE = 0.75f;
    private const float MAPS_DONE = 0.53f;
    private const float PAINT_PROGRESS = 0.78f;
    private const float PREVIEW_PROGRESS = 0.86f;
    private const float PACK_PROGRESS = 0.9f;
    private const float WRITE_PROGRESS = 0.95f;
    private const double PUMP_MILLISECONDS = 8.0;

    public static IReadOnlyList<string> StageTitles { get; } = Titles();

    public sealed class Draft
    {
        public WorldGenerationResult Preview { get; }
        internal WorldDiskContent Content { get; }

        internal Draft(WorldDiskContent content, WorldPreviewSource source, WorldPreviewLayers layers)
        {
            Content = content;
            Preview = new WorldGenerationResult(WorldInfo.FromManifest(content.Manifest, false, null), source, layers, content.Manifest.Stats);
        }
    }

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
        public WorldPreviewSource Source;
        public WorldPreviewLayers Layers;
        public WorldStats Stats;
    }

    public UniTask<WorldGenerationResult> GenerateAsync(WorldBuildSettings settings, WorldVariation variation, string name,
        WorldPreviewQuality quality, IProgress<WorldGenerationProgress> progress, CancellationToken token)
    {
        return GenerateAsync(settings, variation, NewId(), name, WorldCatalog.UserRoot, false, quality, progress, token);
    }

    public async UniTask<WorldGenerationResult> GenerateAsync(WorldBuildSettings settings, WorldVariation variation, string id, string name,
        string root, bool builtIn, WorldPreviewQuality quality, IProgress<WorldGenerationProgress> progress, CancellationToken token)
    {
        Draft draft = await PrepareAsync(settings, variation, name, quality, progress, token);
        await Report(progress, WRITE_STATUS, WRITE_PROGRESS);
        WorldInfo world = await SaveAsync(draft, id, name, root, builtIn, true, token);
        Debug.Log($"{nameof(WorldGenerationService)}: world '{world.Id}' generated in {draft.Preview.Stats.Seconds:0.0} s, seed {world.Seed}, size {world.WorldSize}");
        return new WorldGenerationResult(world, draft.Preview.Preview, draft.Preview.Layers, draft.Preview.Stats);
    }

    public async UniTask<Draft> PrepareAsync(WorldBuildSettings settings, WorldVariation variation, string name,
        WorldPreviewQuality quality, IProgress<WorldGenerationProgress> progress, CancellationToken token)
    {
        Stopwatch clock = Stopwatch.StartNew();
        GroundLayerSet ground = settings.Ground;
        if (ground == null || !ground.IsValid)
            throw new InvalidOperationException("The shipped ground layer set is missing; rebake the ground in the editor with «\u041C\u0438\u0440 → \u041F\u0435\u0440\u0435\u043F\u0435\u0447\u044C \u0437\u0435\u043C\u043B\u044E \u043C\u0438\u0440\u0430»");

        WorldGenerationConfig config = Object.Instantiate(settings.Config);
        var host = new MainThreadJobHost(token);
        var status = new Status();
        CancellationTokenRegistration exit = Application.isPlaying ? Application.exitCancellationToken.Register(host.Stop) : default;

        try
        {
            await Report(progress, PREPARE_STATUS, 0f);
            token.ThrowIfCancellationRequested();
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
                status.Set(PAINT_STATUS, PAINT_PROGRESS);
                await Report(progress, PAINT_STATUS, PAINT_PROGRESS);

                var weights = new BiomeWeightField(maps.Biomes, settings.Biomes.Count, config.BiomeBlendRadius);
                using var painter = new GroundSplatPainter(config, settings.Biomes, weights, maps.Roads.Paved(), ground.Layers) { Water = maps.Water };
                int resolution = WorldPreviewQualities.Resolution(quality);
                payload = await OnWorker(() => Pack(settings, ground, config, maps, painter, prefabs, resolution, status, token), host, status, progress);
            }
            finally
            {
                GenerationJobs.Detach(host);
            }

            string worldName = string.IsNullOrWhiteSpace(name) ? WorldNames.Of(variation.Seed) : name.Trim();
            payload.Stats.Seconds = (float)clock.Elapsed.TotalSeconds;
            WorldDiskContent content = Content(ground, config, maps, payload, variation, worldName);
            token.ThrowIfCancellationRequested();
            return new Draft(content, payload.Source, payload.Layers);
        }
        finally
        {
            exit.Dispose();
            host.Dispose();
            StampBytes.Clear();
            WorldData.Release(config);
        }
    }

    public UniTask<WorldInfo> SaveAsync(Draft draft, string name, CancellationToken token)
        => SaveAsync(draft, draft.Content.Manifest.Id ?? NewId(), name, WorldCatalog.UserRoot, false, false, token);

    private static async UniTask<WorldInfo> SaveAsync(Draft draft, string id, string name, string root, bool builtIn,
        bool replaceExisting, CancellationToken token)
    {
        WorldDiskContent content = draft.Content;
        content.Manifest.Id = id;
        content.Manifest.Name = string.IsNullOrWhiteSpace(name) ? draft.Preview.World.Name : name.Trim();
        content.Manifest.CreatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string directory = Path.Combine(root, id);
        await UniTask.RunOnThreadPool(() => WorldDiskFormat.Write(directory, content, replaceExisting), cancellationToken: token);
        return WorldInfo.FromManifest(content.Manifest, builtIn, directory);
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

        foreach (WorldMapStep step in pipeline.Steps(cancellationToken: token))
        {
            token.ThrowIfCancellationRequested();

            if (step.Result != null)
                return step.Result;

            status.Set(step.Title, step.Progress / MAPS_DONE * MAPS_SHARE);
        }

        throw new InvalidOperationException("The map pipeline ended without a result");
    }

    private static Payload Pack(WorldBuildSettings settings, GroundLayerSet ground, WorldGenerationConfig config, WorldMapResult maps,
        GroundSplatPainter painter, WorldPrefabIndex prefabs, int previewResolution, Status status, CancellationToken token)
    {
        WorldBuildBudget.Validate(settings, config, Center(config), true, maps.Water);

        Color32[][] controls = VoxelSplatBaker.BakePixels(config, painter, maps.Heights, ground.ControlResolution);
        token.ThrowIfCancellationRequested();

        status.Set(PREVIEW_STATUS, PREVIEW_PROGRESS);
        WorldPreviewSource source = WorldPreviewSource.Capture(settings.Biomes, maps.Biomes, maps.Heights, maps.Water, maps.Roads.Paved(),
            maps.Placements, maps.Settlements, maps.Gameplay);
        WorldPreviewLayers layers = WorldPreviewPainter.Layers(source, previewResolution);
        byte[] preview = WorldPreviewImage.Encode(layers);
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
            Source = source,
            Layers = layers,
            Stats = WorldStats.Of(maps, settings.Biomes, config.Stamps?.Database),
            PlacementJson = WorldPlacementFile.Write(maps.Placements, maps.Props, prefabs),
            Fingerprint = WorldFingerprint.Of(BakedWorld.GENERATOR_VERSION, config.Seed, config.WorldSize, heights, water,
                maps.Placements.Count, maps.Props.Count)
        };
    }

    private static WorldDiskContent Content(GroundLayerSet ground, WorldGenerationConfig config, WorldMapResult maps, Payload payload,
        WorldVariation variation, string name)
    {
        var manifest = new WorldManifest
        {
            GeneratorVersion = BakedWorld.GENERATOR_VERSION,
            Name = name,
            Fingerprint = payload.Fingerprint,
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
            Props = maps.Props.Count,
            VariationVersion = WorldVariation.VERSION,
            Variation = variation.Clone(),
            Stats = payload.Stats,
            PreviewResolution = payload.Layers.Resolution
        };

        manifest.FingerprintVersion = 2;
        manifest.GameplayVersion = maps.Gameplay?.Version ?? 0;
        var content = new WorldDiskContent
        {
            Manifest = manifest,
            ConfigJson = WorldConfigJson.Write(config),
            Heights = payload.Heights,
            Water = payload.Water,
            BiomeMap = payload.BiomeMap,
            RoadMask = payload.RoadMask,
            RoadsJson = RoadsJson(maps.Roads),
            PlacementJson = payload.PlacementJson,
            GameplayJson = maps.Gameplay == null ? null : JsonUtility.ToJson(maps.Gameplay),
            Controls = payload.Controls,
            Preview = payload.Preview
        };

        manifest.Fingerprint = WorldFingerprint.WithContent(payload.Fingerprint, content.ConfigJson, content.RoadsJson, content.PlacementJson, content.GameplayJson);
        return content;
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

    private static List<string> Titles()
    {
        var titles = new List<string> { PREPARE_STATUS };
        titles.AddRange(WorldMapPipeline.StageTitles);
        titles.AddRange(new[] { PAINT_STATUS, PREVIEW_STATUS, PACK_STATUS, WRITE_STATUS });
        return titles;
    }

    private static Vector2 Center(WorldGenerationConfig config) => Vector2.one * (config.WorldSize * 0.5f);

    private static string NewId()
    {
        string time = DateTime.Now.ToString(ID_TIME_FORMAT, CultureInfo.InvariantCulture);
        return $"{ID_PREFIX}{time}_{Guid.NewGuid():N}";
    }

    private static async UniTask Report(IProgress<WorldGenerationProgress> progress, string status, float fraction)
    {
        progress?.Report(new WorldGenerationProgress(status, fraction));
        await UniTask.Yield(PlayerLoopTiming.Update);
    }
}

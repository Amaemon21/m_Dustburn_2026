using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public static class GameplayGenerationChecks
{
    public static void Run(BiomeDatabase biomes)
    {
        var clock = Stopwatch.StartNew();
        var config = new WorldGenerationConfig();
        Set(config, nameof(config.WorldSize), 4096);
        Set(config, nameof(config.SeaLevel), 0f);
        var map = new BiomeMap(64, config.WorldSize);
        byte forest = (byte)Enumerable.Range(0, biomes.Count).First(i => biomes.Get(i).Type == BiomeType.PineForest);
        Array.Fill(map.Cells, forest);
        var pois = new List<PoiPlacement>();
        for (int i = 0; i < 8; i++)
        {
            var poi = new PoiPlacement(new GameObject { name = "Starter" + i }, new Vector2(1200f + i % 4 * 45f, 1300f + i / 4 * 60f),
                0f, new Vector2(12f, 12f), DistrictType.Residential, Vector2.zero);
            poi.SetGameplay(new PoiGameplay(), i + 10);
            pois.Add(poi);
        }

        for (int seed = 0; seed < 32; seed++)
        {
            Set(config, nameof(config.Seed), seed);
            WorldGameplayPlan first = Planner(config, biomes, map, pois).Plan();
            WorldGameplayPlan second = Planner(config, biomes, map, pois).Plan();
            Require(JsonUtility.ToJson(first) == JsonUtility.ToJson(second), "A seed must reproduce its start and zones.");
            Require(first.StarterPois.Length >= 2 && first.SpawnBiome == BiomeType.PineForest, "A start needs forest and reachable starter POI.");
            var threat = new WorldThreatQuery(first);
            Require(threat.At(new Vector2(first.Spawn.x, first.Spawn.z)) == 0, "The safe area must override POI threat.");
            foreach (WorldThreatZone zone in first.ThreatZones)
            {
                if ((zone.Center - new Vector2(first.Spawn.x, first.Spawn.z)).sqrMagnitude > first.SafeRadius * first.SafeRadius)
                    Require(threat.At(zone.Center) >= zone.Tier, "The threat index must preserve POI zones across cell boundaries.");
            }
            Require(!new PoiSpatialIndex(config.WorldSize, pois).Occupied(new Vector2(first.Spawn.x, first.Spawn.z), 12f), "The start must clear building footprints.");
            foreach (int index in first.StarterPois)
            {
                float square = (pois[index].Ground - new Vector2(first.Spawn.x, first.Spawn.z)).sqrMagnitude;
                Require(square >= 110f * 110f && square <= 300f * 300f, "Starter POI must respect the configured distances.");
            }
        }

        WorldGameplayPlan plan = Planner(config, biomes, map, pois).Plan();
        string json = JsonUtility.ToJson(plan);
        WorldGameplayPlan restored = JsonUtility.FromJson<WorldGameplayPlan>(json);
        restored.Validate(config.WorldSize, pois.Count);
        Require(JsonUtility.ToJson(restored) == json, "Gameplay data must survive serialization.");

        var source = new PoiGameplay { Tier = 4, Category = PoiCategory.Military, LootProfile = PoiLootProfile.Military,
            ThreatProfile = PoiThreatProfile.Elite, Biomes = new[] { BiomeType.Wasteland }, UniqueGroup = "military", MinDistanceFromSpawn = 700f };
        pois[0].SetGameplay(source, 9123);
        source.Biomes[0] = BiomeType.Desert;
        Require(pois[0].Gameplay.Biomes[0] == BiomeType.Wasteland && pois[0].ContentSeed == 9123, "POI snapshots must own their mutable data.");
        Require(!pois[0].Gameplay.Starter, "An elite landmark must never count as starter content.");
        WorldGameplayPlan distant = Planner(config, biomes, map, pois).Plan(false);
        Require((new Vector2(distant.Spawn.x, distant.Spawn.z) - pois[0].Ground).sqrMagnitude >= 700f * 700f, "Landmarks must respect their minimum spawn distance.");

        byte desert = (byte)Enumerable.Range(0, biomes.Count).First(i => biomes.Get(i).Type == BiomeType.Desert);
        Array.Fill(map.Cells, desert);
        Require(Planner(config, biomes, map, Array.Empty<PoiPlacement>()).Plan(false).SpawnBiome == BiomeType.Desert,
            "A map intentionally without forest must still have a dry start.");

        Set(config, nameof(config.SeaLevel), 200f);
        Reject(() => Planner(config, biomes, map, pois).Plan(false), "A flooded map must be rejected instead of spawning underwater.");
        Set(config, nameof(config.SeaLevel), 0f);
        Reject(() => Planner(config, biomes, map, Array.Empty<PoiPlacement>()).Plan(), "Missing starter buildings must reject an ordinary map.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            Planner(config, biomes, map, pois).Plan(cancellationToken: cancelled.Token);
            throw new Exception("Cancelled planning must stop.");
        }
        catch (OperationCanceledException) { }

        Fingerprints();
        Placements(config, pois, plan);
        PlacementRules(config, biomes, forest);
        foreach (SettlementTypeProfile profile in new[] { config.CityProfile, config.TownProfile, config.CountryTownProfile, config.GhostTownProfile })
            Set(profile, nameof(profile.Count), 0);
        Require(Planner(config, biomes, map, Array.Empty<PoiPlacement>()).Plan().StarterPois.Length == 0,
            "Turning settlements off must allow an intentionally uninhabited map.");
        Console.WriteLine($"PASS: gameplay planning over 32 seeds, dry forest start, footprint clearance, starter access, threat and distance limits, no-forest worlds, flooded-world rejection, cancellation, placement and disk round trips, content fingerprints ({clock.Elapsed.TotalMilliseconds:0} ms).");
    }

    private static GameplayLandmarkPlanner Planner(WorldGenerationConfig config, BiomeDatabase biomes, BiomeMap map, IReadOnlyList<PoiPlacement> pois)
        => new(config, biomes, map, (x, z) => 120f, null, pois, null);

    private static void Fingerprints()
    {
        foreach (int length in new[] { 0, 33, (1 << 20) + 17 })
        {
            byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i * 73)).ToArray();
            using var native = new NativeArray<byte>(data, GenerationJobs.ALLOCATOR);
            Require(GenerationChecksum.OfRawBytes(data) == GenerationChecksum.Of(native), "Managed hashing must preserve legacy fingerprints across block boundaries.");
        }

        string first = WorldFingerprint.WithContent("legacy", "roads-a", "placements-a", "plan-a");
        Require(first != WorldFingerprint.WithContent("legacy", "roads-b", "placements-a", "plan-a"), "Road changes must change the fingerprint.");
        Require(first != WorldFingerprint.WithContent("legacy", "roads-a", "placements-b", "plan-a"), "POI changes must change the fingerprint even with equal counts.");
        Require(first != WorldFingerprint.WithContent("legacy", "roads-a", "placements-a", "plan-b"), "Gameplay changes must change the fingerprint.");
        Require(WorldFingerprint.WithContent("legacy", "ab", "c") != WorldFingerprint.WithContent("legacy", "a", "bc"), "Content boundaries must be hashed.");
    }

    private static void Placements(WorldGenerationConfig config, List<PoiPlacement> pois, WorldGameplayPlan plan)
    {
        var definitions = new List<PoiDefinition>();
        foreach (PoiPlacement poi in pois)
        {
            var definition = new PoiDefinition();
            definition.EditorSetup(poi.Prefab, poi.District, poi.Footprint.x, poi.Footprint.y, 1f);
            definitions.Add(definition);
        }

        var database = new PoiDatabase();
        database.GetType().GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(database, definitions);
        var prefabs = new WorldPrefabIndex(database, null);
        string placementJson = WorldPlacementFile.Write(pois, Array.Empty<PropPlacement>(), prefabs);
        PoiPlacementAsset loaded = WorldPlacementFile.Read(placementJson, prefabs, plan);
        Require(WorldPlacementFile.Write(loaded.Placements, loaded.Props, prefabs) == placementJson, "POI metadata and content seeds must survive the actual disk record format.");
        Require(loaded.Placements[0].Gameplay.UniqueGroup == "military" && loaded.Placements[0].ContentSeed == 9123, "Gameplay profiles must survive placement loading.");

        RoadNetworkAsset roads = RoadNetworkAsset.Create(new RoadNetwork());
        string layoutHash = WorldFingerprint.WithLayout("base", roads, loaded);
        loaded.Placements[0].SetHeight(loaded.Placements[0].Position.y + 1f);
        Require(layoutHash != WorldFingerprint.WithLayout("base", roads, loaded), "Baked worlds must fingerprint actual POI transforms.");

        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dustborn-gameplay-check-" + Guid.NewGuid().ToString("N")));
        string directory = Path.Combine(root, "world");
        Require(root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase), "Test files must stay inside the temporary directory.");
        Directory.CreateDirectory(root);
        try
        {
            var content = new WorldDiskContent
            {
                Manifest = new WorldManifest { Id = "fixture", GeneratorVersion = BakedWorld.GENERATOR_VERSION, WorldSize = config.WorldSize,
                    HeightResolution = 2, BiomeResolution = 1, RoadMaskResolution = 1, WaterLength = 1, ControlResolution = 1, ControlCount = 1,
                    GameplayVersion = WorldGameplayPlan.CURRENT_VERSION, FingerprintVersion = 2, Placements = pois.Count },
                ConfigJson = "{}", RoadsJson = "{}", PlacementJson = placementJson, GameplayJson = JsonUtility.ToJson(plan),
                Heights = new byte[8], Water = new byte[1], BiomeMap = new byte[4], RoadMask = new byte[1], Controls = new[] { new byte[4] }
            };
            WorldDiskFormat.Write(directory, content, replaceExisting: false);
            WorldDiskContent read = WorldDiskFormat.Read(directory);
            Require(read.GameplayJson == content.GameplayJson && read.PlacementJson == placementJson, "The world folder must preserve its gameplay file.");
            content.Manifest.Name = "Replacement";
            bool duplicateRejected = false;
            try { WorldDiskFormat.Write(directory, content, replaceExisting: false); }
            catch (IOException) { duplicateRejected = true; }
            Require(duplicateRejected && WorldDiskFormat.ReadManifest(directory).Name == read.Manifest.Name,
                "Saving a generated map must preserve an existing world directory.");
            File.Delete(Path.Combine(directory, "gameplay.json"));
            bool missing = false;
            try { WorldDiskFormat.Read(directory); }
            catch (FileNotFoundException) { missing = true; }
            Require(missing, "A new world must not silently lose its gameplay data.");
            content.Manifest.GameplayVersion = 0;
            content.Manifest.FingerprintVersion = 0;
            WorldDiskFormat.Write(directory, content);
            Require(WorldDiskFormat.Read(directory).GameplayJson == null, "Legacy worlds must load without a gameplay file.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void PlacementRules(WorldGenerationConfig config, BiomeDatabase biomes, byte forest)
    {
        var heights = new HeightMap(65, config.WorldSize, config.MaxHeight);
        Array.Fill(heights.Heights, 120f / config.MaxHeight);
        var map = new BiomeMap(32, config.WorldSize);
        Array.Fill(map.Cells, forest);
        var layout = new SettlementLayout(new Hub(new Vector2(1300f, 1300f), 300f, 0f, 0, SettlementType.CountryTown), 0, 0f, 100f);
        for (int i = 0; i < 4; i++)
            layout.Lots.Add(new Lot(new Vector2(1200f + i * 50f, 1300f), 30f, 30f, new Vector2(0f, 1f), DistrictType.Residential));

        var first = new PoiDefinition();
        first.EditorSetup(new GameObject { name = "rare-a" }, DistrictType.Residential, 12f, 12f, 1f);
        var second = new PoiDefinition();
        second.EditorSetup(new GameObject { name = "rare-b" }, DistrictType.Residential, 12f, 12f, 1f);
        first.Gameplay.UniqueGroup = "shared-landmark";
        second.Gameplay.UniqueGroup = "shared-landmark";
        var database = new PoiDatabase();
        database.GetType().GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(database, new List<PoiDefinition> { first, second });
        var proximity = new RoadProximity(config.WorldSize, 64f);
        var layouts = new[] { layout };
        List<PoiPlacement> Place(Vector2? start = null) => new PoiPlacer(config, database, heights, null, proximity, biomes, map, start).Place(layouts, new RoadNetwork());
        Require(Place().Count == 1, "Different prefabs in one unique group must share a world cap.");
        first.Gameplay.Biomes = second.Gameplay.Biomes = new[] { BiomeType.Desert };
        Require(Place().Count == 0, "POI biome restrictions must affect actual placement.");
        first.Gameplay.Biomes = second.Gameplay.Biomes = Array.Empty<BiomeType>();
        first.Gameplay.MinDistanceFromSpawn = second.Gameplay.MinDistanceFromSpawn = 700f;
        Require(Place(new Vector2(1250f, 1200f)).Count == 0, "POI minimum distance must affect actual placement.");
        first.Gameplay.MinDistanceFromSpawn = second.Gameplay.MinDistanceFromSpawn = 0f;
        first.Gameplay.RequiredConnection = second.Gameplay.RequiredConnection = PoiConnection.Road;
        Require(Place().Count == 0, "A road-required POI must not appear without a road.");

        var filter = new DecorFilter(config, new BiomeWeightField(map, biomes.Count, config.BiomeBlendRadius), biomes.Count,
            null, 0, Array.Empty<PoiPlacement>(), 4f) { Start = new Vector2(1000f, 1000f) };
        Require(!filter.IsClear(new Vector2(1000f, 1000f), 2f, 0f) && filter.IsClear(new Vector2(1020f, 1000f), 2f, 0f),
            "The spawn must exclude nearby decor without clearing the surrounding resource area.");
    }

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField($"<{name}>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(target, value);

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

public static class BakedWorld
{
    public const int GENERATOR_VERSION = 5;
}

namespace UnityEngine
{
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions OPTIONS = new() { IncludeFields = true, IgnoreReadOnlyProperties = true };
        public static string ToJson(object value, bool pretty = false) => JsonSerializer.Serialize(value, OPTIONS);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, OPTIONS);
    }
}

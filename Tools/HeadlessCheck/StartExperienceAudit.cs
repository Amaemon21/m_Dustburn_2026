using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class StartExperienceAudit
{
    private const float WOOD_RADIUS = 100f;

    public static int Run(string configPath, BiomeDatabase biomes, PoiDatabase pois, string seedList, string heightCell)
    {
        int[] seeds = string.IsNullOrWhiteSpace(seedList)
            ? Enumerable.Range(0, 24).Select(i => unchecked(1337 + i * 104729)).ToArray()
            : seedList.Split(',').Select(int.Parse).ToArray();
        int failed = 0;
        string worldFolder = Path.GetDirectoryName(configPath);
        string content = Path.GetFullPath(Path.Combine(worldFolder, "../.."));
        HashSet<GameObject> harvestable = AssetReader.LoadStarterDecor(biomes, Path.Combine(worldFolder, "Biomes"), content);
        var voxels = AssetReader.Load<VoxelConfig>(Path.Combine(worldFolder, "VoxelConfig.asset"));
        PropDatabase props = AssetReader.LoadProps(Path.Combine(worldFolder, "PropDatabase.asset"), content);
        var prefabs = new WorldPrefabIndex(pois, props);

        foreach (int seed in seeds)
        {
            var clock = Stopwatch.StartNew();
            var config = AssetReader.Load<WorldGenerationConfig>(configPath);
            StampLoader.Attach(config);
            config.Apply(new WorldVariation { WorldSize = 4096, Seed = seed });
            if (heightCell != null)
                StampLoader.Set(config, nameof(config.HeightCellSize), int.Parse(heightCell));
            string stage = "setup";

            try
            {
                WorldMapResult world = new WorldMapPipeline(config, biomes, pois, props).Generate((name, _) => stage = name);
                WorldGameplayPlan plan = world.Gameplay;
                if (plan?.HasSpawn != true || plan.StarterPois.Length < config.Gameplay.StarterPoiCount)
                    throw new InvalidOperationException("Missing spawn or starter buildings.");

                var start = new Vector2(plan.Spawn.x, plan.Spawn.z);
                var buildings = new PoiSpatialIndex(config.WorldSize, world.Placements);
                int reachable = 0;
                float nearest = float.PositiveInfinity;
                foreach (int index in plan.StarterPois)
                {
                    PoiPlacement poi = world.Placements[index];
                    Vector2 approach = Approach(poi);
                    nearest = Mathf.Min(nearest, (approach - start).sqrMagnitude);
                    if (poi.Gameplay.Starter && poi.Gameplay.LootProfile != PoiLootProfile.None
                        && Walk(config, world, buildings, start, approach)
                        && Walk(config, world, buildings, approach, poi.Ground, index))
                        reachable++;
                }

                if (reachable == 0)
                    throw new InvalidOperationException("No starter POI has a dry route with the configured walking grade at 2 m intervals.");

                PoiPlacement first = world.Placements[plan.StarterPois[0]];
                Vector2 firstApproach = Approach(first);
                Vector2 facing = firstApproach - start;
                float bearing = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
                if (facing.sqrMagnitude > nearest + 0.01f || Mathf.Abs(bearing - plan.Facing) > 0.01f)
                    throw new InvalidOperationException("Spawn does not face the nearest verified starter approach.");
                if (!Walk(config, world, buildings, start, firstApproach)
                    || !Walk(config, world, buildings, firstApproach, first.Ground, plan.StarterPois[0]))
                    throw new InvalidOperationException("The approach the player faces is obstructed.");

                float wood = NearestWood(config, biomes, voxels, world, buildings, start, harvestable);
                if (wood > WOOD_RADIUS)
                    throw new InvalidOperationException($"No reachable wood within {WOOD_RADIUS:0} m at ({start.x:0}, {start.y:0}).");

                string placements = WorldPlacementFile.Write(world.Placements, world.Props, prefabs);
                PoiPlacementAsset restored = WorldPlacementFile.Read(placements, prefabs, plan);
                PropPlacement supply = restored.Props.Single(prop => prop.Context == PropContext.Starter);
                var supplyPoint = new Vector2(supply.Position.x, supply.Position.z);
                if ((supplyPoint - firstApproach).sqrMagnitude > 40f * 40f
                    || !Walk(config, world, buildings, start, supplyPoint))
                    throw new InvalidOperationException("The saved starter supplies are not accessible near the first POI.");

                Console.WriteLine($"{seed}: {clock.Elapsed.TotalSeconds:0.0}s, {plan.SpawnBiome} ({start.x:0}, {start.y:0}), "
                    + $"{reachable}/{plan.StarterPois.Length} starter routes, facing {first.Prefab.name} at {Mathf.Sqrt(nearest):0} m, "
                    + $"wood {wood:0} m, supplies {(supplyPoint - start).magnitude:0} m");
            }
            catch (InvalidOperationException error)
            {
                failed++;
                Console.WriteLine($"{seed}: FAIL after {clock.Elapsed.TotalSeconds:0.0}s at {stage}: {error.Message}");
            }
        }

        Console.WriteLine($"Start audit: {seeds.Length - failed}/{seeds.Length} worlds passed.");
        return failed == 0 ? 0 : 1;
    }

    private static float NearestWood(WorldGenerationConfig config, BiomeDatabase biomes, VoxelConfig voxels,
        WorldMapResult world, PoiSpatialIndex buildings, Vector2 start, HashSet<GameObject> harvestable)
    {
        using var field = new VoxelDensityField(world.Heights, voxels, world.Heights.Heights.Min() * world.Heights.MaxHeight);
        var weights = new BiomeWeightField(world.Biomes, biomes.Count, config.BiomeBlendRadius);
        float footprint = 0f;
        for (int i = 0; i < biomes.Count; i++)
        {
            foreach (ScatterLayer tree in biomes.Get(i).Trees)
                footprint = Mathf.Max(footprint, tree.EffectiveFootprint);
            foreach (ScatterLayer rock in biomes.Get(i).Rocks)
                footprint = Mathf.Max(footprint, rock.Footprint);
        }

        int roadResolution = world.RoadMask == null ? 0 : (int)Math.Sqrt(world.RoadMask.Length);
        var filter = new DecorFilter(config, weights, biomes.Count, world.RoadMask, roadResolution, world.Placements, footprint)
        {
            Water = world.Water,
            Start = start
        };
        var placer = new VoxelDecorPlacer(config, biomes, field, filter);
        var candidates = new List<DecorInstance>();
        Vector2 origin = start - new Vector2(WOOD_RADIUS, WOOD_RADIUS);
        float best = float.PositiveInfinity;

        for (int layer = 0; layer < placer.Layers.Count; layer++)
        {
            VoxelDecorLayer decor = placer.Layers[layer];
            if (decor.Kind != DecorKind.Tree || !harvestable.Contains(decor.Prefab))
                continue;

            candidates.Clear();
            placer.Place(layer, origin, WOOD_RADIUS * 2f, candidates);
            foreach (DecorInstance instance in candidates)
            {
                var target = new Vector2(instance.Position.x, instance.Position.z);
                float square = (target - start).sqrMagnitude;
                if (square < best && square <= WOOD_RADIUS * WOOD_RADIUS && Walk(config, world, buildings, start, target))
                    best = square;
            }
        }

        return Mathf.Sqrt(best);
    }

    private static Vector2 Approach(PoiPlacement poi) => poi.Ground + poi.Forward * (poi.Footprint.y * 0.5f + 2f);

    private static bool Walk(WorldGenerationConfig config, WorldMapResult world, PoiSpatialIndex buildings, Vector2 from, Vector2 to, int ignore = -1)
    {
        float length = (to - from).magnitude;
        int steps = Math.Max(1, Mathf.CeilToInt(length / 2f));
        float rise = Mathf.Tan(config.Gameplay.MaxWalkSlope * Mathf.Deg2Rad) * length / steps + 0.05f;
        float previous = world.Heights.SampleWorldSmooth(from.x, from.y);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 sample = Vector2.Lerp(from, to, i / (float)steps);
            float height = world.Heights.SampleWorldSmooth(sample.x, sample.y);
            if (Mathf.Abs(height - previous) > rise || world.Water?.IsWet(sample.x, sample.y, height, config.ShoreMargin) == true
                || buildings.Occupied(sample, 1f, ignore))
                return false;
            previous = height;
        }

        return true;
    }
}

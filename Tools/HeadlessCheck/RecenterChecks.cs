using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

static class RecenterChecks
{
    private const string CONTENT = "../../Assets/_Dustborn/Content/Configs/World";
    private const string GENERATED = "../../Assets/_Dustborn/Generated";
    private const float RECENTER_SHARE = 0.5f;
    private const int STEPS = 48;

    public static void Run(string[] args)
    {
        var config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        var settings = AssetReader.Load<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset");
        var voxels = AssetReader.Load<VoxelConfig>($"{CONTENT}/VoxelConfig.asset");
        HeightMap map = HeightMap.FromRaw16(File.ReadAllBytes($"{GENERATED}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        WaterMap water = WaterMapFormat.Read(File.ReadAllBytes($"{GENERATED}/WaterMap.bytes"));
        using var field = new VoxelDensityField(map, voxels, 0f);
        List<Vector3> samples = VoxelShorePlan.Samples(water);

        float step = settings.NearDistance * RECENTER_SHARE;
        var start = new Vector2(1024f, 1024f);
        Vector2 direction = new Vector2(1f, 0.6f).normalized;
        List<VoxelColumnKey> previous = Plan(field, voxels, settings, config, water, samples, start, out _);
        var built = new List<int>();
        var retired = new List<int>();
        var times = new List<double>();
        int broken = 0;

        for (int i = 1; i <= STEPS; i++)
        {
            Vector2 viewer = start + direction * (step * 1.01f * i);
            List<VoxelColumnKey> current = Plan(field, voxels, settings, config, water, samples, viewer, out double seconds);
            times.Add(seconds);

            var before = new HashSet<VoxelColumnKey>(previous);
            var after = new HashSet<VoxelColumnKey>(current);
            CheckSwapGroups(current.Where(key => !before.Contains(key)).ToList(), previous.Where(key => !after.Contains(key)).ToList());
            built.Add(current.Count(key => !before.Contains(key)));
            retired.Add(previous.Count(key => !after.Contains(key)));

            if (!Partitions(current, config.WorldSize, voxels.ChunkMetres, out string problem))
            {
                broken++;
                Console.WriteLine($"  шаг {i} у ({viewer.x:0}, {viewer.y:0}): {problem}");
            }

            if (LodUnder(current, viewer, voxels.ChunkMetres) != 0)
            {
                broken++;
                Console.WriteLine($"  шаг {i} у ({viewer.x:0}, {viewer.y:0}): под игроком не самое мелкое кольцо");
            }

            previous = current;
        }

        Console.WriteLine($"рецентрирование: {STEPS} шагов по {step:0} м, столбцов строится в среднем {built.Average():0}, максимум {built.Max()}, "
            + $"убирается в среднем {retired.Average():0}; план с уточнением у воды {times.Average():0.00} с в среднем, {times.Max():0.00} с максимум");

        if (broken > 0)
            throw new InvalidOperationException($"{broken} recentered plans do not cover the world exactly once.");

        Console.WriteLine("PASS: every recentered plan covers the world exactly once with the finest ring under the viewer.");
    }

    private static void CheckSwapGroups(List<VoxelColumnKey> added, List<VoxelColumnKey> removed)
    {
        foreach (VoxelColumnSwap swap in VoxelColumnSwap.Plan(added, removed))
        {
            var balance = new Dictionary<Vector2Int, int>();
            CountCells(swap.Added, balance, 1);
            CountCells(swap.Removed, balance, -1);

            if (balance.Values.Any(value => value != 0))
                throw new InvalidOperationException("A terrain swap group changes coverage before all its replacements are active.");
        }
    }

    private static void CountCells(List<VoxelColumnKey> columns, Dictionary<Vector2Int, int> balance, int delta)
    {
        foreach (VoxelColumnKey key in columns)
        {
            int scale = 1 << key.Lod;
            for (int z = key.Z * scale; z < (key.Z + 1) * scale; z++)
            {
                for (int x = key.X * scale; x < (key.X + 1) * scale; x++)
                {
                    var cell = new Vector2Int(x, z);
                    balance.TryGetValue(cell, out int count);
                    balance[cell] = count + delta;
                }
            }
        }
    }

    private static List<VoxelColumnKey> Plan(VoxelDensityField field, VoxelConfig voxels, WorldBuildSettings settings, WorldGenerationConfig config,
        WaterMap water, List<Vector3> samples, Vector2 viewer, out double seconds)
    {
        var clock = Stopwatch.StartNew();
        var plan = new VoxelStreamPlan(voxels, settings.LodCount, settings.NearDistance, config.WorldSize, water);
        VoxelShorePlan.Refine(field.Sampler, plan, viewer, voxels.ChunkSize, samples);
        var keys = new List<VoxelColumnKey>();
        plan.Around(viewer, keys);
        seconds = clock.Elapsed.TotalSeconds;
        return keys;
    }

    private static bool Partitions(List<VoxelColumnKey> keys, float worldSize, float cell, out string problem)
    {
        int resolution = Mathf.CeilToInt(worldSize / cell);
        var cover = new int[resolution * resolution];

        foreach (VoxelColumnKey key in keys)
        {
            int span = 1 << key.Lod;

            for (int z = key.Z * span; z < (key.Z + 1) * span; z++)
                for (int x = key.X * span; x < (key.X + 1) * span; x++)
                    if (x >= 0 && z >= 0 && x < resolution && z < resolution)
                        cover[z * resolution + x]++;
        }

        int holes = cover.Count(count => count == 0);
        int overlaps = cover.Count(count => count > 1);
        problem = $"дыр {holes}, нахлёстов {overlaps} клеток по {cell:0} м";
        return holes == 0 && overlaps == 0;
    }

    private static int LodUnder(List<VoxelColumnKey> keys, Vector2 viewer, float cell)
    {
        int x = Mathf.FloorToInt(viewer.x / cell);
        int z = Mathf.FloorToInt(viewer.y / cell);

        foreach (VoxelColumnKey key in keys)
        {
            int span = 1 << key.Lod;
            if (x >= key.X * span && x < (key.X + 1) * span && z >= key.Z * span && z < (key.Z + 1) * span)
                return key.Lod;
        }

        return -1;
    }
}

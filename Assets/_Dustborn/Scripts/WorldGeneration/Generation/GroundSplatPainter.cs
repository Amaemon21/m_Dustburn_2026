using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class GroundSplatPainter : IDisposable
{
    private const int BATCH_SIZE = 64;

    private readonly WorldGenerationConfig _config;

    private readonly TerrainLayer[] _layers;
    private readonly int _cliffLayerIndex;
    private readonly int _roadLayerIndex;

    private readonly int _roadMaskResolution;
    private readonly int _weightResolution;
    private readonly int _biomeCount;

    private NativeArray<float> _weights;
    private NativeArray<GroundRule> _rules;
    private NativeArray<int> _ruleStart;
    private NativeArray<int> _ruleCount;
    private NativeArray<float> _roadMask;

    public TerrainLayer[] Layers => _layers;

    public bool HasLayers => _layers.Length > 0;

    public GroundSplatPainter(WorldGenerationConfig config, BiomeDatabase biomes, BiomeWeightField weightField, float[] roadMask, int roadMaskResolution)
    {
        _config = config;
        _roadMaskResolution = roadMask != null ? roadMaskResolution : 0;
        _weightResolution = weightField.Resolution;
        _biomeCount = biomes.Count;

        var layers = new List<TerrainLayer>();

        _cliffLayerIndex = Register(layers, config.CliffLayer);
        _roadLayerIndex = _roadMaskResolution > 0 ? Register(layers, config.RoadLayer) : -1;

        var rules = new List<GroundRule>();
        var starts = new int[biomes.Count];
        var counts = new int[biomes.Count];

        BuildRules(layers, biomes, weightField, rules, starts, counts);

        _layers = layers.ToArray();

        _weights = weightField.ToNativeArray(Allocator.Persistent);
        _rules = new NativeArray<GroundRule>(rules.ToArray(), Allocator.Persistent);
        _ruleStart = new NativeArray<int>(starts, Allocator.Persistent);
        _ruleCount = new NativeArray<int>(counts, Allocator.Persistent);
        _roadMask = NativeBuffer.From(roadMask);
    }

    public NativeArray<float> BakeWorld(int resolution, float[] steepness, float[] height)
    {
        int cells = resolution * resolution;

        var slope = new NativeArray<float>(steepness, Allocator.TempJob);
        var elevation = new NativeArray<float>(height, Allocator.TempJob);
        var alphamaps = new NativeArray<float>(cells * _layers.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        try
        {
            var job = new GroundSplatJob
            {
                BiomeWeights = _weights,
                Rules = _rules,
                RuleStart = _ruleStart,
                RuleCount = _ruleCount,
                RoadMask = _roadMask,
                Steepness = slope,
                Height = elevation,
                Alphamaps = alphamaps,
                Resolution = resolution,
                WeightResolution = _weightResolution,
                BiomeCount = _biomeCount,
                LayerCount = _layers.Length,
                RoadMaskResolution = _roadMaskResolution,
                CliffLayer = _cliffLayerIndex,
                RoadLayer = _roadLayerIndex,
                CliffSlopeStart = _config.CliffSlopeStart,
                CliffSlopeFull = _config.CliffSlopeFull,
                TileSize = _config.WorldSize,
                WorldSize = _config.WorldSize,
                OriginX = 0f,
                OriginZ = 0f,
                NoiseSeed = _config.Seed & 0xFFFF
            };

            job.Schedule(cells, BATCH_SIZE).Complete();

            return alphamaps;
        }
        catch
        {
            alphamaps.Dispose();
            throw;
        }
        finally
        {
            slope.Dispose();
            elevation.Dispose();
        }
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref _weights);
        NativeBuffer.Release(ref _rules);
        NativeBuffer.Release(ref _ruleStart);
        NativeBuffer.Release(ref _ruleCount);
        NativeBuffer.Release(ref _roadMask);
    }

    private void BuildRules(List<TerrainLayer> layers, BiomeDatabase biomes, BiomeWeightField weightField,
        List<GroundRule> rules, int[] starts, int[] counts)
    {
        var order = new List<int>(biomes.Count);

        for (int i = 0; i < biomes.Count; i++)
            order.Add(i);

        order.Sort((left, right) => weightField.Coverage(right).CompareTo(weightField.Coverage(left)));

        var dropped = new StringBuilder();
        var buckets = new List<GroundRule>[biomes.Count];

        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<GroundRule>();

        foreach (int biome in order)
        {
            BiomeDefinition definition = biomes.Get(biome);

            if (definition.Ground.Count == 0)
            {
                Debug.LogWarning($"Biome {definition.Type} has no ground assigned, neighbouring layers will paint over its area");
                continue;
            }

            foreach (GroundLayer ground in definition.Ground)
            {
                if (ground == null || !ground.IsValid)
                    continue;

                if (ground.IsMute)
                {
                    Debug.LogWarning($"Biome {definition.name}: ground {ground.Layer.name} will never appear — Weight {ground.Weight}, slope {ground.MinSlope}..{ground.MaxSlope}, height {ground.MinHeight}..{ground.MaxHeight}, PatchThreshold {ground.PatchThreshold}");
                    continue;
                }

                int layer = layers.IndexOf(ground.Layer);

                if (layer < 0 && layers.Count >= _config.MaxTerrainLayers)
                {
                    if (dropped.Length > 0)
                        dropped.Append(", ");

                    dropped.Append($"{ground.Layer.name} of {definition.Type}");
                    continue;
                }

                if (layer < 0)
                    layer = Register(layers, ground.Layer);

                buckets[biome].Add(ToRule(ground, layer));
            }
        }

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            starts[biome] = rules.Count;
            rules.AddRange(buckets[biome]);
            counts[biome] = rules.Count - starts[biome];
        }

        if (dropped.Length > 0)
            Debug.LogWarning($"The budget of {_config.MaxTerrainLayers} layers is spent, these did not fit: {dropped}. Their weight spreads over the remaining grounds of the same biome during normalisation. Raise MaxTerrainLayers, Repetitionless Pro holds up to 32");
    }

    private static GroundRule ToRule(GroundLayer ground, int layer)
    {
        return new GroundRule
        {
            Layer = layer,
            Weight = ground.Weight,
            PatchFrequency = ground.PatchFrequency,
            PatchThreshold = ground.PatchThreshold,
            PatchFade = ground.PatchFade,
            MinSlope = ground.MinSlope,
            MaxSlope = ground.MaxSlope,
            SlopeFade = ground.SlopeFade,
            MinHeight = ground.MinHeight,
            MaxHeight = ground.MaxHeight,
            HeightFade = ground.HeightFade
        };
    }

    private static int Register(List<TerrainLayer> layers, TerrainLayer layer)
    {
        if (layer == null)
            return -1;

        int existing = layers.IndexOf(layer);

        if (existing >= 0)
            return existing;

        layers.Add(layer);

        return layers.Count - 1;
    }
}

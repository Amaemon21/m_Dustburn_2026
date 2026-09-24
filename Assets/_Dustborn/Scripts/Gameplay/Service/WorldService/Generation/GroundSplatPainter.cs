using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class GroundSplatPainter : IDisposable
{
    private const int BATCH_SIZE = 64;
    private const int PATCH_STREAM = 1000;
    private const int PATCH_STREAMS_PER_BIOME = 64;

    private readonly WorldGenerationConfig _config;
    private readonly IReadOnlyList<Road> _roads;

    private readonly TerrainLayer[] _layers;
    private readonly int _cliffLayerIndex;
    private readonly int _roadLayerIndex;
    private readonly int _roadEdgeLayerIndex;

    private readonly int _weightResolution;
    private readonly int _biomeCount;

    private NativeArray<float> _weights;
    private NativeArray<GroundRule> _rules;
    private NativeArray<int> _ruleStart;
    private NativeArray<int> _ruleCount;
    private NativeArray<int> _biomeCliffs;
    private NativeArray<int> _biomeShores;

    private RoadPaintIndex _paint;
    private float _paintTexel;

    public TerrainLayer[] Layers => _layers;

    public bool HasLayers => _layers.Length > 0;

    public int RoadLayerIndex => _roadLayerIndex;

    public int RoadEdgeLayerIndex => _roadEdgeLayerIndex;

    public GroundSplatPainter(WorldGenerationConfig config, BiomeDatabase biomes, BiomeWeightField weightField, IReadOnlyList<Road> roads)
    {
        _config = config;
        _roads = roads;
        _weightResolution = weightField.Resolution;
        _biomeCount = biomes.Count;

        var layers = new List<TerrainLayer>();

        _cliffLayerIndex = Register(layers, config.CliffLayer);
        _roadLayerIndex = RoadPaintIndex.HasRoads(roads) ? Register(layers, config.RoadLayer) : -1;
        _roadEdgeLayerIndex = _roadLayerIndex >= 0 ? Register(layers, config.RoadEdgeLayer) : -1;

        var rules = new List<GroundRule>();
        var starts = new int[biomes.Count];
        var counts = new int[biomes.Count];

        BuildRules(layers, biomes, weightField, rules, starts, counts);

        var cliffs = new int[biomes.Count];

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            TerrainLayer cliff = biomes.Get(biome).CliffLayer;
            int registered = cliff == null ? -1 : layers.IndexOf(cliff);

            if (registered < 0 && cliff != null && layers.Count < _config.MaxTerrainLayers)
                registered = Register(layers, cliff);

            cliffs[biome] = registered >= 0 ? registered : _cliffLayerIndex;
        }

        var shores = new int[biomes.Count];

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            TerrainLayer shore = biomes.Get(biome).ShoreLayer;
            int registered = shore == null ? -1 : layers.IndexOf(shore);

            if (registered < 0 && shore != null && layers.Count < _config.MaxTerrainLayers)
                registered = Register(layers, shore);

            shores[biome] = registered;
        }

        _layers = layers.ToArray();

        _weights = weightField.ToNativeArray(Allocator.Persistent);
        _rules = new NativeArray<GroundRule>(rules.ToArray(), Allocator.Persistent);
        _ruleStart = new NativeArray<int>(starts, Allocator.Persistent);
        _ruleCount = new NativeArray<int>(counts, Allocator.Persistent);
        _biomeCliffs = new NativeArray<int>(cliffs, Allocator.Persistent);
        _biomeShores = new NativeArray<int>(shores, Allocator.Persistent);
    }

    public NativeArray<float> BakeWorld(int resolution, float[] steepness, float[] height, float[] relief = null)
    {
        return BakeTile(resolution, resolution, 0, 0, steepness, height, relief);
    }

    public NativeArray<float> BakeTile(int resolution, int tileResolution, int tileX, int tileY, float[] steepness, float[] height,
        float[] relief = null)
    {
        int cells = tileResolution * tileResolution;

        float texel = (float)_config.WorldSize / resolution;
        RoadPaintIndex paint = PaintFor(texel);

        var slope = new NativeArray<float>(steepness, Allocator.TempJob);
        var elevation = new NativeArray<float>(height, Allocator.TempJob);
        var shape = relief != null
            ? new NativeArray<float>(relief, Allocator.TempJob)
            : new NativeArray<float>(cells, Allocator.TempJob);
        var wetness = new NativeArray<float>(Wetness(tileResolution, tileX * texel, tileY * texel, texel, height), Allocator.TempJob);
        var alphamaps = new NativeArray<float>(cells * _layers.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        try
        {
            var job = new GroundSplatJob
            {
                BiomeWeights = _weights,
                Rules = _rules,
                RuleStart = _ruleStart,
                RuleCount = _ruleCount,
                BiomeCliffs = _biomeCliffs,
                BiomeShores = _biomeShores,
                Wetness = wetness,
                RoadSegments = paint.Segments,
                RoadCellStart = paint.CellStart,
                RoadCellItems = paint.CellItems,
                Steepness = slope,
                Height = elevation,
                Relief = shape,
                Alphamaps = alphamaps,
                Resolution = tileResolution,
                WeightResolution = _weightResolution,
                BiomeCount = _biomeCount,
                LayerCount = _layers.Length,
                CliffLayer = _cliffLayerIndex,
                RoadLayer = _roadLayerIndex,
                RoadEdgeLayer = _roadEdgeLayerIndex,
                Roads = paint.Paint,
                CliffSlopeStart = _config.CliffSlopeStart,
                CliffSlopeFull = _config.CliffSlopeFull,
                Border = BiomeBorder.From(_config),
                Noise = GroundNoise.From(_config),
                TileSize = tileResolution * texel,
                WorldSize = _config.WorldSize,
                OriginX = tileX * texel,
                OriginZ = tileY * texel
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
            shape.Dispose();
            wetness.Dispose();
        }
    }

    public WaterMap Water { get; set; }

    public float[] Wetness(int tileResolution, float originX, float originZ, float texel, float[] height)
    {
        const float RISE_START = 0.4f;
        const float RISE_END = 2.5f;
        const float CORE = 0.35f;

        var wetness = new float[tileResolution * tileResolution];
        WaterMap water = Water;
        float width = _config.Water == null ? 0f : _config.Water.ShoreWidth;

        if (water == null || width <= 0f || water.Resolution < 2)
            return wetness;

        float maxHeight = _config.MaxHeight;

        System.Threading.Tasks.Parallel.For(0, tileResolution, row =>
        {
            for (int column = 0; column < tileResolution; column++)
            {
                int index = row * tileResolution + column;

                float x = originX + (column + 0.5f) * texel;
                float z = originZ + (row + 0.5f) * texel;

                float distance = ShoreDistance(water, x, z);

                if (distance >= width)
                    continue;

                float level = water.ShoreSurface[water.CellIndex(x, z)];
                float above = height[index] * maxHeight - level;

                float near = 1f - Smooth(CORE * width, width, distance);
                float low = 1f - Smooth(RISE_START, RISE_END, above);

                wetness[index] = near * low;
            }
        });

        return wetness;
    }

    private static float ShoreDistance(WaterMap water, float x, float z)
    {
        float cell = water.CellSize;
        int last = water.Resolution - 1;

        float u = Mathf.Clamp(x / cell - 0.5f, 0f, last);
        float v = Mathf.Clamp(z / cell - 0.5f, 0f, last);

        int column = Mathf.Min((int)u, last - 1);
        int row = Mathf.Min((int)v, last - 1);

        float tx = u - column;
        float ty = v - row;

        float[] distance = water.ShoreDistance;
        int origin = row * water.Resolution + column;

        float a = distance[origin], b = distance[origin + 1];
        float c = distance[origin + water.Resolution], d = distance[origin + water.Resolution + 1];

        if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsInfinity(c) || float.IsInfinity(d))
            return Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) + cell;

        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static float Smooth(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));

        return t * t * (3f - 2f * t);
    }

    public GroundRule[] RulesOf(int biome)
    {
        var rules = new GroundRule[_ruleCount[biome]];

        for (int i = 0; i < rules.Length; i++)
            rules[i] = _rules[_ruleStart[biome] + i];

        return rules;
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref _weights);
        NativeBuffer.Release(ref _rules);
        NativeBuffer.Release(ref _ruleStart);
        NativeBuffer.Release(ref _ruleCount);
        NativeBuffer.Release(ref _biomeCliffs);
        NativeBuffer.Release(ref _biomeShores);

        _paint?.Dispose();
        _paint = null;
    }

    private RoadPaintIndex PaintFor(float texel)
    {
        if (_paint != null && Mathf.Approximately(_paintTexel, texel))
            return _paint;

        _paint?.Dispose();
        _paint = new RoadPaintIndex(_roadLayerIndex >= 0 ? _roads : null, _config, texel, _roadEdgeLayerIndex >= 0);
        _paintTexel = texel;

        return _paint;
    }

    private static GroundRule Flat(int layer)
    {
        return new GroundRule
        {
            Layer = layer,
            Opacity = 1f,
            MinSlope = 0f,
            MaxSlope = 90f,
            SlopeFade = 0f,
            MinHeight = 0f,
            MaxHeight = 1f,
            HeightFade = 0f,
            PatchFrequency = 1f,
            PatchAxis = new float2(1f, 0f),
            PatchThreshold = 0f,
            PatchFade = 0f
        };
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

            for (int index = 0; index < definition.Ground.Count; index++)
            {
                GroundLayer ground = definition.Ground[index];

                if (ground == null || !ground.IsValid)
                    continue;

                if (ground.IsMute)
                {
                    Debug.LogWarning($"Biome {definition.name}: ground {ground.Layer.name} will never appear — Opacity {ground.Opacity}, slope {ground.MinSlope}..{ground.MaxSlope}, height {ground.MinHeight}..{ground.MaxHeight}, PatchThreshold {ground.PatchThreshold}");
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

                buckets[biome].Add(_config.OneGroundPerBiome ? Flat(layer) : ToRule(ground, layer, PatchStream(definition.Type, index)));

                if (_config.OneGroundPerBiome)
                    break;
            }
        }

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            starts[biome] = rules.Count;
            rules.AddRange(buckets[biome]);
            counts[biome] = rules.Count - starts[biome];
        }

        if (dropped.Length > 0)
            Debug.LogWarning($"The budget of {_config.MaxTerrainLayers} layers is spent, these did not fit: {dropped}. The grounds below them in the same biome show through where they would have been laid. Raise MaxTerrainLayers, Repetitionless Pro holds up to 32");
    }

    private static int PatchStream(BiomeType biome, int ground)
    {
        return PATCH_STREAM + (int)biome * PATCH_STREAMS_PER_BIOME + ground;
    }

    private GroundRule ToRule(GroundLayer ground, int layer, int stream)
    {
        return new GroundRule
        {
            Layer = layer,
            Opacity = ground.Opacity,
            PatchFrequency = ground.PatchFrequency,
            PatchOffset = FractalNoise.Offset(_config.Seed, stream),
            PatchAxis = FractalNoise.Axis(_config.Seed, stream),
            MacroBias = ground.MacroBias,
            SlopeBias = ground.SlopeBias,
            ReliefBias = ground.ReliefBias,
            EdgeBias = ground.EdgeBias,
            DetailSign = FractalNoise.Axis(_config.Seed, stream).x >= 0f ? 1f : -1f,
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

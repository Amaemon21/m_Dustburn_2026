using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class DecorFilter
{
    private const float ROAD_BLOCK = 0.04f;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeWeightField _weights;
    private readonly float[] _roadMask;
    private readonly int _roadMaskResolution;
    private readonly PoiPadIndex _pads;
    private readonly BiomeBorder _border;

    public DecorFilter(WorldGenerationConfig config, BiomeWeightField weights, int biomeCount,
        float[] roadMask, int roadMaskResolution, IReadOnlyList<PoiPlacement> placements, float widestFootprint)
    {
        _config = config;
        _weights = weights;
        _border = BiomeBorder.From(config);
        _roadMask = roadMask;
        _roadMaskResolution = roadMaskResolution > 0 && roadMask != null ? roadMaskResolution : 0;

        if (placements != null && placements.Count > 0)
            _pads = new PoiPadIndex(placements, config.WorldSize, 64f, config.PoiPadMargin, widestFootprint);
    }

    public bool IsClear(Vector2 point, float footprint, float roadClearance)
    {
        if (IsNearRoad(point, Mathf.Max(roadClearance, footprint)))
            return false;

        return _pads == null || !_pads.Covers(point, footprint);
    }

    public bool InBiome(Vector2 point, int biome, float roll)
    {
        return roll <= SurfaceWeight(point, biome);
    }

    public float Weight(Vector2 point, int biome)
    {
        return _weights.SampleOne(point.x / _config.WorldSize, point.y / _config.WorldSize, biome);
    }

    public float SurfaceWeight(Vector2 point, int biome)
    {
        float blend = Weight(point, biome);

        if (blend <= BiomeBorder.ABSENT)
            return 0f;

        if (blend >= BiomeBorder.PURE)
            return 1f;

        float2 shifted = _border.Displace(point.x, point.y);

        float u = shifted.x / _config.WorldSize;
        float v = shifted.y / _config.WorldSize;

        float own = 0f;
        float total = 0f;

        for (int other = 0; other < _weights.BiomeCount; other++)
        {
            float contrast = _border.Contrast(_weights.SampleOne(u, v, other));

            total += contrast;

            if (other == biome)
                own = contrast;
        }

        return total > 0f ? own / total : blend;
    }

    public bool InPatch(Vector2 point, float frequency, float threshold)
    {
        if (threshold <= 0f)
            return true;

        float patch = FractalNoise.Sample01(new float2(point.x, point.y) / frequency,
            new float2(_config.Seed & 0xFFFF, frequency), 3, 2.1f, 0.5f);

        return patch >= threshold;
    }

    private bool IsNearRoad(Vector2 point, float clearance)
    {
        if (_roadMaskResolution <= 0)
            return false;

        if (SampleRoad(point) > ROAD_BLOCK)
            return true;

        if (clearance <= 0f)
            return false;

        return SampleRoad(point + new Vector2(clearance, 0f)) > ROAD_BLOCK
            || SampleRoad(point + new Vector2(-clearance, 0f)) > ROAD_BLOCK
            || SampleRoad(point + new Vector2(0f, clearance)) > ROAD_BLOCK
            || SampleRoad(point + new Vector2(0f, -clearance)) > ROAD_BLOCK;
    }

    private float SampleRoad(Vector2 point)
    {
        int last = _roadMaskResolution - 1;

        int x = Mathf.Clamp(Mathf.RoundToInt(point.x / _config.WorldSize * last), 0, last);
        int y = Mathf.Clamp(Mathf.RoundToInt(point.y / _config.WorldSize * last), 0, last);

        return _roadMask[y * _roadMaskResolution + x];
    }
}

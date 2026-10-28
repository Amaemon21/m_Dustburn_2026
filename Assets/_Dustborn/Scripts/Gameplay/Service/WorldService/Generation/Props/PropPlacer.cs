using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed partial class PropPlacer
{
    private const float EDGE_MARGIN = 8f;
    private const float SHORE_MARGIN = 0.6f;
    private const float SLOPE_STEP = 1.5f;
    private const float OCCUPANCY_CELL = 8f;
    private const float PAD_CELL = 64f;
    private const int ATTEMPTS_PER_PROP = 4;

    private readonly WorldGenerationConfig _config;
    private readonly PropDatabase _database;
    private readonly BiomeDatabase _biomes;
    private readonly BiomeMap _biomeMap;
    private readonly HeightMap _heights;
    private readonly WaterMap _water;
    private readonly RoadNetwork _network;
    private readonly IReadOnlyList<PoiPlacement> _pois;
    private readonly RoadProximity _roads;
    private readonly PoiPadIndex _pads;
    private readonly Dictionary<long, List<(Vector2 Point, float Radius)>> _occupied = new();
    private readonly Dictionary<PropContext, (List<PropDefinition> Definitions, float Total)> _byContext = new();
    private readonly List<PropPlacement> _placed = new();
    private readonly float _widest;
    private Random _random;

    private readonly Dictionary<PropContext, int> _counts = new();
    private int _refusedWater;
    private int _refusedSlope;
    private int _refusedPad;
    private int _refusedRoad;
    private int _refusedCrowd;
    private int _refusedBiome;
    private int _refusedBorder;

    public PropPlacer(WorldGenerationConfig config, PropDatabase database, BiomeDatabase biomes, BiomeMap biomeMap,
        HeightMap heights, WaterMap water, RoadNetwork network, IReadOnlyList<PoiPlacement> pois)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _biomes = biomes;
        _biomeMap = biomeMap;
        _heights = heights ?? throw new ArgumentNullException(nameof(heights));
        _water = water;
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _pois = pois ?? Array.Empty<PoiPlacement>();
        _widest = Mathf.Max(0.1f, database.WidestRadius);

        _roads = new RoadProximity(network.Roads, config.WorldSize, config.RoadCellSize);
        _roads.AddRange(network.Streets);
        _pads = new PoiPadIndex(_pois, config.WorldSize, PAD_CELL, config.PoiPadMargin, _widest);
        _random = new Random((((uint)config.Seed | 1u) * 2654435761u + 101u) | 1u);

        foreach (PropContext context in Enum.GetValues(typeof(PropContext)))
            _byContext[context] = Collect(context);
    }

    public List<PropPlacement> Place()
    {
        _placed.Clear();

        PlaceAlongRoads(_network.Roads, PropContext.Road);
        PlaceAlongRoads(_network.Streets, PropContext.Street);
        PlaceYards();
        PlaceWild();

        Debug.Log($"Props: {_placed.Count} placed — road {Count(PropContext.Road)}, street {Count(PropContext.Street)}, yard {Count(PropContext.Yard)}, wild {Count(PropContext.Wild)}. " +
            $"Refused {_refusedWater} by water, {_refusedSlope} too steep, {_refusedPad} on a building, {_refusedRoad} on a road, " +
            $"{_refusedCrowd} crowded, {_refusedBiome} outside their biome, {_refusedBorder} past the border");

        return new List<PropPlacement>(_placed);
    }

    private (List<PropDefinition> Definitions, float Total) Collect(PropContext context)
    {
        var definitions = new List<PropDefinition>();
        float total = 0f;

        foreach (PropDefinition definition in _database.Definitions)
        {
            if (definition == null || definition.Prefab == null || !definition.Fits(context) || definition.Weight <= 0f)
                continue;

            definitions.Add(definition);
            total += definition.Weight;
        }

        return (definitions, total);
    }

    private bool TryPick(PropContext context, out PropDefinition definition)
    {
        definition = null;
        (List<PropDefinition> definitions, float total) = _byContext[context];

        if (definitions.Count == 0)
            return false;

        float roll = _random.NextFloat() * total;

        foreach (PropDefinition candidate in definitions)
        {
            roll -= candidate.Weight;
            if (roll > 0f)
                continue;

            definition = candidate;
            return true;
        }

        definition = definitions[definitions.Count - 1];
        return true;
    }

    private bool TryAccept(PropDefinition definition, Vector2 point, bool clearOfRoads, float roadClearance, out float ground)
    {
        ground = 0f;
        float radius = definition.Radius;
        float limit = _config.WorldSize - EDGE_MARGIN - radius;

        if (point.x < EDGE_MARGIN + radius || point.y < EDGE_MARGIN + radius || point.x > limit || point.y > limit)
            return Refuse(ref _refusedBorder);

        ground = _heights.SampleWorldSmooth(point.x, point.y);

        if (_water != null && (_water.IsWet(point.x, point.y, ground, SHORE_MARGIN + radius * 0.5f) || WaterCrossings.OverSpan(_water, point)))
            return Refuse(ref _refusedWater);

        if (SlopeDegrees(point) > definition.MaxSlope)
            return Refuse(ref _refusedSlope);

        if (_pads.Covers(point, radius))
            return Refuse(ref _refusedPad);

        if (clearOfRoads && _roads.IsWithin(point, radius + roadClearance))
            return Refuse(ref _refusedRoad);

        if (!InBiome(definition, point))
            return Refuse(ref _refusedBiome);

        if (Crowded(point, radius))
            return Refuse(ref _refusedCrowd);

        return true;
    }

    private static bool Refuse(ref int counter)
    {
        counter++;
        return false;
    }

    private void Commit(PropDefinition definition, PropContext context, Vector2 point, float ground, float yaw)
    {
        _placed.Add(new PropPlacement(definition, context, new Vector3(point.x, ground, point.y), Mathf.Repeat(yaw, 360f)));
        _counts.TryGetValue(context, out int count);
        _counts[context] = count + 1;

        long cell = CellOf(point);
        if (!_occupied.TryGetValue(cell, out List<(Vector2 Point, float Radius)> bucket))
        {
            bucket = new List<(Vector2 Point, float Radius)>();
            _occupied.Add(cell, bucket);
        }

        bucket.Add((point, definition.Radius));
    }

    private void PlaceCluster(PropDefinition definition, PropContext context, Vector2 centre, float yaw, bool clearOfRoads, float roadClearance)
    {
        int extra = _random.NextInt(definition.ClusterMin, definition.ClusterMax + 1) - 1;

        for (int i = 0; i < extra; i++)
        {
            float angle = _random.NextFloat() * Mathf.PI * 2f;
            float distance = definition.Radius * 2f + _random.NextFloat() * definition.ClusterRadius;
            Vector2 point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            if (TryAccept(definition, point, clearOfRoads, roadClearance, out float ground))
                Commit(definition, context, point, ground, yaw + Jitter(definition.YawJitter));
        }
    }

    private float Jitter(float range) => range <= 0f ? 0f : (_random.NextFloat() * 2f - 1f) * range;

    private bool Crowded(Vector2 point, float radius)
    {
        int reach = Mathf.CeilToInt((radius + _widest) / OCCUPANCY_CELL);
        int cx = Mathf.FloorToInt(point.x / OCCUPANCY_CELL);
        int cy = Mathf.FloorToInt(point.y / OCCUPANCY_CELL);

        for (int y = cy - reach; y <= cy + reach; y++)
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (!_occupied.TryGetValue(Key(x, y), out List<(Vector2 Point, float Radius)> bucket))
                    continue;

                foreach ((Vector2 other, float otherRadius) in bucket)
                {
                    float limit = radius + otherRadius;
                    if ((other - point).sqrMagnitude < limit * limit)
                        return true;
                }
            }

        return false;
    }

    private float SlopeDegrees(Vector2 point)
    {
        float dx = _heights.SampleWorldSmooth(point.x + SLOPE_STEP, point.y) - _heights.SampleWorldSmooth(point.x - SLOPE_STEP, point.y);
        float dz = _heights.SampleWorldSmooth(point.x, point.y + SLOPE_STEP) - _heights.SampleWorldSmooth(point.x, point.y - SLOPE_STEP);
        float gradient = Mathf.Sqrt(dx * dx + dz * dz) / (2f * SLOPE_STEP);
        return Mathf.Atan(gradient) * Mathf.Rad2Deg;
    }

    private bool InBiome(PropDefinition definition, Vector2 point)
    {
        if (definition.Biomes == null || definition.Biomes.Count == 0 || _biomeMap == null || _biomes == null)
            return true;

        int index = _biomeMap.SampleWorld(new Vector3(point.x, 0f, point.y));
        if (index < 0 || index >= _biomes.Biomes.Count || _biomes.Biomes[index] == null)
            return false;

        return definition.Allows(_biomes.Biomes[index].Type);
    }

    private int Count(PropContext context) => _counts.TryGetValue(context, out int count) ? count : 0;

    private static long CellOf(Vector2 point) => Key(Mathf.FloorToInt(point.x / OCCUPANCY_CELL), Mathf.FloorToInt(point.y / OCCUPANCY_CELL));

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

    private static float YawOf(Vector2 direction) => Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
}

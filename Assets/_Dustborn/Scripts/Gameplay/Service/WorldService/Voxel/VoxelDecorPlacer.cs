using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct DecorInstance
{
    public Vector3 Position;
    public float Rotation;
    public float Scale;
    public float Height;
    public Color Tint;
}

public class VoxelDecorPlacer
{
    private const float BIOME_PROBE_STEP = 24f;
    private const float BIOME_EPSILON = 0.002f;
    private const int MAX_BIOME_PROBES = 8;

    private readonly WorldGenerationConfig _config;
    private readonly VoxelDensityField _field;
    private readonly DecorFilter _filter;
    private readonly List<VoxelDecorLayer> _layers = new();

    private readonly float _grassScale;

    public IReadOnlyList<VoxelDecorLayer> Layers => _layers;

    public int Rejected { get; private set; }

    public VoxelDecorPlacer(WorldGenerationConfig config, BiomeDatabase biomes, VoxelDensityField field, DecorFilter filter, float grassScale = 1f)
    {
        _config = config;
        _field = field;
        _filter = filter;
        _grassScale = Mathf.Clamp01(grassScale);

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            BiomeDefinition definition = biomes.Get(biome);

            Collect(biome, definition, definition.Trees, DecorKind.Tree);
            Collect(biome, definition, definition.Rocks, DecorKind.Rock);
            CollectGrass(biome, definition);
        }
    }

    public int Place(int layerIndex, Vector2 origin, float span, List<DecorInstance> output, DecorSurface frame = default)
    {
        VoxelDecorLayer layer = _layers[layerIndex];

        float spacing = layer.Spacing;
        float chance = layer.Kind == DecorKind.Grass ? layer.Chance * _grassScale : layer.Chance;

        float minCornerX = Mathf.Max(origin.x, 0f);
        float minCornerY = Mathf.Max(origin.y, 0f);
        float maxCornerX = Mathf.Min(origin.x + span, _config.WorldSize);
        float maxCornerY = Mathf.Min(origin.y + span, _config.WorldSize);

        if (chance <= 0f || minCornerX >= maxCornerX || minCornerY >= maxCornerY)
            return 0;

        if (!InBiome(layer, origin, span))
            return 0;

        int minX = Mathf.FloorToInt(origin.x / spacing);
        int maxX = Mathf.CeilToInt((origin.x + span) / spacing);
        int minY = Mathf.FloorToInt(origin.y / spacing);
        int maxY = Mathf.CeilToInt((origin.y + span) / spacing);

        for (int cellY = minY; cellY <= maxY; cellY++)
        {
            for (int cellX = minX; cellX <= maxX; cellX++)
            {
                var random = new Random(Hash(cellX, cellY, layerIndex));

                var point = new Vector2((cellX + random.NextFloat()) * spacing, (cellY + random.NextFloat()) * spacing);

                if (point.x < minCornerX || point.y < minCornerY || point.x >= maxCornerX || point.y >= maxCornerY)
                    continue;

                if (random.NextFloat() > chance)
                    continue;

                if (!Fits(layer, point, random.NextFloat(), frame, out float surface))
                {
                    Rejected++;
                    continue;
                }

                output.Add(Build(layer, point, surface, ref random));
            }
        }

        return (maxX - minX + 1) * (maxY - minY + 1);
    }

    private bool InBiome(VoxelDecorLayer layer, Vector2 origin, float span)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(span / BIOME_PROBE_STEP), 1, MAX_BIOME_PROBES);

        for (int y = 0; y <= steps; y++)
        {
            for (int x = 0; x <= steps; x++)
            {
                var point = new Vector2(origin.x + span * x / steps, origin.y + span * y / steps);

                if (_filter.Weight(point, layer.Biome) > BIOME_EPSILON)
                    return true;
            }
        }

        return false;
    }

    private bool Fits(VoxelDecorLayer layer, Vector2 point, float roll, DecorSurface frame, out float surface)
    {
        surface = 0f;

        if (!_filter.InBiome(point, layer.Biome, roll))
            return false;

        if (!_filter.IsClear(point, layer.Footprint, layer.RoadClearance))
            return false;

        surface = _field.Surface(point.x, point.y, frame);

        float elevation = surface / _config.MaxHeight;

        if (elevation < layer.MinHeight || elevation > layer.MaxHeight)
            return false;

        if (Slope(point) > layer.MaxSlope)
            return false;

        return _filter.InPatch(point, layer.PatchFrequency, layer.PatchThreshold);
    }

    private float Slope(Vector2 point)
    {
        float step = _config.HeightCellSize;

        float dx = _field.Surface(point.x + step, point.y) - _field.Surface(point.x - step, point.y);
        float dz = _field.Surface(point.x, point.y + step) - _field.Surface(point.x, point.y - step);

        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * step)) * Mathf.Rad2Deg;
    }

    private DecorInstance Build(VoxelDecorLayer layer, Vector2 point, float surface, ref Random random)
    {
        float scale = random.NextFloat(layer.MinScale, Mathf.Max(layer.MinScale, layer.MaxScale));
        float squash = layer.Squash <= 0f ? 1f : random.NextFloat(1f - layer.Squash, 1f + layer.Squash);
        float shade = layer.TintVariance <= 0f ? 1f : random.NextFloat(1f - layer.TintVariance, 1f + layer.TintVariance);

        return new DecorInstance
        {
            Position = new Vector3(point.x, surface, point.y),
            Rotation = random.NextFloat(0f, 360f),
            Scale = scale,
            Height = scale * squash * layer.HeightScale,
            Tint = layer.Tint * shade
        };
    }

    private void Collect(int biome, BiomeDefinition definition, List<ScatterLayer> layers, DecorKind kind)
    {
        foreach (ScatterLayer layer in layers)
        {
            if (layer == null || !layer.IsValid || layer.IsMute)
                continue;

            _layers.Add(VoxelDecorLayer.FromScatter(biome, layer, kind));
        }
    }

    private void CollectGrass(int biome, BiomeDefinition definition)
    {
        foreach (GrassLayer layer in definition.Grass)
        {
            if (layer == null || layer.IsMute)
                continue;

            if (!layer.IsValid)
            {
                Debug.LogWarning($"Biome {definition.name}: grass with neither a card texture nor a prefab is skipped, there is nothing to draw");
                continue;
            }

            if (layer.Card != null && layer.Mask == null)
                Debug.LogWarning($"Biome {definition.name}: grass card {layer.Card.name} has no color mask, the leaves color is used for the whole plant");

            _layers.Add(VoxelDecorLayer.FromGrass(biome, layer));
        }
    }

    private uint Hash(int cellX, int cellY, int layer)
    {
        unchecked
        {
            uint hash = (uint)_config.Seed * 2654435761u;

            hash ^= (uint)cellX * 2246822519u;
            hash ^= (uint)cellY * 3266489917u;
            hash ^= (uint)(layer + 1) * 668265263u;
            hash ^= hash >> 15;

            return hash == 0u ? 1u : hash;
        }
    }
}

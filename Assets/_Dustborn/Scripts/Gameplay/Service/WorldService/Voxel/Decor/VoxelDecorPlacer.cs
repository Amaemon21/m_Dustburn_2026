using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct DecorInstance
{
    public Vector3 Position;
    public float Rotation;
    public float Scale;
    public float Height;
    public float Shade;
    public Vector2 Slope;
    public float Lean;
    public int CellX;
    public int CellY;
}

public class VoxelDecorPlacer
{
    private const float BIOME_PROBE_STEP = 24f;
    private const float BIOME_EPSILON = 0.002f;
    private const int MAX_BIOME_PROBES = 8;

    public const float TRUNK_RADIUS = 0.4f;
    public const float ROCK_LEAN = 1f;

    private readonly WorldGenerationConfig _config;
    private readonly VoxelDensityField _field;
    private readonly DecorFilter _filter;
    private readonly List<VoxelDecorLayer> _layers = new();

    private readonly float _grassScale;

    public IReadOnlyList<VoxelDecorLayer> Layers => _layers;

    public DecorEditIndex Edits { get; set; }

    private int _rejected;

    public int Rejected => _rejected;

    public VoxelDecorPlacer(WorldGenerationConfig config, BiomeDatabase biomes, VoxelDensityField field, DecorFilter filter, float grassScale = 1f)
    {
        _config = config;
        _field = field;
        _filter = filter;
        _grassScale = Mathf.Max(0f, grassScale);

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
        float chance = layer.Chance;

        if (layer.Kind == DecorKind.Grass)
            GrassDensity(ref spacing, ref chance);

        float minCornerX = Mathf.Max(origin.x, 0f);
        float minCornerY = Mathf.Max(origin.y, 0f);
        float maxCornerX = Mathf.Min(origin.x + span, _config.WorldSize);
        float maxCornerY = Mathf.Min(origin.y + span, _config.WorldSize);

        if (chance <= 0f || minCornerX >= maxCornerX || minCornerY >= maxCornerY)
            return 0;

        DecorEditIndex edits = Edits;
        bool edited = edits != null && edits.Touches(layerIndex);

        if (edited)
            AddMoved(layer, edits.Moved(layerIndex), new Vector2(minCornerX, minCornerY), new Vector2(maxCornerX, maxCornerY), output, frame);

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

                if (edited && edits.Overrides(new DecorKey(layerIndex, cellX, cellY)))
                    continue;

                if (random.NextFloat() > chance)
                    continue;

                if (!Fits(layer, point, random.NextFloat(), frame, out float surface))
                {
                    System.Threading.Interlocked.Increment(ref _rejected);
                    continue;
                }

                DecorInstance instance = Build(layer, point, surface, ref random);
                Ground(layer, ref instance, frame);
                instance.CellX = cellX;
                instance.CellY = cellY;
                output.Add(instance);
            }
        }

        return (maxX - minX + 1) * (maxY - minY + 1);
    }

    public static float TrunkSink(Vector2 slope, float scale)
    {
        return Mathf.Sqrt(slope.x * slope.x + slope.y * slope.y) * TRUNK_RADIUS * scale;
    }

    private void AddMoved(VoxelDecorLayer layer, IReadOnlyList<DecorEdit> moved, Vector2 min, Vector2 max, List<DecorInstance> output,
        DecorSurface frame)
    {
        foreach (DecorEdit edit in moved)
        {
            Vector2 point = edit.Position;

            if (point.x < min.x || point.y < min.y || point.x >= max.x || point.y >= max.y)
                continue;

            DecorInstance instance = edit.Instance(0f);

            Ground(layer, ref instance, frame);
            output.Add(instance);
        }
    }

    private void Ground(VoxelDecorLayer layer, ref DecorInstance instance, DecorSurface frame)
    {
        float ground = _field.Drawn(instance.Position.x, instance.Position.z, frame, out Vector2 slope);

        instance.Slope = slope;
        instance.Lean = layer.Kind == DecorKind.Rock ? ROCK_LEAN : 0f;
        instance.Position.y = layer.Kind == DecorKind.Tree ? ground - TrunkSink(slope, instance.Scale) : ground;
    }

    private void GrassDensity(ref float spacing, ref float chance)
    {
        if (_grassScale <= 1f)
        {
            chance *= _grassScale;
            return;
        }

        spacing /= Mathf.Sqrt(_grassScale);
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

        if (!_filter.IsDry(point, surface, layer.Footprint))
            return false;

        if (layer.ShoreDistance > 0f && !_filter.IsNearWater(point, surface, layer.ShoreDistance))
            return false;

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
            Shade = shade
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

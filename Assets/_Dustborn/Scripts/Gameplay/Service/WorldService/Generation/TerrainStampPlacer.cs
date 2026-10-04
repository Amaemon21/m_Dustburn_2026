using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed class TerrainStampPlacer
{
    private const int ATTEMPTS_PER_STAMP = 48;

    private readonly WorldGenerationConfig _config;
    private readonly TerrainStampSettings _settings;
    private readonly BiomeDatabase _biomes;
    private readonly BiomeMap _biomeMap;

    public int Attempts { get; private set; }

    public TerrainStampPlacer(WorldGenerationConfig config, TerrainStampSettings settings, BiomeDatabase biomes, BiomeMap biomeMap)
    {
        _config = config;
        _settings = settings;
        _biomes = biomes;
        _biomeMap = biomeMap;
    }

    public List<TerrainStampPlacement> Place()
    {
        var placed = new List<TerrainStampPlacement>();
        TerrainStampDatabase database = _settings.Database;

        if (!_settings.Enabled || database == null || database.Count == 0 || _settings.Count <= 0)
            return placed;

        float world = _config.WorldSize;
        float margin = _settings.EdgeMargin;
        var weights = new float[database.Count];

        for (int attempt = 0; attempt < _settings.Count * ATTEMPTS_PER_STAMP && placed.Count < _settings.Count; attempt++)
        {
            Attempts++;

            var random = new Random(Hash(_config.Seed, 0x5157u, attempt));
            var center = new Vector2(random.NextFloat(margin, world - margin), random.NextFloat(margin, world - margin));

            int stamp = Choose(database, BiomeAt(center), random.NextFloat(), weights);

            if (stamp < 0)
                continue;

            TerrainStampPlacement placement = Shape(database.Get(stamp), stamp, center, placed.Count);

            if (!InsideWorld(placement, margin, world) || Crowded(placement, placed))
                continue;

            placed.Add(placement);
        }

        return placed;
    }

    private TerrainStampPlacement Shape(TerrainStampDefinition definition, int stamp, Vector2 center, int index)
    {
        var random = new Random(Hash(_config.Seed, (uint)stamp + 1u, index));

        float scale = 1f + random.NextFloat(-_settings.ScaleVariation, _settings.ScaleVariation);
        float stretch = random.NextFloat(-_settings.Stretch, _settings.Stretch);
        float amplitude = 1f + random.NextFloat(-_settings.AmplitudeVariation, _settings.AmplitudeVariation);

        return new TerrainStampPlacement
        {
            Center = center,
            Size = new Vector2(definition.RecommendedSize.x * scale * (1f + stretch), definition.RecommendedSize.y * scale * (1f - stretch)),
            Amplitude = definition.RecommendedAmplitude * _settings.AmplitudeScale * amplitude,
            Rotation = random.NextFloat(0f, 360f),
            StampIndex = stamp,
            Operation = definition.Operation,
            Name = definition.Name
        };
    }

    private int Choose(TerrainStampDatabase database, BiomeType biome, float roll, float[] weights)
    {
        float total = 0f;

        for (int i = 0; i < database.Count; i++)
        {
            TerrainStampDefinition definition = database.Get(i);

            if (definition == null || !definition.IsValid)
            {
                weights[i] = 0f;
                continue;
            }

            float affinity = definition.PreferredBiomes.Count == 0 || definition.PreferredBiomes.Contains(biome) ? 1f : _settings.OffBiomeWeight;

            weights[i] = definition.Weight * affinity;
            total += weights[i];
        }

        if (total <= 0f)
            return -1;

        float target = roll * total;

        for (int i = 0; i < weights.Length; i++)
        {
            target -= weights[i];

            if (target < 0f && weights[i] > 0f)
                return i;
        }

        for (int i = weights.Length - 1; i >= 0; i--)
        {
            if (weights[i] > 0f)
                return i;
        }

        return -1;
    }

    private BiomeType BiomeAt(Vector2 point)
    {
        if (_biomeMap == null || _biomes == null || _biomes.Count == 0)
            return BiomeType.PineForest;

        int index = _biomeMap.SampleWorld(new Vector3(point.x, 0f, point.y));

        return _biomes.Get(Mathf.Clamp(index, 0, _biomes.Count - 1)).Type;
    }

    private static bool InsideWorld(TerrainStampPlacement placement, float margin, float world)
    {
        placement.Bounds(out Vector2 min, out Vector2 max);

        return min.x >= margin && min.y >= margin && max.x <= world - margin && max.y <= world - margin;
    }

    private bool Crowded(TerrainStampPlacement candidate, List<TerrainStampPlacement> placed)
    {
        foreach (TerrainStampPlacement other in placed)
        {
            float distance = Vector2.Distance(candidate.Center, other.Center);

            if (distance < _settings.MinSpacing)
                return true;

            float smaller = Mathf.Min(candidate.Radius, other.Radius);

            if (Overlap(candidate.Radius, other.Radius, distance) > _settings.MaxOverlap * Mathf.PI * smaller * smaller)
                return true;
        }

        return false;
    }

    public static float Overlap(float first, float second, float distance)
    {
        if (distance >= first + second)
            return 0f;

        float smaller = Mathf.Min(first, second);

        if (distance <= Mathf.Abs(first - second))
            return Mathf.PI * smaller * smaller;

        float a = first * first * Mathf.Acos(Mathf.Clamp((distance * distance + first * first - second * second) / (2f * distance * first), -1f, 1f));
        float b = second * second * Mathf.Acos(Mathf.Clamp((distance * distance + second * second - first * first) / (2f * distance * second), -1f, 1f));
        float c = 0.5f * Mathf.Sqrt(Mathf.Max(0f, (-distance + first + second) * (distance + first - second) * (distance - first + second) * (distance + first + second)));

        return a + b - c;
    }

    private static uint Hash(int seed, uint salt, int index)
    {
        unchecked
        {
            uint hash = (uint)seed * 2654435761u;

            hash ^= salt * 2246822519u;
            hash ^= (uint)(index + 1) * 3266489917u;
            hash ^= hash >> 15;
            hash *= 668265263u;
            hash ^= hash >> 13;

            return hash == 0u ? 1u : hash;
        }
    }
}

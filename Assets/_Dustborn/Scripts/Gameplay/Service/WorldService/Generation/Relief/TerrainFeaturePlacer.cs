using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed class TerrainFeaturePlacer
{
    private const int ATTEMPTS_PER_FEATURE = 64;
    private const float MIN_CRATER_DIAMETER = 360f;
    private const float MAX_CRATER_DIAMETER = 820f;
    private const float CRATER_DEPTH_RATIO = 0.075f;
    private const float CRATER_RIM_RATIO = 0.4f;
    private const float FEATURE_GAP = 0.7f;
    private const float SMALL_WORLD_FILL = 0.95f;
    private const uint CRATER_SALT = 0xC4A7u;
    private const uint RIFT_SALT = 0x41F7u;
    private const uint CANYON_SALT = 0xCA9Bu;

    private readonly WorldGenerationConfig _config;
    private readonly TerrainStampSettings _settings;
    private readonly List<TerrainStampPlacement> _placed;

    public int Craters { get; private set; }
    public int Rifts { get; private set; }
    public int Canyons { get; private set; }

    public TerrainFeaturePlacer(WorldGenerationConfig config, TerrainStampSettings settings, List<TerrainStampPlacement> placed)
    {
        _config = config;
        _settings = settings;
        _placed = placed;
    }

    public static bool IsRift(TerrainStampCategory category) => category == TerrainStampCategory.Ravines || category == TerrainStampCategory.Escarpment;

    public static bool IsCanyon(TerrainStampCategory category) => category == TerrainStampCategory.Canyon;

    public void Place()
    {
        Craters = PlaceCraters(_config.CraterCount);
        Rifts = PlaceStamps(_config.RiftCount, Candidates(IsRift), RIFT_SALT, "rift");
        Canyons = PlaceStamps(_config.CanyonCount, Candidates(IsCanyon), CANYON_SALT, "canyon");
    }

    private int PlaceCraters(int count)
    {
        int first = TerrainStampShapes.FirstProcedural(_settings.Database);
        int placed = 0;

        for (int attempt = 0; attempt < count * ATTEMPTS_PER_FEATURE && placed < count; attempt++)
        {
            var random = new Random(TerrainStampPlacer.Hash(_config.Seed, CRATER_SALT, attempt));
            float diameter = random.NextFloat(MIN_CRATER_DIAMETER, MAX_CRATER_DIAMETER);
            float span = diameter / TerrainStampShapes.CRATER_RADIUS;
            var size = new Vector2(span, span);
            Vector2 center = Center(ref random, span);

            var bowl = new TerrainStampPlacement
            {
                Center = center,
                Size = size,
                Amplitude = diameter * CRATER_DEPTH_RATIO,
                Rotation = 0f,
                StampIndex = first + TerrainStampShapes.CRATER_BOWL,
                Operation = TerrainStampOperation.Subtract,
                Name = TerrainStampShapes.CRATER_BOWL_NAME
            };

            if (!Fits(bowl))
                continue;

            var rim = bowl;
            rim.Amplitude = bowl.Amplitude * CRATER_RIM_RATIO;
            rim.StampIndex = first + TerrainStampShapes.CRATER_RIM;
            rim.Operation = TerrainStampOperation.Add;
            rim.Name = TerrainStampShapes.CRATER_RIM_NAME;

            _placed.Add(bowl);
            _placed.Add(rim);
            placed++;
        }

        return placed;
    }

    private int PlaceStamps(int count, List<int> candidates, uint salt, string kind)
    {
        if (count <= 0)
            return 0;

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"{nameof(TerrainFeaturePlacer)}: the terrain stamp database holds no {kind} stamps, {count} requested are skipped");
            return 0;
        }

        int placed = 0;

        for (int attempt = 0; attempt < count * ATTEMPTS_PER_FEATURE && placed < count; attempt++)
        {
            var random = new Random(TerrainStampPlacer.Hash(_config.Seed, salt, attempt));
            int stamp = candidates[random.NextInt(candidates.Count)];
            TerrainStampDefinition definition = _settings.Database.Get(stamp);

            float scale = 1f + random.NextFloat(-_settings.ScaleVariation, _settings.ScaleVariation);
            float stretch = random.NextFloat(-_settings.Stretch, _settings.Stretch);
            float amplitude = 1f + random.NextFloat(-_settings.AmplitudeVariation, _settings.AmplitudeVariation);
            var size = new Vector2(definition.RecommendedSize.x * scale * (1f + stretch), definition.RecommendedSize.y * scale * (1f - stretch));
            float span = size.magnitude;
            float room = SMALL_WORLD_FILL * (_config.WorldSize - 2f * _settings.EdgeMargin);

            if (span > room)
            {
                size *= room / span;
                span = room;
            }

            var placement = new TerrainStampPlacement
            {
                Center = Center(ref random, span),
                Size = size,
                Amplitude = definition.RecommendedAmplitude * _settings.AmplitudeScale * amplitude,
                Rotation = random.NextFloat(0f, 360f),
                StampIndex = stamp,
                Operation = definition.Operation,
                Name = definition.Name
            };

            if (!Fits(placement))
                continue;

            _placed.Add(placement);
            placed++;
        }

        return placed;
    }

    private List<int> Candidates(System.Func<TerrainStampCategory, bool> accepts)
    {
        var candidates = new List<int>();
        TerrainStampDatabase database = _settings.Database;

        if (database == null)
            return candidates;

        for (int i = 0; i < database.Count; i++)
        {
            TerrainStampDefinition definition = database.Get(i);

            if (definition != null && definition.IsValid && accepts(definition.Category))
                candidates.Add(i);
        }

        return candidates;
    }

    private Vector2 Center(ref Random random, float span)
    {
        float world = _config.WorldSize;
        float margin = Mathf.Min(world * 0.5f - 1f, _settings.EdgeMargin + span * 0.5f);

        return new Vector2(random.NextFloat(margin, world - margin), random.NextFloat(margin, world - margin));
    }

    private bool Fits(TerrainStampPlacement candidate)
    {
        candidate.Bounds(out Vector2 min, out Vector2 max);
        float world = _config.WorldSize;
        float margin = _settings.EdgeMargin;

        if (min.x < margin || min.y < margin || max.x > world - margin || max.y > world - margin)
            return false;

        foreach (TerrainStampPlacement other in _placed)
        {
            float gap = FEATURE_GAP * (candidate.Radius + other.Radius);

            if ((candidate.Center - other.Center).sqrMagnitude < gap * gap)
                return false;
        }

        return true;
    }
}

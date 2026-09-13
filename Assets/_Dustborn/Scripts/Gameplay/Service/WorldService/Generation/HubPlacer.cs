using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class HubPlacer
{
    private const int RELIEF_SAMPLES = 7;
    private const int TILE_SAMPLES = 5;
    private const float SCORE_JITTER = 0.08f;

    private static readonly SettlementType[] ORDER =
    {
        SettlementType.City,
        SettlementType.Town,
        SettlementType.CountryTown,
        SettlementType.GhostTown
    };

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;

    public int[] Requested { get; } = new int[ORDER.Length];
    public int[] Placed { get; } = new int[ORDER.Length];
    public int BuildableCells { get; private set; }

    public HubPlacer(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;
    }

    public List<Hub> Place()
    {
        float step = Mathf.Max(8f, _config.TileSize * 0.5f);
        int resolution = Mathf.Max(1, Mathf.FloorToInt(_config.WorldSize / step));

        bool[] buildable = Buildable(step, resolution);
        int[] summed = SummedArea(buildable, resolution);
        var hubs = new List<Hub>();

        foreach (SettlementType type in ORDER)
            PlaceType(type, hubs, buildable, summed, step, resolution);

        Report(hubs);

        return hubs;
    }

    private void PlaceType(SettlementType type, List<Hub> hubs, bool[] buildable, int[] summed, float step, int resolution)
    {
        SettlementTypeProfile profile = _config.Profile(type);
        int index = (int)type;

        if (profile == null || profile.Count <= 0)
            return;

        Requested[index] = profile.Count;

        float radius = profile.EstimateRadius(_config.TileSize);
        int reach = Mathf.Max(0, Mathf.CeilToInt((radius - _config.TileSize * 0.5f) / step));
        var candidates = new List<(float Score, int Cell)>();

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int cell = y * resolution + x;

                if (!buildable[cell])
                    continue;

                var center = new Vector2((x + 0.5f) * step, (y + 0.5f) * step);

                if (!InsideMargin(center, radius))
                    continue;

                float share = Share(summed, resolution, x - reach, y - reach, x + reach, y + reach);

                if (share < _config.SiteBuildableShare)
                    continue;

                candidates.Add((share + SCORE_JITTER * Noise(cell, type), cell));
            }
        }

        candidates.Sort((left, right) =>
        {
            int compare = right.Score.CompareTo(left.Score);
            return compare != 0 ? compare : left.Cell.CompareTo(right.Cell);
        });

        foreach ((float _, int cell) in candidates)
        {
            if (Placed[index] >= profile.Count)
                break;

            var center = new Vector2((cell % resolution + 0.5f) * step, (cell / resolution + 0.5f) * step);

            if (!IsSpaced(hubs, center, radius, type, profile))
                continue;

            float relief = MeasureRelief(center.x, center.y);

            if (relief > _config.MaxHubRelief)
                continue;

            hubs.Add(new Hub(center, radius, relief, 0, type));
            Placed[index]++;
        }
    }

    private bool[] Buildable(float step, int resolution)
    {
        var buildable = new bool[resolution * resolution];
        float half = _config.TileSize * 0.5f;
        int count = 0;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float centerX = (x + 0.5f) * step;
                float centerY = (y + 0.5f) * step;

                if (centerX < half || centerY < half || centerX > _config.WorldSize - half || centerY > _config.WorldSize - half)
                    continue;

                if (!IsBuildableSquare(centerX, centerY, half))
                    continue;

                buildable[y * resolution + x] = true;
                count++;
            }
        }

        BuildableCells = count;

        return buildable;
    }

    private bool IsBuildableSquare(float centerX, float centerY, float half)
    {
        float low = float.MaxValue;
        float high = float.MinValue;

        for (int j = 0; j < TILE_SAMPLES; j++)
        {
            for (int i = 0; i < TILE_SAMPLES; i++)
            {
                float x = centerX + (i / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                float y = centerY + (j / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                float height = _map.SampleWorldSmooth(x, y);

                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
        }

        if (_config.SeaLevel > 0f && low < _config.SeaLevel + _config.ShoreMargin)
            return false;

        return high - low <= _config.MaxTileRelief;
    }

    private static int[] SummedArea(bool[] cells, int resolution)
    {
        int side = resolution + 1;
        var summed = new int[side * side];

        for (int y = 0; y < resolution; y++)
        {
            int row = 0;

            for (int x = 0; x < resolution; x++)
            {
                row += cells[y * resolution + x] ? 1 : 0;
                summed[(y + 1) * side + x + 1] = summed[y * side + x + 1] + row;
            }
        }

        return summed;
    }

    private static float Share(int[] summed, int resolution, int minX, int minY, int maxX, int maxY)
    {
        int side = resolution + 1;
        int area = (maxX - minX + 1) * (maxY - minY + 1);

        minX = Mathf.Clamp(minX, 0, resolution - 1);
        minY = Mathf.Clamp(minY, 0, resolution - 1);
        maxX = Mathf.Clamp(maxX, 0, resolution - 1);
        maxY = Mathf.Clamp(maxY, 0, resolution - 1);

        int total = summed[(maxY + 1) * side + maxX + 1] - summed[minY * side + maxX + 1]
            - summed[(maxY + 1) * side + minX] + summed[minY * side + minX];

        return area <= 0 ? 0f : total / (float)area;
    }

    private bool InsideMargin(Vector2 center, float radius)
    {
        float margin = Mathf.Min(radius + _config.HubEdgeMargin, _config.WorldSize * 0.5f);

        return center.x >= margin && center.y >= margin && center.x <= _config.WorldSize - margin && center.y <= _config.WorldSize - margin;
    }

    private bool IsSpaced(List<Hub> hubs, Vector2 center, float radius, SettlementType type, SettlementTypeProfile profile)
    {
        foreach (Hub hub in hubs)
        {
            float required = radius + hub.Radius + _config.SettlementGap;

            if (hub.Type == type)
                required = Mathf.Max(required, profile.Spacing);

            if ((hub.Position - center).sqrMagnitude < required * required)
                return false;
        }

        return true;
    }

    private float Noise(int cell, SettlementType type)
    {
        uint hash = (uint)cell * 2654435761u ^ ((uint)_config.Seed * 2246822519u) ^ ((uint)type + 1u) * 3266489917u;
        var random = new Random(hash | 1u);

        random.NextUInt();

        return random.NextFloat();
    }

    private float MeasureRelief(float centerX, float centerY)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        float radius = _config.HubSampleRadius;

        for (int i = 0; i < RELIEF_SAMPLES; i++)
        {
            for (int j = 0; j < RELIEF_SAMPLES; j++)
            {
                float x = centerX + (i / (RELIEF_SAMPLES - 1f) - 0.5f) * 2f * radius;
                float y = centerY + (j / (RELIEF_SAMPLES - 1f) - 0.5f) * 2f * radius;
                float height = _map.SampleWorldSmooth(x, y);

                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }
        }

        return max - min;
    }

    private void Report(List<Hub> hubs)
    {
        Debug.Log($"Settlement sites: {hubs.Count} placed, {Placed[0]} of {Requested[0]} cities, {Placed[1]} of {Requested[1]} towns, {Placed[2]} of {Requested[2]} country towns, {Placed[3]} of {Requested[3]} ghost towns, over {BuildableCells} buildable tile cells");

        for (int i = 0; i < ORDER.Length; i++)
        {
            if (Placed[i] < Requested[i])
                Debug.Log($"{ORDER[i]}: only {Placed[i]} of {Requested[i]} sites found. Lower SiteBuildableShare, the profile Spacing or SettlementGap, or raise MaxTileRelief");
        }
    }
}

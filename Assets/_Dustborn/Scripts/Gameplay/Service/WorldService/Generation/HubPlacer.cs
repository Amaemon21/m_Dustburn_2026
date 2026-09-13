using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class HubPlacer
{
    private const int RELIEF_SAMPLES = 7;

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;

    public HubPlacer(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;
    }

    public List<Hub> Place()
    {
        List<Hub> candidates = CollectSectorBest(true);

        if (candidates.Count < 2)
        {
            Debug.LogWarning($"No site fits within MaxHubRelief {_config.MaxHubRelief} m. Taking the flattest spots as they are — raise MaxHubRelief or lower ReliefScale");

            candidates = CollectSectorBest(false);
        }

        candidates.Sort((a, b) => a.Relief.CompareTo(b.Relief));

        var hubs = new List<Hub>();
        float minDistanceSqr = _config.MinHubDistance * _config.MinHubDistance;

        foreach (Hub candidate in candidates)
        {
            if (hubs.Count >= _config.HubCount)
                break;

            if (IsTooClose(hubs, candidate, minDistanceSqr))
                continue;

            hubs.Add(candidate);
        }

        AssignHouses(hubs);

        if (hubs.Count < _config.HubCount)
            Debug.Log($"{hubs.Count} settlements of the {_config.HubCount} requested. Candidates were culled by MinHubDistance {_config.MinHubDistance} m and by the world border, since a settlement has to fit inside the map. Lower MinHubDistance or MaxHouses");

        return hubs;
    }

    private List<Hub> CollectSectorBest(bool respectMaxRelief)
    {
        int sectors = Mathf.CeilToInt(Mathf.Sqrt(_config.HubCount));
        float margin = EdgeMargin();
        float sectorSize = (_config.WorldSize - 2f * margin) / sectors;
        float step = _config.HubCandidateStep;

        var best = new List<Hub>();

        for (int sectorY = 0; sectorY < sectors; sectorY++)
        {
            for (int sectorX = 0; sectorX < sectors; sectorX++)
            {
                Hub winner = null;

                float originX = margin + sectorX * sectorSize;
                float originY = margin + sectorY * sectorSize;

                for (float y = originY + step; y < originY + sectorSize; y += step)
                {
                    for (float x = originX + step; x < originX + sectorSize; x += step)
                    {
                        if (IsFlooded(x, y))
                            continue;

                        float relief = MeasureRelief(x, y);

                        if (respectMaxRelief && relief > _config.MaxHubRelief)
                            continue;

                        if (winner != null && relief >= winner.Relief)
                            continue;

                        winner = new Hub(new Vector2(x, y), 0f, relief, 0);
                    }
                }

                if (winner != null)
                    best.Add(winner);
            }
        }

        return best;
    }

    private void AssignHouses(List<Hub> hubs)
    {
        if (hubs.Count == 0)
            return;

        var random = new Random(((uint)_config.Seed | 1u) * 3266489917u + 5u);

        int low = Mathf.Max(1, Mathf.Min(_config.MinHouses, _config.MaxHouses));
        int high = Mathf.Max(low, Mathf.Max(_config.MinHouses, _config.MaxHouses));
        float bias = Mathf.Max(0.01f, _config.HouseCountBias);

        var sizes = new int[hubs.Count];

        for (int i = 0; i < sizes.Length; i++)
            sizes[i] = Mathf.RoundToInt(Mathf.Lerp(low, high, Mathf.Pow(random.NextFloat(), bias)));

        System.Array.Sort(sizes);
        System.Array.Reverse(sizes);

        long total = 0;

        for (int i = 0; i < hubs.Count; i++)
        {
            hubs[i] = new Hub(hubs[i].Position, BlockLattice.EstimateRadius(_config, sizes[i]), hubs[i].Relief, sizes[i]);
            total += sizes[i];
        }

        Debug.Log($"Settlements: {hubs.Count}, from {sizes[^1]} to {sizes[0]} houses each, {total} in all. The flattest site gets the largest settlement");
    }

    private float EdgeMargin()
    {
        float reach = BlockLattice.EstimateRadius(_config, Mathf.Max(_config.MinHouses, _config.MaxHouses));
        float margin = Mathf.Max(_config.HubEdgeMargin, reach);
        float limit = (_config.WorldSize - _config.HubCandidateStep * 4f) * 0.5f;

        if (margin < limit)
            return margin;

        Debug.LogWarning($"HubEdgeMargin {margin:F0} m leaves no room in a {_config.WorldSize} m world. Using {limit:F0} m — lower HubEdgeMargin or MaxHouses");

        return Mathf.Max(0f, limit);
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

                float height = _map.SampleWorld(new Vector3(x, 0f, y));

                if (height < min)
                    min = height;

                if (height > max)
                    max = height;
            }
        }

        return max - min;
    }

    private bool IsFlooded(float x, float y)
    {
        if (_config.SeaLevel <= 0f)
            return false;

        return _map.SampleWorldSmooth(x, y) < _config.SeaLevel + _config.ShoreMargin;
    }

    private static bool IsTooClose(List<Hub> hubs, Hub candidate, float minDistanceSqr)
    {
        foreach (Hub hub in hubs)
        {
            if ((hub.Position - candidate.Position).sqrMagnitude < minDistanceSqr)
                return true;
        }

        return false;
    }
}

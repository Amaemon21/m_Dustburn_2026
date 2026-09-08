using System.Collections.Generic;
using UnityEngine;

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

        AssignTiers(hubs);

        if (hubs.Count < _config.HubCount)
            Debug.Log($"{hubs.Count} hubs of the {_config.HubCount} requested. Candidates were culled by MinHubDistance {_config.MinHubDistance} m and by the world border, since a settlement has to fit inside the map. Lower MinHubDistance, MaxHubRadius or CityRadiusScale");

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

                        winner = new Hub(new Vector2(x, y), RadiusFor(relief), relief);
                    }
                }

                if (winner != null)
                    best.Add(winner);
            }
        }

        return best;
    }

    private void AssignTiers(List<Hub> hubs)
    {
        var counts = new int[3];

        for (int i = 0; i < hubs.Count; i++)
        {
            SettlementTier tier = TierFor(i);
            SettlementProfile profile = _config.ProfileFor(tier);

            hubs[i] = new Hub(hubs[i].Position, hubs[i].Radius * profile.RadiusScale, hubs[i].Relief, tier);

            counts[(int)tier]++;
        }

        Debug.Log($"Settlements: {counts[0]} cities, {counts[1]} towns, {counts[2]} villages. Tier follows how flat the site is, the flattest becomes a city");
    }

    private SettlementTier TierFor(int rank)
    {
        if (rank < _config.CityCount)
            return SettlementTier.City;

        return rank < _config.CityCount + _config.TownCount ? SettlementTier.Town : SettlementTier.Village;
    }

    private float EdgeMargin()
    {
        float cityReach = _config.MaxHubRadius * _config.CityRadiusScale * (1f + _config.CityShapeJitter);
        float margin = Mathf.Max(_config.HubEdgeMargin, cityReach);
        float limit = (_config.WorldSize - _config.HubCandidateStep * 4f) * 0.5f;

        if (margin < limit)
            return margin;

        Debug.LogWarning($"HubEdgeMargin {margin:F0} m leaves no room in a {_config.WorldSize} m world. Using {limit:F0} m — lower HubEdgeMargin, MaxHubRadius or CityRadiusScale");

        return Mathf.Max(0f, limit);
    }

    private float RadiusFor(float relief)
    {
        float flatness = 1f - Mathf.Clamp01(relief / Mathf.Max(1f, _config.MaxHubRelief));

        return Mathf.Lerp(_config.MinHubRadius, _config.MaxHubRadius, flatness);
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

                float height = SampleMeters(x, y);

                if (height < min)
                    min = height;

                if (height > max)
                    max = height;
            }
        }

        return max - min;
    }

    private float SampleMeters(float x, float y)
    {
        return _map.SampleWorld(new Vector3(x, 0f, y));
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

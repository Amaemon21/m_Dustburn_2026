using System;
using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class CityPlanner
{
    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;
    private readonly HeightMap _map;
    private readonly LotSubdivider _subdivider;
    private readonly List<Vector2> _piece = new();
    private readonly PoiCensus _world = new();

    private int _replans;
    private int _planned;
    private int _unmet;

    public CityPlanner(WorldGenerationConfig config, PoiDatabase pois, HeightMap map)
    {
        _config = config;
        _pois = pois;
        _map = map;
        _subdivider = new LotSubdivider(config, pois);
    }

    public List<CityLayout> Plan(IReadOnlyList<Hub> hubs, IReadOnlyList<Road> roads)
    {
        var layouts = new List<CityLayout>();

        if (hubs.Count == 0)
        {
            Debug.LogWarning("No settlements planned: there is not a single hub. Run the hubs and roads step first");

            return layouts;
        }

        WarnMissingDistricts();
        WarnComposition();

        _world.Clear();
        _replans = 0;
        _planned = 0;
        _unmet = 0;

        var random = new Random(((uint)_config.Seed | 1u) * 2654435761u + 7u);
        var index = new RoadProximity(roads, _config.WorldSize, _config.RoadCellSize);
        var grower = new StreetGrower(_config, _map, index);
        var stitcher = new StreetStitcher(_config, _map, index);

        int streets = 0;
        int lots = 0;

        foreach (Hub hub in hubs)
        {
            CityLayout layout = PlanCity(hub, roads, index, grower, stitcher, ref random);

            streets += layout.Streets.Count;
            lots += layout.Lots.Count;

            layouts.Add(layout);
        }

        if (lots == 0)
        {
            Debug.LogWarning($"Not a single lot was cut across {hubs.Count} hubs. Raise MaxHubRadius or CityRadiusScale, or lower LotMargin and the footprints in PoiDefinition");

            return layouts;
        }

        Report(streets, lots, grower, stitcher);

        return layouts;
    }

    private CityLayout PlanCity(Hub hub, IReadOnlyList<Road> roads, RoadProximity index, StreetGrower grower, StreetStitcher stitcher, ref Random random)
    {
        uint seed = random.NextUInt() | 1u;
        int attempts = Mathf.Max(1, _config.SettlementPlanAttempts);

        int bestAttempt = 0;
        int bestScore = int.MinValue;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            CityLayout layout = Attempt(hub, roads, index, grower, stitcher, seed, attempt);
            int score = layout.Composition.Met * 10000 + layout.Lots.Count;

            if (score > bestScore)
            {
                bestScore = score;
                bestAttempt = attempt;
            }

            if (layout.Composition.Unmet == 0)
                return Accept(layout);

            Discard(layout, index);
            _replans++;
        }

        return Accept(Attempt(hub, roads, index, grower, stitcher, seed, bestAttempt));
    }

    private CityLayout Accept(CityLayout layout)
    {
        _world.Merge(layout.Composition.Claimed);
        _planned += layout.Composition.Planned;
        _unmet += layout.Composition.Unmet;

        if (layout.Composition.Unmet > 0)
        {
            Debug.LogWarning($"{layout.Profile.Name} at {layout.Hub.Position}: composition short of {layout.Composition.DescribeUnmet()}. "
                + "Raise SettlementPlanAttempts or CityRadiusScale, lower the footprint of that POI, or widen its radius band");
        }

        return layout;
    }

    private CityLayout Attempt(Hub hub, IReadOnlyList<Road> roads, RoadProximity index, StreetGrower grower, StreetStitcher stitcher, uint seed, int attempt)
    {
        var random = new Random((seed + (uint)attempt * 2654435761u) | 1u);

        float angle = OrientationFor(hub, roads, ref random);
        float radius = hub.Radius * _config.CityRadiusScale;

        SettlementProfile profile = _config.ProfileFor(hub.Tier);

        var layout = new CityLayout(hub, angle, radius, profile, new SettlementComposition(profile, _world), _config.CityShapeJitter,
            random.NextFloat(0f, Mathf.PI * 2f), random.NextFloat(0f, Mathf.PI * 2f), random.NextFloat(0f, Mathf.PI * 2f));

        var trunks = new List<Road>();

        foreach (Road road in roads)
            CollectInside(layout, road, trunks);

        grower.Grow(layout, trunks, ref random);
        stitcher.Connect(layout, trunks);

        layout.Frontage.AddRange(trunks);
        layout.Frontage.AddRange(layout.Streets);

        _subdivider.Fill(layout, index, ref random);

        return layout;
    }

    private void Discard(CityLayout layout, RoadProximity index)
    {
        foreach (Road street in layout.Streets)
            index.Remove(street);

        layout.Streets.Clear();
        layout.Frontage.Clear();

        _subdivider.Rollback(layout);
    }

    private void CollectInside(CityLayout layout, Road road, List<Road> pieces)
    {
        if (road.Points == null || road.Points.Length < 2)
            return;

        _piece.Clear();

        foreach (Vector2 point in road.Points)
        {
            if (layout.Contains(point))
            {
                _piece.Add(point);
                continue;
            }

            Flush(road, pieces);
        }

        Flush(road, pieces);
    }

    private void Flush(Road road, List<Road> pieces)
    {
        if (_piece.Count >= 2)
            pieces.Add(new Road(_piece.ToArray(), road.Width));

        _piece.Clear();
    }

    private float OrientationFor(Hub hub, IReadOnlyList<Road> roads, ref Random random)
    {
        float bestDistance = float.MaxValue;
        float angle = random.NextFloat(0f, Mathf.PI);

        foreach (Road road in roads)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            for (int i = 0; i < road.Points.Length - 1; i++)
            {
                float distance = (road.Points[i] - hub.Position).sqrMagnitude;

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;

                Vector2 direction = road.Points[i + 1] - road.Points[i];
                angle = Mathf.Atan2(direction.y, direction.x);
            }
        }

        return angle;
    }

    private void WarnMissingDistricts()
    {
        foreach (DistrictType district in Enum.GetValues(typeof(DistrictType)))
        {
            if (_pois.HasDistrict(district))
                continue;

            Debug.LogWarning($"PoiDatabase has no PoiDefinition for the {district} district: those blocks will stay empty");
        }
    }

    private void WarnComposition()
    {
        foreach (SettlementTier tier in Enum.GetValues(typeof(SettlementTier)))
        {
            SettlementProfile profile = _config.ProfileFor(tier);

            if (profile?.Composition == null)
                continue;

            foreach (PoiRequirement requirement in profile.Composition)
            {
                if (requirement == null || !requirement.IsValid)
                {
                    Debug.LogWarning($"{profile.Name}: a composition row has no PoiDefinition or the prefab is missing, the row is ignored");
                    continue;
                }

                if (!profile.HasDistrict(requirement.Definition.District))
                {
                    Debug.LogWarning($"{profile.Name}: requires {requirement.Definition.name} of the {requirement.Definition.District} district, but this rank has no such zone. "
                        + "Change the district of the POI or the district fractions of the profile");
                }
            }
        }
    }

    private void Report(int streets, int lots, StreetGrower grower, StreetStitcher stitcher)
    {
        Debug.Log($"Settlements: {streets} streets, {lots} lots. Junctions {grower.Junctions}, dead ends stopped by a neighbouring road {grower.DeadEnds}");
        Debug.Log($"Streets: of {grower.Seeded} seeds, {grower.Crowded} dropped as crowded and {grower.TooShort} as too short, {grower.Blocked} ran into terrain or a border");
        Debug.Log($"Connectivity: {stitcher.Islands} detached islands found, {stitcher.Linked} linked by a bridge, {stitcher.Dropped} streets removed as unreachable");
        Debug.Log($"Composition: {_planned - _unmet} of {_planned} required buildings got a lot, {_replans} planning attempts discarded");

        if (_subdivider.SkippedOnRoad > 0)
            Debug.Log($"Lots: {_subdivider.SkippedOnRoad} dropped for covering another road. That is normal around junctions");

        if (_subdivider.SkippedOverlap > 0)
            Debug.Log($"Lots: {_subdivider.SkippedOverlap} dropped for overlapping a neighbour");

        if (_subdivider.SkippedOutside > 0)
            Debug.Log($"Lots: {_subdivider.SkippedOutside} dropped outside the settlement border");
    }
}

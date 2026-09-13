using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class PoiPlacer
{
    private const int FIT_SAMPLES = 4;
    private const float ROAD_PROBE_STEP = 4f;

    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;
    private readonly HeightMap _map;
    private readonly RoadProximity _roads;

    private readonly List<PoiPlacement> _placements = new();
    private readonly List<float> _groundOffsets = new();

    private readonly PoiCensus _world = new();
    private readonly PoiCensus _settlement = new();

    private int _skippedSteep;
    private int _skippedNoFit;
    private int _skippedOnRoad;
    private int _planned;
    private int _short;

    public PoiPlacer(WorldGenerationConfig config, PoiDatabase pois, HeightMap map, RoadProximity roads)
    {
        _config = config;
        _pois = pois;
        _map = map;
        _roads = roads;

        if (_roads == null || _roads.SegmentCount == 0)
            Debug.LogWarning("Road geometry was not passed in: buildings may end up standing on the highway");
    }

    public List<PoiPlacement> Place(IReadOnlyList<SettlementLayout> layouts, RoadNetwork network)
    {
        _placements.Clear();
        _groundOffsets.Clear();
        _world.Clear();

        _skippedSteep = 0;
        _skippedNoFit = 0;
        _skippedOnRoad = 0;
        _planned = 0;
        _short = 0;

        var random = new Random(((uint)_config.Seed | 1u) * 2246822519u + 13u);

        int lots = 0;

        foreach (SettlementLayout layout in layouts)
        {
            lots += layout.Lots.Count;
            PlaceSettlement(layout, ref random);
        }

        int inSettlements = _placements.Count;

        _settlement.Clear();

        PlaceAlongRoads(network, ref random);

        Report(lots, inSettlements);

        return _placements;
    }

    public void ApplyHeights(HeightMap map)
    {
        for (int i = 0; i < _placements.Count; i++)
        {
            Vector2 ground = _placements[i].Ground;

            _placements[i].SetHeight(map.SampleWorld(new Vector3(ground.x, 0f, ground.y)) + _groundOffsets[i]);
        }
    }

    private void PlaceSettlement(SettlementLayout layout, ref Random random)
    {
        _settlement.Clear();

        Vector2 origin = layout.Hub.Position;
        int houses = layout.Hub.Houses;
        int placed = 0;

        layout.Lots.Sort((left, right) => (left.Center - origin).sqrMagnitude.CompareTo((right.Center - origin).sqrMagnitude));

        foreach (Lot lot in layout.Lots)
        {
            if (PlaceOnLot(lot, ref random))
                placed++;
        }

        _planned += houses;
        _short += Mathf.Max(0, houses - placed);
    }

    private bool PlaceOnLot(Lot lot, ref Random random)
    {
        PoiDefinition definition = Pick(lot.District, lot.Width - 2f * _config.LotMargin, lot.Depth - _config.LotSetback - _config.LotMargin, ref random);

        if (definition == null)
        {
            _skippedNoFit++;
            return false;
        }

        float offset = lot.Depth * 0.5f - _config.LotSetback - definition.FootprintDepth * 0.5f;

        Vector2 position = lot.Center + lot.Forward * offset;
        float street = SampleMeters(lot.FrontEdge);

        if (IsOnRoad(position, definition.Footprint, lot.Forward))
        {
            _skippedOnRoad++;
            return false;
        }

        if (!Fits(position, definition.Footprint, lot.Forward, street))
        {
            _skippedSteep++;
            return false;
        }

        Add(definition, position, lot.Forward, lot.District);

        return true;
    }

    private void PlaceAlongRoads(RoadNetwork network, ref Random random)
    {
        if (!_pois.HasDistrict(DistrictType.Rural))
            return;

        foreach (Road road in network.Roads)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            float travelled = 0f;
            float nextAt = _config.RuralSpacing;
            float side = 1f;

            for (int i = 0; i < road.Points.Length - 1; i++)
            {
                Vector2 from = road.Points[i];
                Vector2 to = road.Points[i + 1];

                float segment = Vector2.Distance(from, to);

                if (segment <= Mathf.Epsilon)
                    continue;

                travelled += segment;

                if (travelled < nextAt)
                    continue;

                nextAt = travelled + _config.RuralSpacing;
                side = -side;

                if (random.NextFloat() > _config.RuralChance)
                    continue;

                Vector2 direction = (to - from) / segment;
                Vector2 normal = new(-direction.y, direction.x);

                Vector2 position = to + normal * (_config.RuralOffset * side);

                if (IsInsideSettlement(network, position))
                    continue;

                PoiDefinition definition = Pick(DistrictType.Rural, float.MaxValue, float.MaxValue, ref random);

                if (definition == null)
                    continue;

                Vector2 forward = -normal * side;
                float street = SampleMeters(to);

                if (IsOnRoad(position, definition.Footprint, forward))
                {
                    _skippedOnRoad++;
                    continue;
                }

                if (!Fits(position, definition.Footprint, forward, street))
                {
                    _skippedSteep++;
                    continue;
                }

                Add(definition, position, forward, DistrictType.Rural);
            }
        }
    }

    private void Add(PoiDefinition definition, Vector2 position, Vector2 forward, DistrictType district)
    {
        float rotation = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;

        _placements.Add(new PoiPlacement(definition.Prefab, position, rotation, definition.Footprint, district, definition.PivotOffset));
        _groundOffsets.Add(definition.GroundOffset);

        _world.Add(definition);
        _settlement.Add(definition);
    }

    private bool IsInsideSettlement(RoadNetwork network, Vector2 position)
    {
        foreach (Hub hub in network.Hubs)
        {
            float radius = hub.Radius * _config.RuralClearance;

            if ((position - hub.Position).sqrMagnitude < radius * radius)
                return true;
        }

        return false;
    }

    private bool Fits(Vector2 center, Vector2 footprint, Vector2 forward, float street)
    {
        Vector2 right = new(forward.y, -forward.x);
        float pad = 2f * Mathf.Max(_config.PoiPadMargin, _config.HeightCellSize);

        for (int i = 0; i < FIT_SAMPLES; i++)
        {
            for (int j = 0; j < FIT_SAMPLES; j++)
            {
                float u = (i / (FIT_SAMPLES - 1f) - 0.5f) * (footprint.x + pad);
                float v = (j / (FIT_SAMPLES - 1f) - 0.5f) * (footprint.y + pad);

                Vector2 point = center + right * u + forward * v;

                if (point.x < 0f || point.y < 0f || point.x > _config.WorldSize || point.y > _config.WorldSize)
                    return false;

                float ground = SampleMeters(point);

                if (_config.SeaLevel > 0f && ground < _config.SeaLevel + _config.ShoreMargin)
                    return false;

                if (ground - street > _config.MaxPoiCut)
                    return false;

                if (street - ground > _config.MaxPoiFill)
                    return false;
            }
        }

        return true;
    }

    private bool IsOnRoad(Vector2 center, Vector2 footprint, Vector2 forward)
    {
        if (_roads == null)
            return false;

        float clearance = _config.LotFrontGap * 0.5f;

        Vector2 right = new(forward.y, -forward.x);

        int alongSteps = Mathf.CeilToInt(footprint.x / ROAD_PROBE_STEP);
        int acrossSteps = Mathf.CeilToInt(footprint.y / ROAD_PROBE_STEP);

        for (int i = 0; i <= alongSteps; i++)
        {
            for (int j = 0; j <= acrossSteps; j++)
            {
                float u = (i / (float)alongSteps - 0.5f) * footprint.x;
                float v = (j / (float)acrossSteps - 0.5f) * footprint.y;

                if (_roads.IsWithin(center + right * u + forward * v, clearance))
                    return true;
            }
        }

        return false;
    }

    private PoiDefinition Pick(DistrictType district, float maxWidth, float maxDepth, ref Random random)
    {
        float total = 0f;

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (!Accepts(definition, district, maxWidth, maxDepth))
                continue;

            total += Score(definition);
        }

        if (total <= 0f)
            return null;

        float roll = random.NextFloat(0f, total);

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (!Accepts(definition, district, maxWidth, maxDepth))
                continue;

            roll -= Score(definition);

            if (roll <= 0f)
                return definition;
        }

        return null;
    }

    private bool Accepts(PoiDefinition definition, DistrictType district, float maxWidth, float maxDepth)
    {
        if (definition == null || definition.District != district)
            return false;

        if (definition.FootprintWidth > maxWidth || definition.FootprintDepth > maxDepth)
            return false;

        return HasRoom(definition);
    }

    private bool HasRoom(PoiDefinition definition)
    {
        if (definition.MaxPerWorld > 0 && _world.Count(definition) >= definition.MaxPerWorld)
            return false;

        return definition.MaxPerSettlement <= 0 || _settlement.Count(definition) < definition.MaxPerSettlement;
    }

    private static float Score(PoiDefinition definition)
    {
        return definition.Weight * definition.FootprintArea;
    }

    private float SampleMeters(Vector2 position)
    {
        return _map.SampleWorld(new Vector3(position.x, 0f, position.y));
    }

    private void Report(int lots, int inSettlements)
    {
        if (_placements.Count == 0)
        {
            Debug.LogWarning($"Not a single POI was placed across {lots} lots. Check the PoiDefinition footprints against BlockSizeMin and raise MaxPoiCut and MaxPoiFill");

            return;
        }

        Debug.Log($"POI: {_placements.Count} placed, {inSettlements} houses in settlements against {_planned} drawn, {_placements.Count - inSettlements} along roads");

        if (_short > 0)
            Debug.Log($"POI: settlements came {_short} houses short of their draw. A settlement boxed in by steep ground, water or a neighbour runs out of blocks — raise MaxBlockRelief or lower SettlementGap");

        if (_skippedNoFit > 0)
            Debug.Log($"POI: {_skippedNoFit} lots stayed empty, no prefab of the district fitted. Add a smaller PoiDefinition, lower LotMargin or raise MaxPerSettlement");

        if (_skippedSteep > 0)
            Debug.Log($"POI: {_skippedSteep} lots dropped by the terrain. Raise MaxPoiCut and MaxPoiFill or SettlementSmoothing");

        if (_skippedOnRoad > 0)
            Debug.Log($"POI: {_skippedOnRoad} buildings dropped for landing on a road surface");
    }
}

using System.Collections.Generic;
using UnityEngine;

public sealed partial class PropPlacer
{
    private const float YARD_GAP = 0.5f;
    private const float YARD_ROAD_CLEARANCE = 0.75f;
    private const float FRONT_SHARE = 0.15f;
    private const float WILD_ROAD_CLEARANCE = 6f;
    private const float SETTLEMENT_REACH = 0.75f;

    private void PlaceYards()
    {
        if (_byContext[PropContext.Yard].Definitions.Count == 0)
            return;

        foreach (PoiPlacement poi in _pois)
        {
            int wanted = _random.NextInt(_database.YardMin, _database.YardMax + 1);
            int placed = 0;

            for (int attempt = 0; attempt < wanted * ATTEMPTS_PER_PROP && placed < wanted; attempt++)
            {
                if (!TryPick(PropContext.Yard, out PropDefinition definition))
                    return;

                Vector2 point = YardPoint(poi, definition.Radius);
                float yaw = _random.NextFloat() * 360f;

                if (!TryAccept(definition, point, true, YARD_ROAD_CLEARANCE, out float ground))
                    continue;

                Commit(definition, PropContext.Yard, point, ground, yaw);
                PlaceCluster(definition, PropContext.Yard, point, yaw, true, YARD_ROAD_CLEARANCE);
                placed++;
            }
        }
    }

    private Vector2 YardPoint(PoiPlacement poi, float radius)
    {
        Vector2 forward = poi.Forward;
        Vector2 right = new(forward.y, -forward.x);
        float halfWidth = poi.Footprint.x * 0.5f;
        float halfDepth = poi.Footprint.y * 0.5f;
        float gap = _config.PoiPadMargin + radius + YARD_GAP + _random.NextFloat() * _database.YardDepth;
        float roll = _random.NextFloat();
        float along = _random.NextFloat() * 2f - 1f;

        Vector2 local;

        if (roll < FRONT_SHARE)
            local = new Vector2(along * halfWidth, halfDepth + gap);
        else if (roll < (1f + FRONT_SHARE) / 3f)
            local = new Vector2(-(halfWidth + gap), along * halfDepth);
        else if (roll < (2f + FRONT_SHARE) / 3f)
            local = new Vector2(halfWidth + gap, along * halfDepth);
        else
            local = new Vector2(along * halfWidth, -(halfDepth + gap));

        return poi.Ground + right * local.x + forward * local.y;
    }

    private void PlaceWild()
    {
        if (_byContext[PropContext.Wild].Definitions.Count == 0 || _database.WildPerKm2 <= 0f)
            return;

        float area = _config.WorldSize / 1000f * (_config.WorldSize / 1000f);
        int wanted = Mathf.RoundToInt(area * _database.WildPerKm2);
        List<HashSet<long>> tiles = SettlementTiles();
        int placed = 0;

        for (int attempt = 0; attempt < wanted * ATTEMPTS_PER_PROP && placed < wanted; attempt++)
        {
            if (!TryPick(PropContext.Wild, out PropDefinition definition))
                return;

            Vector2 point = new(_random.NextFloat() * _config.WorldSize, _random.NextFloat() * _config.WorldSize);
            float yaw = _random.NextFloat() * 360f;

            if (InSettlement(point, tiles))
            {
                _refusedPad++;
                continue;
            }

            if (!TryAccept(definition, point, true, WILD_ROAD_CLEARANCE, out float ground))
                continue;

            Commit(definition, PropContext.Wild, point, ground, yaw);
            PlaceCluster(definition, PropContext.Wild, point, yaw, true, WILD_ROAD_CLEARANCE);
            placed++;
        }
    }

    private List<HashSet<long>> SettlementTiles()
    {
        var tiles = new List<HashSet<long>>();

        foreach (SettlementRecord settlement in _network.Settlements)
        {
            var set = new HashSet<long>();
            foreach (TileRecord tile in settlement.Tiles)
                set.Add(Key(tile.I, tile.J));
            tiles.Add(set);
        }

        return tiles;
    }

    private bool InSettlement(Vector2 point, List<HashSet<long>> tiles)
    {
        for (int index = 0; index < _network.Settlements.Count; index++)
        {
            SettlementRecord settlement = _network.Settlements[index];
            Vector2 local = point - settlement.Origin;
            float u = Vector2.Dot(local, settlement.AxisU) / settlement.TileSize;
            float v = Vector2.Dot(local, settlement.AxisV) / settlement.TileSize;
            int i = Mathf.RoundToInt(u);
            int j = Mathf.RoundToInt(v);

            for (int ti = i - 1; ti <= i + 1; ti++)
                for (int tj = j - 1; tj <= j + 1; tj++)
                    if (Mathf.Abs(u - ti) <= SETTLEMENT_REACH && Mathf.Abs(v - tj) <= SETTLEMENT_REACH && tiles[index].Contains(Key(ti, tj)))
                        return true;
        }

        return false;
    }
}

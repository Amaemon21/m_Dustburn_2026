using System.Collections.Generic;
using UnityEngine;

public sealed partial class TileStreetBuilder
{
    private void AddHalfFrontages(SettlementLayout layout, HalfStreet half, SettlementTypeProfile profile)
    {
        RoadKindProfile road = RoadKindProfile.For(_config, half.Kind);
        Vector2 unit = (half.Port - half.Center).normalized;
        Vector2 left = new(-unit.y, unit.x);
        float halfTile = layout.TileSize * 0.5f;
        float centerTrim = CenterTrim(half.Tile);
        float depth = halfTile - road.HalfWidth - _config.LotFrontGap;
        float density = Density(half.Tile.District, profile);

        foreach (float sign in new[] { 1f, -1f })
        {
            Vector2 normal = left * sign;
            TilePorts sideTowards = SideOf(layout, normal);
            var cuts = new List<(float From, float To)> { (centerTrim, halfTile) };

            foreach (float anchor in half.Anchors)
            {
                if (Rotate(half.Side) != sideTowards)
                    continue;

                float trim = _config.StreetHalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
                cuts = Cut(cuts, anchor - trim, anchor + trim);
            }

            foreach ((float from, float to) in cuts)
            {
                if (to - from < MIN_FRONTAGE)
                    continue;

                Vector2 start = half.Center + unit * from;
                Vector2 end = half.Center + unit * to;

                layout.Frontages.Add(new Frontage(start, end, normal, road.HalfWidth, half.Tile.District, depth, density, half.Kind));
            }
        }
    }

    private void AddCourtFrontages(SettlementLayout layout, Court court, SettlementTypeProfile profile)
    {
        Vector2 hostUnit = (court.Host.Port - court.Host.Center).normalized;
        Vector2 anchor = court.Host.Center + hostUnit * court.Along;
        Vector2 direction = TilePortRules.Direction(court.Toward, layout.AxisU, layout.AxisV);
        RoadKindProfile host = RoadKindProfile.For(_config, court.Host.Kind);
        float start = host.HalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
        float halfTile = layout.TileSize * 0.5f;
        float depthTowardCenter = court.Along - _config.StreetHalfWidth - _config.LotFrontGap;
        float depthTowardEdge = halfTile - court.Along - _config.StreetHalfWidth - _config.LotFrontGap;
        float density = Density(court.Host.Tile.District, profile);

        if (court.Length - start < MIN_FRONTAGE)
            return;

        Vector2 from = anchor + direction * start;
        Vector2 to = anchor + direction * court.Length;

        layout.Frontages.Add(new Frontage(from, to, -hostUnit, _config.StreetHalfWidth, court.Host.Tile.District, depthTowardCenter, density, RoadKind.LocalStreet));
        layout.Frontages.Add(new Frontage(from, to, hostUnit, _config.StreetHalfWidth, court.Host.Tile.District, depthTowardEdge, density, RoadKind.LocalStreet));
    }

    private float CenterTrim(SettlementTile tile)
    {
        switch (tile.Shape)
        {
            case TileShape.Tee:
            case TileShape.Intersection:
                return _config.ArterialHalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
            case TileShape.Corner:
                return _config.StreetCornerRadius + CORNER_CLEARANCE;
            default:
                return 0f;
        }
    }

    private static List<(float From, float To)> Cut(List<(float From, float To)> ranges, float from, float to)
    {
        var result = new List<(float, float)>();

        foreach ((float start, float end) in ranges)
        {
            if (to <= start || from >= end)
            {
                result.Add((start, end));
                continue;
            }

            if (from > start)
                result.Add((start, from));

            if (to < end)
                result.Add((to, end));
        }

        return result;
    }

    private static TilePorts SideOf(SettlementLayout layout, Vector2 normal)
    {
        float u = Vector2.Dot(normal, layout.AxisU);
        float v = Vector2.Dot(normal, layout.AxisV);

        return TilePortRules.Nearest(Mathf.Atan2(v, u));
    }

    private static TilePorts Rotate(TilePorts side)
    {
        return side switch
        {
            TilePorts.East => TilePorts.North,
            TilePorts.North => TilePorts.West,
            TilePorts.West => TilePorts.South,
            _ => TilePorts.East
        };
    }

    private static float Density(DistrictType district, SettlementTypeProfile profile)
    {
        float factor = district switch
        {
            DistrictType.Residential => 0.9f,
            DistrictType.Industrial => 0.8f,
            DistrictType.Rural => 0.45f,
            _ => 1f
        };

        return Mathf.Clamp01(profile.PoiDensity * factor);
    }
}

using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class LotSubdivider
{
    private const float OVERLAP_SHRINK = 0.94f;

    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;

    private readonly LotIndex _lots;

    public int SkippedOverlap { get; private set; }
    public int SkippedOnRoad { get; private set; }
    public int SkippedOutside { get; private set; }

    public LotSubdivider(WorldGenerationConfig config, PoiDatabase pois)
    {
        _config = config;
        _pois = pois;
        _lots = new LotIndex(config.WorldSize, 48f);
    }

    public void Fill(CityLayout layout, RoadProximity roads, ref Random random)
    {
        foreach (Road line in layout.Frontage)
        {
            Walk(layout, line, 1f, roads, ref random);
            Walk(layout, line, -1f, roads, ref random);
        }
    }

    private void Walk(CityLayout layout, Road line, float side, RoadProximity roads, ref Random random)
    {
        if (line.Points == null || line.Points.Length < 2)
            return;

        float halfWidth = line.Width * 0.5f;
        float total = Length(line.Points);
        float cursor = _config.LotGap;

        while (cursor < total)
        {
            Sample(line.Points, cursor, out Vector2 anchor, out Vector2 tangent);

            Vector2 normal = new Vector2(-tangent.y, tangent.x) * side;

            if (TryLot(layout, anchor, tangent, normal, halfWidth, roads, ref random, out Lot lot, out float step))
            {
                layout.Lots.Add(lot);
                _lots.Add(lot);
            }

            cursor += step;
        }
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 0; i < points.Length - 1; i++)
            total += Vector2.Distance(points[i], points[i + 1]);

        return total;
    }

    private static void Sample(Vector2[] points, float distance, out Vector2 position, out Vector2 tangent)
    {
        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);

            if (length <= Mathf.Epsilon)
                continue;

            tangent = (points[i + 1] - points[i]) / length;

            if (distance <= length)
            {
                position = points[i] + tangent * distance;
                return;
            }

            distance -= length;
        }

        position = points[^1];
        tangent = (points[^1] - points[^2]).normalized;
    }

    public void Rollback(CityLayout layout)
    {
        foreach (Lot lot in layout.Lots)
            _lots.Remove(lot);

        layout.Lots.Clear();
    }

    private bool TryLot(CityLayout layout, Vector2 anchor, Vector2 tangent, Vector2 normal, float halfWidth,
        RoadProximity roads, ref Random random, out Lot lot, out float step)
    {
        lot = null;
        step = _config.LotProbeStep;

        DistrictType district = layout.DistrictAt(anchor);
        SettlementComposition composition = layout.Composition;

        PoiRequirement claim = composition?.Claim(district, layout.NormalizedDistance(anchor), anchor, layout.BlockSizeAt(anchor));
        PoiDefinition definition = claim?.Definition ?? Sample(district, ref random);

        if (definition == null)
        {
            composition?.Return(claim);
            return false;
        }

        SettlementProfile profile = layout.Profile;

        float width = definition.FootprintWidth + 2f * profile.LotMargin;
        float depth = definition.FootprintDepth + _config.LotSetback + profile.LotMargin;

        float offset = halfWidth + _config.LotFrontGap + depth * 0.5f;

        Vector2 center = anchor + tangent * (width * 0.5f) + normal * offset;
        var candidate = new Lot(center, width, depth, -normal, district, claim?.Definition);

        if (!IsInside(layout, candidate))
        {
            composition?.Return(claim);
            SkippedOutside++;
            return false;
        }

        if (TouchesRoad(candidate, roads))
        {
            composition?.Return(claim);
            SkippedOnRoad++;
            return false;
        }

        if (_lots.Overlaps(candidate, OVERLAP_SHRINK))
        {
            composition?.Return(claim);
            SkippedOverlap++;
            return false;
        }

        composition?.Confirm(claim, anchor);

        lot = candidate;
        step = width + _config.LotGap * random.NextFloat(0.5f, 2f);

        return true;
    }

    private bool TouchesRoad(Lot lot, RoadProximity roads)
    {
        Vector2 right = lot.Right * (lot.Width * 0.5f);
        Vector2 forward = lot.Forward * (lot.Depth * 0.5f);

        float clearance = _config.LotFrontGap * 0.5f;

        for (int i = -2; i <= 2; i++)
        {
            for (int j = -2; j <= 2; j++)
            {
                Vector2 point = lot.Center + right * (i * 0.5f) + forward * (j * 0.5f);

                if (roads.IsWithin(point, clearance))
                    return true;
            }
        }

        return false;
    }

    private bool IsInside(CityLayout layout, Lot lot)
    {
        Vector2 right = lot.Right * (lot.Width * 0.5f);
        Vector2 forward = lot.Forward * (lot.Depth * 0.5f);

        for (int i = -1; i <= 1; i += 2)
        {
            for (int j = -1; j <= 1; j += 2)
            {
                Vector2 corner = lot.Center + right * i + forward * j;

                if (!layout.Contains(corner))
                    return false;

                if (corner.x < 0f || corner.y < 0f || corner.x > _config.WorldSize || corner.y > _config.WorldSize)
                    return false;
            }
        }

        return true;
    }

    private PoiDefinition Sample(DistrictType district, ref Random random)
    {
        float total = 0f;

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (definition != null && definition.District == district)
                total += definition.Weight;
        }

        if (total <= 0f)
            return null;

        float roll = random.NextFloat(0f, total);

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (definition == null || definition.District != district)
                continue;

            roll -= definition.Weight;

            if (roll <= 0f)
                return definition;
        }

        return null;
    }
}

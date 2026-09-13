using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class LotSubdivider
{
    private const float OVERLAP_SHRINK = 0.94f;

    private static readonly DistrictType[] DOWNTOWN_FALLBACK = { DistrictType.Downtown, DistrictType.Commercial, DistrictType.Residential };
    private static readonly DistrictType[] COMMERCIAL_FALLBACK = { DistrictType.Commercial, DistrictType.Downtown, DistrictType.Residential };
    private static readonly DistrictType[] INDUSTRIAL_FALLBACK = { DistrictType.Industrial, DistrictType.Residential };
    private static readonly DistrictType[] RURAL_FALLBACK = { DistrictType.Rural, DistrictType.Residential };
    private static readonly DistrictType[] RESIDENTIAL_FALLBACK = { DistrictType.Residential };

    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;

    private readonly LotIndex _lots;

    public int SkippedOverlap { get; private set; }
    public int SkippedOnRoad { get; private set; }
    public int SkippedOutside { get; private set; }
    public int SkippedNoPrefab { get; private set; }
    public int SkippedDepth { get; private set; }
    public int SkippedDensity { get; private set; }

    public LotSubdivider(WorldGenerationConfig config, PoiDatabase pois)
    {
        _config = config;
        _pois = pois;
        _lots = new LotIndex(config.WorldSize, 48f);
    }

    public static IReadOnlyList<DistrictType> Fallback(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => DOWNTOWN_FALLBACK,
            DistrictType.Commercial => COMMERCIAL_FALLBACK,
            DistrictType.Industrial => INDUSTRIAL_FALLBACK,
            DistrictType.Rural => RURAL_FALLBACK,
            _ => RESIDENTIAL_FALLBACK
        };
    }

    public void Fill(SettlementLayout layout, RoadProximity roads, ref Random random)
    {
        var order = new List<Frontage>(layout.Frontages);
        var rank = new Dictionary<Frontage, int>(order.Count);

        for (int i = 0; i < order.Count; i++)
            rank[order[i]] = i;

        order.Sort((left, right) =>
        {
            int compare = Priority(left.Kind).CompareTo(Priority(right.Kind));
            return compare != 0 ? compare : rank[left].CompareTo(rank[right]);
        });

        foreach (Frontage frontage in order)
            Walk(layout, frontage, roads, ref random);
    }

    private static int Priority(RoadKind kind)
    {
        return kind == RoadKind.Arterial ? 0 : 1;
    }

    private void Walk(SettlementLayout layout, Frontage frontage, RoadProximity roads, ref Random random)
    {
        Vector2 line = frontage.To - frontage.From;
        float length = line.magnitude;

        if (length <= Mathf.Epsilon)
            return;

        Vector2 tangent = line / length;
        float cursor = _config.LotGap * 0.5f;

        while (cursor < length)
        {
            Vector2 anchor = frontage.From + tangent * cursor;

            if (random.NextFloat() > frontage.Density)
            {
                SkippedDensity++;
                cursor += _config.LotProbeStep + _config.LotGap * random.NextFloat(1f, 3f);
                continue;
            }

            if (TryLot(frontage, anchor, tangent, length - cursor, roads, ref random, out Lot lot, out float step))
            {
                layout.Lots.Add(lot);
                _lots.Add(lot);
            }

            cursor += step;
        }
    }

    private bool TryLot(Frontage frontage, Vector2 anchor, Vector2 tangent, float remaining, RoadProximity roads, ref Random random, out Lot lot, out float step)
    {
        lot = null;
        step = _config.LotProbeStep;

        PoiDefinition definition = null;
        DistrictType district = frontage.District;

        foreach (DistrictType candidate in Fallback(frontage.District))
        {
            definition = Sample(candidate, frontage.MaxDepth, ref random);

            if (definition == null)
                continue;

            district = candidate;
            break;
        }

        if (definition == null)
        {
            if (_pois.HasDistrict(frontage.District))
                SkippedDepth++;
            else
                SkippedNoPrefab++;

            return false;
        }

        float width = definition.FootprintWidth + 2f * _config.LotMargin;
        float depth = definition.FootprintDepth + _config.LotSetback + _config.LotMargin;

        if (width > remaining + _config.LotGap)
            return false;

        float offset = frontage.HalfWidth + _config.LotFrontGap + depth * 0.5f;

        Vector2 center = anchor + tangent * (width * 0.5f) + frontage.Normal * offset;
        var candidateLot = new Lot(center, width, depth, -frontage.Normal, district);

        if (!IsInsideWorld(candidateLot))
        {
            SkippedOutside++;
            return false;
        }

        if (roads != null && TouchesRoad(candidateLot, roads))
        {
            SkippedOnRoad++;
            return false;
        }

        if (_lots.Overlaps(candidateLot, OVERLAP_SHRINK))
        {
            SkippedOverlap++;
            return false;
        }

        lot = candidateLot;
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

    private bool IsInsideWorld(Lot lot)
    {
        Vector2 right = lot.Right * (lot.Width * 0.5f);
        Vector2 forward = lot.Forward * (lot.Depth * 0.5f);

        for (int i = -1; i <= 1; i += 2)
        {
            for (int j = -1; j <= 1; j += 2)
            {
                Vector2 corner = lot.Center + right * i + forward * j;

                if (corner.x < 0f || corner.y < 0f || corner.x > _config.WorldSize || corner.y > _config.WorldSize)
                    return false;
            }
        }

        return true;
    }

    private PoiDefinition Sample(DistrictType district, float maxDepth, ref Random random)
    {
        float total = 0f;

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (Fits(definition, district, maxDepth))
                total += definition.Weight;
        }

        if (total <= 0f)
            return null;

        float roll = random.NextFloat(0f, total);

        foreach (PoiDefinition definition in _pois.Definitions)
        {
            if (!Fits(definition, district, maxDepth))
                continue;

            roll -= definition.Weight;

            if (roll <= 0f)
                return definition;
        }

        return null;
    }

    private bool Fits(PoiDefinition definition, DistrictType district, float maxDepth)
    {
        return definition != null && definition.District == district
            && definition.FootprintDepth + _config.LotSetback + _config.LotMargin <= maxDepth;
    }
}

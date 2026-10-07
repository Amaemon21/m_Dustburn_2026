using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed partial class TileStreetBuilder
{
    private List<Court> PlanCourts(SettlementLayout layout, List<HalfStreet> halves, SettlementTypeProfile profile, ref Random random)
    {
        var courts = new List<Court>();
        float half = layout.TileSize * 0.5f;
        float along = layout.TileSize * COURT_ANCHOR;
        float limit = half - _config.StreetHalfWidth - _config.LotFrontGap - CORNER_CLEARANCE;
        float length = Mathf.Min(_config.CourtDepth, limit);
        float typeChance = TypeCourtChance(layout.Type);

        Courts = 0;

        if (length < MIN_FRONTAGE || typeChance <= 0f)
            return courts;

        var industrialTaken = new HashSet<SettlementTile>();

        foreach (HalfStreet host in halves)
        {
            float chance = DistrictCourtChance(host.Tile.District) * typeChance;
            float roll = random.NextFloat();

            if (roll >= chance)
                continue;

            if (host.Tile.District == DistrictType.Industrial && !industrialTaken.Add(host.Tile))
                continue;

            TilePorts toward = Rotate(host.Side);

            host.Anchors.Add(along);
            courts.Add(new Court(host, along, toward, host.Tile.District == DistrictType.Industrial ? limit : length));
            Courts++;
        }

        return courts;
    }

    private void AddCourt(SettlementLayout layout, Court court)
    {
        Vector2 unit = (court.Host.Port - court.Host.Center).normalized;
        Vector2 anchor = court.Host.Center + unit * court.Along;
        Vector2 direction = TilePortRules.Direction(court.Toward, layout.AxisU, layout.AxisV);
        Vector2 end = anchor + direction * court.Length;

        AddNode(layout, anchor);
        AddNode(layout, end);

        layout.Streets.Add(RoadKindProfile.Create(_config, new[] { anchor, end }, RoadKind.LocalStreet));
    }

    private static float TypeCourtChance(SettlementType type)
    {
        return type switch
        {
            SettlementType.City => 1f,
            SettlementType.Town => 0.8f,
            SettlementType.CountryTown => 0.35f,
            _ => 0f
        };
    }

    private static float DistrictCourtChance(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => 1f,
            DistrictType.Commercial => 0.5f,
            DistrictType.Residential => 0.6f,
            DistrictType.Industrial => 0.5f,
            _ => 0f
        };
    }
}

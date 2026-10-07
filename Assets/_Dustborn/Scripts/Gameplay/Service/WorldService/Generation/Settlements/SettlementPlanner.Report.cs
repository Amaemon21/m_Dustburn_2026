using System.Collections.Generic;
using UnityEngine;

public partial class SettlementPlanner
{
    private void WarnMissingDistricts()
    {
        foreach (DistrictType district in new[] { DistrictType.Downtown, DistrictType.Commercial, DistrictType.Residential, DistrictType.Industrial })
        {
            if (_pois.HasDistrict(district))
                continue;

            Debug.LogWarning($"PoiDatabase has no PoiDefinition for the {district} district: those tiles fall back to other buildings");
        }

        if (!_pois.HasDistrict(DistrictType.Rural))
            Debug.LogWarning("PoiDatabase has no Rural PoiDefinition: outskirts and dirt spurs stay empty");
    }

    private void Report(List<SettlementLayout> layouts)
    {
        var counts = new int[4];
        var tiles = new int[4];
        int gateways = 0;
        int streets = 0;
        int empty = 0;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout.IsEmpty)
            {
                empty++;
                continue;
            }

            counts[(int)layout.Type]++;
            tiles[(int)layout.Type] += layout.Tiles.Count;
            gateways += layout.Gateways.Count;
            streets += layout.Streets.Count;
        }

        Debug.Log($"Settlements: {counts[0]} cities ({tiles[0]} tiles), {counts[1]} towns ({tiles[1]}), {counts[2]} country towns ({tiles[2]}), {counts[3]} ghost towns ({tiles[3]}), {empty} without a buildable core; {gateways} gateways, {streets} streets, {Courts} courts, {MergedGateways} links sharing a gateway, {TopologyViolations} topology rule violations");
        Debug.Log($"Tiles refused: {RefusedSteep} steeper than MaxTileRelief {_config.MaxTileRelief} m, {RefusedFlooded} under water, {RefusedCrowded} inside SettlementGap {_config.SettlementGap} m of a neighbour, {RefusedOutside} past the world border");
    }
}

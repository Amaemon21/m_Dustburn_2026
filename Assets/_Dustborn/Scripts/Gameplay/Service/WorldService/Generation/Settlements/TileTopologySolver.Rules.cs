using UnityEngine;

public sealed partial class TileTopologySolver
{
    private static int MaxPorts(SettlementLayout layout, SettlementTile tile, SettlementTypeProfile profile)
    {
        int limit = tile.District switch
        {
            DistrictType.Rural => 2,
            DistrictType.Industrial => 3,
            _ => 4
        };

        if (profile.Shape == SettlementShape.Linear)
            limit = Mathf.Min(limit, 2);

        if (profile.Shape == SettlementShape.Cross && !(tile.I == 0 && tile.J == 0))
            limit = Mathf.Min(limit, 3);

        return Mathf.Max(limit, TilePortRules.Count(tile.ArterialPorts | tile.GatewayPorts));
    }

    private int DeadEndAllowance(SettlementLayout layout)
    {
        return Mathf.Max(2, Mathf.FloorToInt(_config.DeadEndShare * layout.Tiles.Count));
    }

    private static bool IsGridded(SettlementLayout layout)
    {
        return layout.Type == SettlementType.City || layout.Type == SettlementType.Town;
    }

    private static bool IsDense(DistrictType district)
    {
        return district == DistrictType.Downtown || district == DistrictType.Commercial;
    }

    private static DistrictType Sparser(DistrictType a, DistrictType b)
    {
        return LoopFactor(a) <= LoopFactor(b) ? a : b;
    }

    private static float LoopFactor(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => 1.6f,
            DistrictType.Commercial => 1.3f,
            DistrictType.Industrial => 0.7f,
            DistrictType.Rural => 0.25f,
            _ => 1f
        };
    }
}

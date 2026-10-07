using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial class SettlementPlanner
{
    private static void Zone(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, List<GatewaySpec> specs, ref Random random)
    {
        int count = layout.Tiles.Count;
        var distance = new Dictionary<SettlementTile, float>(count);

        foreach (SettlementTile tile in layout.Tiles)
        {
            int du = tile.I - core.I;
            int dv = tile.J - core.J;

            distance[tile] = du * du + dv * dv + ZONE_NOISE * random.NextFloat();
            tile.District = DistrictType.Residential;
        }

        var free = new List<SettlementTile>(layout.Tiles);

        free.Sort((left, right) => distance[left].CompareTo(distance[right]));
        Take(free, Share(count, profile.DowntownShare), DistrictType.Downtown);

        free.Sort((left, right) => (distance[left] * (left.IsArterial ? 0.5f : 1f)).CompareTo(distance[right] * (right.IsArterial ? 0.5f : 1f)));
        Take(free, Share(count, profile.CommercialShare), DistrictType.Commercial);

        int industrial = Share(count, profile.IndustrialShare);

        if (industrial > 0)
        {
            Vector2 direction = specs.Count > 0
                ? TilePortRules.Direction(specs[random.NextInt(specs.Count)].Side, layout.AxisU, layout.AxisV)
                : new Vector2(Mathf.Cos(random.NextFloat(0f, Mathf.PI * 2f)), Mathf.Sin(random.NextFloat(0f, Mathf.PI * 2f)));

            free.Sort((left, right) => Vector2.Dot(right.Center - core.Center, direction).CompareTo(Vector2.Dot(left.Center - core.Center, direction)));
            Take(free, industrial, DistrictType.Industrial);
        }

        free.RemoveAll(tile => Occupied(layout, tile) > 2);
        free.Sort((left, right) => distance[right].CompareTo(distance[left]));
        Take(free, Share(count, profile.RuralShare), DistrictType.Rural);
    }

    private static void Take(List<SettlementTile> free, int count, DistrictType district)
    {
        int taken = Mathf.Min(count, free.Count);

        for (int i = 0; i < taken; i++)
            free[i].District = district;

        free.RemoveRange(0, taken);
    }

    private static int Share(int count, float share)
    {
        if (share <= 0f)
            return 0;

        return Mathf.Max(count >= 3 ? 1 : 0, Mathf.RoundToInt(count * share));
    }

    private static int Occupied(SettlementLayout layout, SettlementTile tile)
    {
        int neighbours = 0;

        foreach (TilePorts side in TilePortRules.SIDES)
        {
            if (layout.Neighbour(tile, side) != null)
                neighbours++;
        }

        return neighbours;
    }
}

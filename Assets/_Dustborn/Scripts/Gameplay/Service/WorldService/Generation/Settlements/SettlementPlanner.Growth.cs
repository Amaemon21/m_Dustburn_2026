using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial class SettlementPlanner
{
    private void LayArm(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core,
        GatewaySpec spec, HashSet<long> blocked)
    {
        int stepI = TilePortRules.StepI(spec.Side);
        int stepJ = TilePortRules.StepJ(spec.Side);
        bool alongU = stepI != 0;
        TilePorts offsetSide = alongU
            ? (spec.Offset > 0 ? TilePorts.North : TilePorts.South)
            : (spec.Offset > 0 ? TilePorts.East : TilePorts.West);

        SettlementTile current = core;

        for (int k = 0; k < Mathf.Abs(spec.Offset); k++)
        {
            SettlementTile next = Occupy(hubs, layout, current, offsetSide, blocked);

            if (next == null)
                break;

            current = next;
        }

        for (int k = 0; k < Mathf.Max(0, profile.ArmTiles); k++)
        {
            SettlementTile next = Occupy(hubs, layout, current, spec.Side, blocked);

            if (next == null)
                break;

            current = next;
        }

        if (TilePortRules.Has(current.GatewayPorts, spec.Side) || layout.TileAt(current.I + stepI, current.J + stepJ) != null)
            return;

        current.GatewayPorts |= spec.Side;
        current.ArterialPorts |= spec.Side;
        spec.Tile = current;

        for (int k = 1; k <= BLOCK_BEYOND_GATEWAY; k++)
            blocked.Add(SettlementLayout.Key(current.I + stepI * k, current.J + stepJ * k));
    }

    private SettlementTile Occupy(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTile from, TilePorts side, HashSet<long> blocked)
    {
        int i = from.I + TilePortRules.StepI(side);
        int j = from.J + TilePortRules.StepJ(side);

        if (blocked.Contains(SettlementLayout.Key(i, j)))
            return null;

        SettlementTile next = layout.TileAt(i, j);

        if (next == null)
        {
            if (!Buildable(hubs, layout, i, j))
                return null;

            next = layout.AddTile(i, j);
        }

        from.ArterialPorts |= side;
        next.ArterialPorts |= TilePortRules.Opposite(side);

        return next;
    }

    private void ExtendMainStreet(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, HashSet<long> blocked)
    {
        if (profile.Shape == SettlementShape.Compact)
            return;

        foreach (TilePorts side in new[] { TilePorts.East, TilePorts.West })
        {
            if (TilePortRules.Has(core.ArterialPorts, side))
                continue;

            SettlementTile current = core;

            for (int k = 0; k < Mathf.Max(1, profile.ArmTiles); k++)
            {
                SettlementTile next = Occupy(hubs, layout, current, side, blocked);

                if (next == null)
                    break;

                current = next;
            }
        }
    }

    private void Grow(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core,
        HashSet<long> blocked, ref Random random)
    {
        int low = profile.LowTiles;
        int high = profile.HighTiles;
        int target = Mathf.Max(layout.Tiles.Count, low + random.NextInt(high - low + 1));
        int reach = Mathf.Max(1, profile.ArmTiles) + Mathf.CeilToInt(Mathf.Sqrt(high)) + 2;
        int side = reach * 2 + 1;

        var heap = new MinHeap(64);
        var queued = new HashSet<long>();

        foreach (SettlementTile tile in new List<SettlementTile>(layout.Tiles))
            Enqueue(layout, profile, core, tile, heap, queued, reach, side, ref random);

        while (layout.Tiles.Count < target && heap.TryPop(out int id))
        {
            int i = id % side - reach + core.I;
            int j = id / side - reach + core.J;

            if (layout.TileAt(i, j) != null || blocked.Contains(SettlementLayout.Key(i, j)))
                continue;

            if (!Buildable(hubs, layout, i, j))
                continue;

            SettlementTile added = layout.AddTile(i, j);

            Enqueue(layout, profile, core, added, heap, queued, reach, side, ref random);
        }
    }

    private static void Enqueue(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, SettlementTile tile,
        MinHeap heap, HashSet<long> queued, int reach, int side, ref Random random)
    {
        foreach (TilePorts direction in TilePortRules.SIDES)
        {
            int i = tile.I + TilePortRules.StepI(direction);
            int j = tile.J + TilePortRules.StepJ(direction);
            int du = i - core.I;
            int dv = j - core.J;

            if (Mathf.Abs(du) > reach || Mathf.Abs(dv) > reach)
                continue;

            if (profile.Shape == SettlementShape.Linear && dv != 0)
                continue;

            if (profile.Shape == SettlementShape.Cross && du != 0 && dv != 0)
                continue;

            long key = SettlementLayout.Key(i, j);

            if (layout.TileAt(i, j) != null || !queued.Add(key))
                continue;

            float priority = profile.Shape == SettlementShape.Compact
                ? du * du + dv * dv
                : Mathf.Abs(du) + Mathf.Abs(dv) * 4f;

            if (profile.Shape == SettlementShape.Compact && TouchesArterial(layout, i, j))
                priority *= ARTERIAL_PULL;

            priority *= 1f + GROWTH_NOISE * random.NextFloat(-1f, 1f);

            heap.Push((dv + reach) * side + du + reach, priority);
        }
    }

    private static bool TouchesArterial(SettlementLayout layout, int i, int j)
    {
        foreach (TilePorts direction in TilePortRules.SIDES)
        {
            SettlementTile neighbour = layout.TileAt(i + TilePortRules.StepI(direction), j + TilePortRules.StepJ(direction));

            if (neighbour != null && neighbour.IsArterial)
                return true;
        }

        return false;
    }
}

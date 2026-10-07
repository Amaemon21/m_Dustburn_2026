using System.Collections.Generic;
using UnityEngine;

public sealed partial class TileTopologySolver
{
    private void Repair(SettlementLayout layout, List<Adjacency> edges, bool[] kept, SettlementTypeProfile profile)
    {
        int passes = layout.Tiles.Count * 3;

        for (int pass = 0; pass < passes; pass++)
        {
            if (!TrimOverfull(layout, edges, kept, profile) && !ExtendDeadEnds(layout, edges, kept, profile))
                return;
        }
    }

    private bool TrimOverfull(SettlementLayout layout, List<Adjacency> edges, bool[] kept, SettlementTypeProfile profile)
    {
        int[] ports = PortCounts(layout, edges, kept);
        int worst = -1;
        float worstCost = float.MinValue;

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (!kept[edge] || edges[edge].Required)
                continue;

            bool overfull = ports[edges[edge].A] > MaxPorts(layout, layout.Tiles[edges[edge].A], profile)
                || ports[edges[edge].B] > MaxPorts(layout, layout.Tiles[edges[edge].B], profile);

            if (!overfull || edges[edge].Cost <= worstCost)
                continue;

            if (IsBridge(layout, edges, kept, edge))
                continue;

            worst = edge;
            worstCost = edges[edge].Cost;
        }

        if (worst < 0)
            return false;

        kept[worst] = false;

        return true;
    }

    private bool ExtendDeadEnds(SettlementLayout layout, List<Adjacency> edges, bool[] kept, SettlementTypeProfile profile)
    {
        int[] ports = PortCounts(layout, edges, kept);
        int caps = 0;

        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i] == 1)
                caps++;
        }

        bool shareExceeded = IsGridded(layout) && caps > DeadEndAllowance(layout);
        int best = -1;
        float bestCost = float.MaxValue;

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (kept[edge])
                continue;

            int a = edges[edge].A;
            int b = edges[edge].B;
            bool fixesDense = (ports[a] == 1 && IsDense(layout.Tiles[a].District)) || (ports[b] == 1 && IsDense(layout.Tiles[b].District));
            bool fixesShare = shareExceeded && (ports[a] == 1 || ports[b] == 1);

            if (!fixesDense && !fixesShare)
                continue;

            if (ports[a] + 1 > MaxPorts(layout, layout.Tiles[a], profile) || ports[b] + 1 > MaxPorts(layout, layout.Tiles[b], profile))
                continue;

            if (edges[edge].Cost >= bestCost)
                continue;

            best = edge;
            bestCost = edges[edge].Cost;
        }

        if (best < 0)
            return false;

        kept[best] = true;

        return true;
    }

    private int CountViolations(SettlementLayout layout, List<Adjacency> edges, bool[] kept, SettlementTypeProfile profile)
    {
        int[] ports = PortCounts(layout, edges, kept);
        TilePorts[] masks = PortMasks(layout, edges, kept);
        int violations = 0;
        int caps = 0;

        for (int i = 0; i < layout.Tiles.Count; i++)
        {
            SettlementTile tile = layout.Tiles[i];

            if (ports[i] == 0)
                violations++;

            if (ports[i] > MaxPorts(layout, tile, profile))
                violations++;

            if (ports[i] == 1)
            {
                caps++;

                if (IsDense(tile.District) && HasFreeNeighbour(layout, tile, masks[i]))
                    violations++;
            }
        }

        if (IsGridded(layout))
            violations += Mathf.Max(0, caps - DeadEndAllowance(layout));

        return violations + Disconnected(layout, edges, kept);
    }

    private static bool HasFreeNeighbour(SettlementLayout layout, SettlementTile tile, TilePorts mask)
    {
        foreach (TilePorts side in TilePortRules.SIDES)
        {
            if (!TilePortRules.Has(mask, side) && layout.Neighbour(tile, side) != null)
                return true;
        }

        return false;
    }
}

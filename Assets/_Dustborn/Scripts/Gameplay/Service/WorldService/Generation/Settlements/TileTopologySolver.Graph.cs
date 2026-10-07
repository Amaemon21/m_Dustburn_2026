using System.Collections.Generic;

public sealed partial class TileTopologySolver
{
    private static TilePorts[] PortMasks(SettlementLayout layout, List<Adjacency> edges, bool[] kept)
    {
        var masks = new TilePorts[layout.Tiles.Count];

        for (int i = 0; i < layout.Tiles.Count; i++)
            masks[i] = layout.Tiles[i].GatewayPorts;

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (!kept[edge])
                continue;

            masks[edges[edge].A] |= edges[edge].Side;
            masks[edges[edge].B] |= TilePortRules.Opposite(edges[edge].Side);
        }

        return masks;
    }

    private static int Disconnected(SettlementLayout layout, List<Adjacency> edges, bool[] kept)
    {
        var parent = new int[layout.Tiles.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        int components = layout.Tiles.Count;

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (kept[edge] && RoadGraph.Union(parent, edges[edge].A, edges[edge].B))
                components--;
        }

        return components - 1;
    }

    private static bool IsBridge(SettlementLayout layout, List<Adjacency> edges, bool[] kept, int removed)
    {
        kept[removed] = false;
        bool disconnects = Disconnected(layout, edges, kept) > 0;
        kept[removed] = true;

        return disconnects;
    }

    private static int[] PortCounts(SettlementLayout layout, List<Adjacency> edges, bool[] kept)
    {
        var ports = new int[layout.Tiles.Count];

        for (int i = 0; i < layout.Tiles.Count; i++)
            ports[i] = TilePortRules.Count(layout.Tiles[i].GatewayPorts);

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (!kept[edge])
                continue;

            ports[edges[edge].A]++;
            ports[edges[edge].B]++;
        }

        return ports;
    }

    private static void Apply(SettlementLayout layout, List<Adjacency> edges, bool[] kept)
    {
        foreach (SettlementTile tile in layout.Tiles)
            tile.Ports = tile.GatewayPorts;

        for (int edge = 0; edge < edges.Count; edge++)
        {
            if (!kept[edge])
                continue;

            SettlementTile a = layout.Tiles[edges[edge].A];
            SettlementTile b = layout.Tiles[edges[edge].B];

            a.Ports |= edges[edge].Side;
            b.Ports |= TilePortRules.Opposite(edges[edge].Side);
        }

        foreach (SettlementTile tile in layout.Tiles)
            tile.ArterialPorts &= tile.Ports;
    }

    private static void MeasureHops(SettlementLayout layout)
    {
        var queue = new Queue<SettlementTile>();

        foreach (SettlementTile tile in layout.Tiles)
        {
            tile.Hops = int.MaxValue;

            if (!tile.IsGateway)
                continue;

            tile.Hops = 0;
            queue.Enqueue(tile);
        }

        while (queue.Count > 0)
        {
            SettlementTile tile = queue.Dequeue();

            foreach (TilePorts side in TilePortRules.SIDES)
            {
                if (!TilePortRules.Has(tile.Ports, side))
                    continue;

                SettlementTile next = layout.Neighbour(tile, side);

                if (next == null || next.Hops <= tile.Hops + 1)
                    continue;

                next.Hops = tile.Hops + 1;
                queue.Enqueue(next);
            }
        }
    }
}

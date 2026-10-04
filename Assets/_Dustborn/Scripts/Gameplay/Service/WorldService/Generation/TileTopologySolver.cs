using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed class TileTopologySolver
{
    private const float COST_JITTER = 0.2f;
    private const float STEEP_COST = 4f;

    private sealed class Adjacency
    {
        public int A;
        public int B;
        public TilePorts Side;
        public bool Required;
        public float Cost;
    }

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;

    public int Attempts { get; private set; }
    public int Violations { get; private set; }

    public TileTopologySolver(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;
    }

    public void Solve(SettlementLayout layout, SettlementTypeProfile profile, ref Random random)
    {
        Attempts = 0;
        Violations = 0;

        if (layout.IsEmpty)
            return;

        List<Adjacency> edges = Collect(layout);
        bool[] best = null;
        int bestViolations = int.MaxValue;
        int attempts = Mathf.Max(1, _config.TopologyAttempts);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var local = new Random(random.NextUInt() | 1u);
            bool[] kept = Build(layout, edges, profile, ref local);

            Repair(layout, edges, kept, profile);

            int violations = CountViolations(layout, edges, kept, profile);
            Attempts++;

            if (violations < bestViolations)
            {
                bestViolations = violations;
                best = kept;
            }

            if (violations == 0)
                break;
        }

        Apply(layout, edges, best);
        Violations = bestViolations;
        layout.TopologyViolations = bestViolations;
        MeasureHops(layout);
    }

    private List<Adjacency> Collect(SettlementLayout layout)
    {
        var index = new Dictionary<SettlementTile, int>();

        for (int i = 0; i < layout.Tiles.Count; i++)
            index[layout.Tiles[i]] = i;

        var edges = new List<Adjacency>();

        foreach (SettlementTile tile in layout.Tiles)
        {
            foreach (TilePorts side in new[] { TilePorts.East, TilePorts.North })
            {
                SettlementTile other = layout.Neighbour(tile, side);

                if (other == null)
                    continue;

                bool required = TilePortRules.Has(tile.ArterialPorts, side) && TilePortRules.Has(other.ArterialPorts, TilePortRules.Opposite(side));

                edges.Add(new Adjacency
                {
                    A = index[tile],
                    B = index[other],
                    Side = side,
                    Required = required,
                    Cost = Grade(tile.Center, other.Center)
                });
            }
        }

        return edges;
    }

    private float Grade(Vector2 from, Vector2 to)
    {
        float distance = Mathf.Max(1f, Vector2.Distance(from, to));
        float grade = Mathf.Abs(_map.SampleWorldSmooth(to.x, to.y) - _map.SampleWorldSmooth(from.x, from.y)) / distance;

        return 1f + (grade > _config.MaxStreetSlope ? STEEP_COST : _config.RoadSlopePenalty * grade * grade);
    }

    private bool[] Build(SettlementLayout layout, List<Adjacency> edges, SettlementTypeProfile profile, ref Random random)
    {
        var kept = new bool[edges.Count];
        var parent = new int[layout.Tiles.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        var order = new List<(float Cost, int Edge)>(edges.Count);

        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].Required)
            {
                kept[i] = true;
                RoadGraph.Union(parent, edges[i].A, edges[i].B);
                continue;
            }

            order.Add((edges[i].Cost * (1f + COST_JITTER * random.NextFloat(-1f, 1f)), i));
        }

        order.Sort((left, right) =>
        {
            int compare = left.Cost.CompareTo(right.Cost);
            return compare != 0 ? compare : left.Edge.CompareTo(right.Edge);
        });

        var leftover = new List<int>();

        foreach ((float _, int edge) in order)
        {
            if (RoadGraph.Union(parent, edges[edge].A, edges[edge].B))
            {
                kept[edge] = true;
                continue;
            }

            leftover.Add(edge);
        }

        foreach (int edge in leftover)
        {
            DistrictType district = Sparser(layout.Tiles[edges[edge].A].District, layout.Tiles[edges[edge].B].District);
            float chance = Mathf.Clamp01(profile.LoopDensity * LoopFactor(district));

            if (random.NextFloat() < chance)
                kept[edge] = true;
        }

        return kept;
    }

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

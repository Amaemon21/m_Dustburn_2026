using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed partial class TileTopologySolver
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
}

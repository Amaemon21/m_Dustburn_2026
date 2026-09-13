using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class BlockLattice
{
    private const int GROWTH_MARGIN = 3;
    private const float CAPACITY_SLACK = 1f;
    private const float HOUSES_PER_BLOCK = 8f;
    private const float GROWTH_NOISE = 0.3f;
    private const float COST_JITTER = 0.15f;
    private const float STEEP_COST = 1000f;
    private const float FALLBACK_WIDTH = 14f;
    private const float FALLBACK_DEPTH = 12f;

    private const byte UNSEEN = 0;
    private const byte QUEUED = 1;
    private const byte TAKEN = 2;
    private const byte REFUSED = 3;

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly float _lotStep;
    private readonly float _cornerLoss;

    public int Empty { get; private set; }
    public int Flooded { get; private set; }
    public int Steep { get; private set; }
    public int Crowded { get; private set; }
    public int Outside { get; private set; }
    public int SteepStreets { get; private set; }
    public int DroppedStreets { get; private set; }

    public BlockLattice(WorldGenerationConfig config, HeightMap map, PoiDatabase pois)
    {
        _config = config;
        _map = map;

        MeanLot(pois, out float width, out float depth);

        _lotStep = Mathf.Max(1f, width + 2f * config.LotMargin + config.LotGap * 1.25f);
        _cornerLoss = 2f * (config.StreetHalfWidth + config.LotFrontGap) + 0.5f * (depth + config.LotSetback + config.LotMargin);
    }

    public static float EstimateRadius(WorldGenerationConfig config, int houses)
    {
        float blocks = Mathf.Max(1f, houses * CAPACITY_SLACK / HOUSES_PER_BLOCK);
        float block = (config.BlockSizeMin + config.BlockSizeMax) * 0.5f;

        return Mathf.Sqrt(blocks) * block * 0.75f + config.BlockWarp;
    }

    public SettlementLayout Build(IReadOnlyList<Hub> hubs, int index, IReadOnlyList<int> neighbours, float angle, ref Random random)
    {
        Hub hub = hubs[index];
        var layout = new SettlementLayout(hub, angle);

        layout.Neighbours.AddRange(neighbours);

        var frame = new Frame(hub.Position, angle, Span(hub.Houses), _config, ref random);
        List<int> cells = Grow(frame, hubs, index, hub.Houses, ref random);

        if (cells.Count == 0)
        {
            Empty++;
            layout.Measure();
            return layout;
        }

        var blocks = new Dictionary<int, Block>(cells.Count);

        foreach (int cell in cells)
        {
            int i = cell % frame.CellsPerSide;
            int j = cell / frame.CellsPerSide;

            var block = new Block(frame.Node(i, j), frame.Node(i + 1, j), frame.Node(i + 1, j + 1), frame.Node(i, j + 1));

            blocks[cell] = block;
            layout.Blocks.Add(block);
        }

        List<Side> sides = Connect(frame, cells, ref random);

        Chain(frame, sides, layout);
        Front(frame, sides, blocks, layout, ref random);

        layout.Measure();
        PlaceGates(hubs, frame, sides, layout);

        return layout;
    }

    private List<int> Grow(Frame frame, IReadOnlyList<Hub> hubs, int index, int houses, ref Random random)
    {
        int side = frame.CellsPerSide;
        var state = new byte[side * side];
        var heap = new MinHeap(64);
        var taken = new List<int>();

        float target = Mathf.Max(1, houses) * CAPACITY_SLACK;
        float capacity = 0f;
        int centre = side / 2;

        Enqueue(frame, state, heap, centre - 1, centre - 1, ref random);
        Enqueue(frame, state, heap, centre, centre - 1, ref random);
        Enqueue(frame, state, heap, centre - 1, centre, ref random);
        Enqueue(frame, state, heap, centre, centre, ref random);

        while (capacity < target && heap.TryPop(out int cell))
        {
            int i = cell % side;
            int j = cell / side;

            if (!Accept(frame, hubs, index, i, j))
            {
                state[cell] = REFUSED;
                continue;
            }

            state[cell] = TAKEN;
            taken.Add(cell);
            capacity += Capacity(frame, i, j);

            Enqueue(frame, state, heap, i + 1, j, ref random);
            Enqueue(frame, state, heap, i - 1, j, ref random);
            Enqueue(frame, state, heap, i, j + 1, ref random);
            Enqueue(frame, state, heap, i, j - 1, ref random);
        }

        return taken;
    }

    private static void Enqueue(Frame frame, byte[] state, MinHeap heap, int i, int j, ref Random random)
    {
        int side = frame.CellsPerSide;

        if (i < 0 || j < 0 || i >= side || j >= side)
            return;

        int cell = j * side + i;

        if (state[cell] != UNSEEN)
            return;

        state[cell] = QUEUED;

        float noise = 1f + GROWTH_NOISE * random.NextFloat(-1f, 1f);

        heap.Push(cell, (frame.CellCenter(i, j) - frame.Origin).sqrMagnitude * noise * noise);
    }

    private bool Accept(Frame frame, IReadOnlyList<Hub> hubs, int index, int i, int j)
    {
        Vector2 a = frame.Node(i, j);
        Vector2 b = frame.Node(i + 1, j);
        Vector2 c = frame.Node(i + 1, j + 1);
        Vector2 d = frame.Node(i, j + 1);
        Vector2 center = (a + b + c + d) * 0.25f;

        if (!InsideWorld(a) || !InsideWorld(b) || !InsideWorld(c) || !InsideWorld(d))
        {
            Outside++;
            return false;
        }

        float low = float.MaxValue;
        float high = float.MinValue;

        Probe(a, ref low, ref high);
        Probe(b, ref low, ref high);
        Probe(c, ref low, ref high);
        Probe(d, ref low, ref high);
        Probe(center, ref low, ref high);
        Probe((a + b) * 0.5f, ref low, ref high);
        Probe((b + c) * 0.5f, ref low, ref high);
        Probe((c + d) * 0.5f, ref low, ref high);
        Probe((d + a) * 0.5f, ref low, ref high);

        if (_config.SeaLevel > 0f && low < _config.SeaLevel + _config.ShoreMargin)
        {
            Flooded++;
            return false;
        }

        if (high - low > _config.MaxBlockRelief)
        {
            Steep++;
            return false;
        }

        if (IsCrowded(hubs, index, center) || IsCrowded(hubs, index, a) || IsCrowded(hubs, index, b)
            || IsCrowded(hubs, index, c) || IsCrowded(hubs, index, d))
        {
            Crowded++;
            return false;
        }

        return true;
    }

    private void Probe(Vector2 point, ref float low, ref float high)
    {
        float height = _map.SampleWorldSmooth(point.x, point.y);

        low = Mathf.Min(low, height);
        high = Mathf.Max(high, height);
    }

    private bool InsideWorld(Vector2 point)
    {
        float margin = _config.StreetHalfWidth + _config.StreetShoulder;
        float limit = _config.WorldSize - margin;

        return point.x >= margin && point.y >= margin && point.x <= limit && point.y <= limit;
    }

    private bool IsCrowded(IReadOnlyList<Hub> hubs, int index, Vector2 point)
    {
        float own = (point - hubs[index].Position).magnitude + _config.SettlementGap;
        float ownSqr = own * own;

        for (int other = 0; other < hubs.Count; other++)
        {
            if (other == index)
                continue;

            if ((point - hubs[other].Position).sqrMagnitude < ownSqr)
                return true;
        }

        return false;
    }

    private float Capacity(Frame frame, int i, int j)
    {
        float frontage = Usable(frame.Node(i, j), frame.Node(i + 1, j))
            + Usable(frame.Node(i + 1, j), frame.Node(i + 1, j + 1))
            + Usable(frame.Node(i + 1, j + 1), frame.Node(i, j + 1))
            + Usable(frame.Node(i, j + 1), frame.Node(i, j));

        return frontage / _lotStep;
    }

    private float Usable(Vector2 from, Vector2 to)
    {
        return Mathf.Max(0f, Vector2.Distance(from, to) - _cornerLoss);
    }

    private List<Side> Connect(Frame frame, List<int> cells, ref Random random)
    {
        var lookup = new Dictionary<long, Side>();
        var sides = new List<Side>();
        int perSide = frame.CellsPerSide;

        foreach (int cell in cells)
        {
            int i = cell % perSide;
            int j = cell / perSide;

            Register(frame, lookup, sides, i, j, true);
            Register(frame, lookup, sides, i, j + 1, true);
            Register(frame, lookup, sides, i, j, false);
            Register(frame, lookup, sides, i + 1, j, false);
        }

        foreach (Side side in sides)
            Measure(frame, side, ref random);

        sides.Sort((left, right) => left.Cost.CompareTo(right.Cost));

        var edges = new List<(int From, int To, float Cost)>(sides.Count);

        foreach (Side side in sides)
            edges.Add((side.From, side.To, side.Cost));

        var leftover = new List<int>();

        foreach (int edge in RoadGraph.SpanningTree(frame.Nodes.Length, edges, leftover))
        {
            sides[edge].Kept = true;

            if (sides[edge].Steep)
                SteepStreets++;
        }

        foreach (int edge in leftover)
        {
            Side side = sides[edge];

            if (!side.Steep && random.NextFloat() < _config.StreetLoopChance)
            {
                side.Kept = true;
                continue;
            }

            DroppedStreets++;
        }

        return sides;
    }

    private static void Register(Frame frame, Dictionary<long, Side> lookup, List<Side> sides, int i, int j, bool horizontal)
    {
        long key = frame.SideKey(i, j, horizontal);

        if (lookup.TryGetValue(key, out Side existing))
        {
            existing.Cells++;
            return;
        }

        var side = new Side
        {
            I = i,
            J = j,
            Horizontal = horizontal,
            From = frame.NodeIndex(i, j),
            To = horizontal ? frame.NodeIndex(i + 1, j) : frame.NodeIndex(i, j + 1),
            Cells = 1
        };

        lookup[key] = side;
        sides.Add(side);
    }

    private void Measure(Frame frame, Side side, ref Random random)
    {
        Vector2 from = frame.Nodes[side.From];
        Vector2 to = frame.Nodes[side.To];
        Vector2 middle = (from + to) * 0.5f;

        float length = Vector2.Distance(from, to);
        float half = Mathf.Max(1f, length * 0.5f);

        float start = _map.SampleWorldSmooth(from.x, from.y);
        float centre = _map.SampleWorldSmooth(middle.x, middle.y);
        float end = _map.SampleWorldSmooth(to.x, to.y);

        float grade = Mathf.Max(Mathf.Abs(centre - start), Mathf.Abs(end - centre)) / half;

        side.Steep = grade > _config.MaxStreetSlope;
        side.Cost = length * (1f + _config.RoadSlopePenalty * grade * grade) * (1f + COST_JITTER * random.NextFloat(-1f, 1f));

        if (side.Steep)
            side.Cost += STEEP_COST * length;
    }

    private void Chain(Frame frame, List<Side> sides, SettlementLayout layout)
    {
        var kept = new HashSet<long>();

        foreach (Side side in sides)
        {
            if (side.Kept)
                kept.Add(frame.SideKey(side.I, side.J, side.Horizontal));
        }

        int size = frame.NodesPerSide;
        var points = new List<Vector2>();

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size - 1; i++)
            {
                if (!kept.Contains(frame.SideKey(i, j, true)))
                {
                    Flush(points, layout);
                    continue;
                }

                if (points.Count == 0)
                    points.Add(frame.Node(i, j));

                points.Add(frame.Node(i + 1, j));
            }

            Flush(points, layout);
        }

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size - 1; j++)
            {
                if (!kept.Contains(frame.SideKey(i, j, false)))
                {
                    Flush(points, layout);
                    continue;
                }

                if (points.Count == 0)
                    points.Add(frame.Node(i, j));

                points.Add(frame.Node(i, j + 1));
            }

            Flush(points, layout);
        }
    }

    private void Flush(List<Vector2> points, SettlementLayout layout)
    {
        if (points.Count >= 2)
            layout.Streets.Add(new Road(points.ToArray(), _config.StreetHalfWidth * 2f));

        points.Clear();
    }

    private void Front(Frame frame, List<Side> sides, Dictionary<int, Block> blocks, SettlementLayout layout, ref Random random)
    {
        foreach (Side side in sides)
        {
            if (!side.Kept)
                continue;

            Vector2 from = frame.Nodes[side.From];
            Vector2 to = frame.Nodes[side.To];
            Vector2 direction = (to - from).normalized;
            var left = new Vector2(-direction.y, direction.x);

            Block leftBlock = BlockBeside(blocks, frame, side, true);
            Block rightBlock = BlockBeside(blocks, frame, side, false);

            AddFrontage(layout, from, to, left, leftBlock, rightBlock, ref random);
            AddFrontage(layout, from, to, -left, rightBlock, leftBlock, ref random);
        }
    }

    private void AddFrontage(SettlementLayout layout, Vector2 from, Vector2 to, Vector2 normal, Block own, Block other, ref Random random)
    {
        if (own != null)
        {
            layout.Frontages.Add(new Frontage(from, to, normal, _config.StreetHalfWidth, own));
            return;
        }

        if (other == null || random.NextFloat() >= _config.OuterFrontageChance)
            return;

        layout.Frontages.Add(new Frontage(from, to, normal, _config.StreetHalfWidth, other));
    }

    private static Block BlockBeside(Dictionary<int, Block> blocks, Frame frame, Side side, bool left)
    {
        int i = side.I;
        int j = side.J;

        if (side.Horizontal)
            j = left ? j : j - 1;
        else
            i = left ? i - 1 : i;

        if (i < 0 || j < 0 || i >= frame.CellsPerSide || j >= frame.CellsPerSide)
            return null;

        return blocks.TryGetValue(j * frame.CellsPerSide + i, out Block block) ? block : null;
    }

    private static void PlaceGates(IReadOnlyList<Hub> hubs, Frame frame, List<Side> sides, SettlementLayout layout)
    {
        var boundary = new List<Vector2>();
        var seen = new HashSet<int>();

        foreach (Side side in sides)
        {
            if (!side.Kept || side.Cells != 1)
                continue;

            if (seen.Add(side.From))
                boundary.Add(frame.Nodes[side.From]);

            if (seen.Add(side.To))
                boundary.Add(frame.Nodes[side.To]);
        }

        if (boundary.Count == 0)
        {
            foreach (Side side in sides)
            {
                if (side.Kept && seen.Add(side.From))
                    boundary.Add(frame.Nodes[side.From]);
            }
        }

        Vector2 origin = layout.Hub.Position;

        foreach (int neighbour in layout.Neighbours)
        {
            Vector2 direction = hubs[neighbour].Position - origin;
            Vector2 best = origin;
            float bestScore = float.MinValue;

            foreach (Vector2 node in boundary)
            {
                float score = Vector2.Dot(node - origin, direction);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = node;
            }

            layout.Gates.Add(best);
        }
    }

    private static int Span(int houses)
    {
        return Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1f, houses * CAPACITY_SLACK / HOUSES_PER_BLOCK))) + GROWTH_MARGIN;
    }

    private static void MeanLot(PoiDatabase pois, out float width, out float depth)
    {
        width = FALLBACK_WIDTH;
        depth = FALLBACK_DEPTH;

        if (pois == null)
            return;

        float weight = 0f;
        float widths = 0f;
        float depths = 0f;

        foreach (PoiDefinition definition in pois.Definitions)
        {
            if (definition == null || definition.District != DistrictType.Residential)
                continue;

            weight += definition.Weight;
            widths += definition.FootprintWidth * definition.Weight;
            depths += definition.FootprintDepth * definition.Weight;
        }

        if (weight <= 0f)
            return;

        width = widths / weight;
        depth = depths / weight;
    }

    private sealed class Side
    {
        public int I;
        public int J;
        public bool Horizontal;
        public int From;
        public int To;
        public int Cells;
        public float Cost;
        public bool Steep;
        public bool Kept;
    }

    private sealed class Frame
    {
        public Vector2 Origin { get; }
        public int NodesPerSide { get; }
        public int CellsPerSide { get; }
        public Vector2[] Nodes { get; }

        public Frame(Vector2 origin, float angle, int span, WorldGenerationConfig config, ref Random random)
        {
            Origin = origin;
            NodesPerSide = span * 2 + 1;
            CellsPerSide = span * 2;
            Nodes = new Vector2[NodesPerSide * NodesPerSide];

            float[] columns = Lines(span, config, ref random);
            float[] rows = Lines(span, config, ref random);

            var axisU = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var axisV = new Vector2(-axisU.y, axisU.x);

            var seedU = new float2(random.NextFloat(-512f, 512f), random.NextFloat(-512f, 512f));
            var seedV = new float2(random.NextFloat(-512f, 512f), random.NextFloat(-512f, 512f));
            float period = Mathf.Max(1f, config.BlockWarpPeriod);

            for (int j = 0; j < NodesPerSide; j++)
            {
                for (int i = 0; i < NodesPerSide; i++)
                {
                    var local = new float2(columns[i], rows[j]) / period;

                    float warpU = FractalNoise.Sample(local, seedU, 2, 2f, 0.5f) * config.BlockWarp;
                    float warpV = FractalNoise.Sample(local, seedV, 2, 2f, 0.5f) * config.BlockWarp;

                    Nodes[j * NodesPerSide + i] = origin + axisU * (columns[i] + warpU) + axisV * (rows[j] + warpV);
                }
            }
        }

        public Vector2 Node(int i, int j)
        {
            return Nodes[NodeIndex(i, j)];
        }

        public int NodeIndex(int i, int j)
        {
            return j * NodesPerSide + i;
        }

        public long SideKey(int i, int j, bool horizontal)
        {
            return (long)NodeIndex(i, j) * 2 + (horizontal ? 1 : 0);
        }

        public Vector2 CellCenter(int i, int j)
        {
            return (Node(i, j) + Node(i + 1, j) + Node(i + 1, j + 1) + Node(i, j + 1)) * 0.25f;
        }

        private static float[] Lines(int span, WorldGenerationConfig config, ref Random random)
        {
            float low = Mathf.Min(config.BlockSizeMin, config.BlockSizeMax);
            float high = Mathf.Max(config.BlockSizeMin, config.BlockSizeMax);

            var lines = new float[span * 2 + 1];

            for (int step = 1; step <= span; step++)
            {
                lines[span + step] = lines[span + step - 1] + random.NextFloat(low, high);
                lines[span - step] = lines[span - step + 1] - random.NextFloat(low, high);
            }

            return lines;
        }
    }
}

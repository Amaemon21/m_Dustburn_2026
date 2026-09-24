using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class StandingWaterRegions
{
    private const int OWNERSHIP_REACH = 2;
    private const float LEAK_MARGIN = 0.05f;
    private const float MIN_FRAGMENT_AREA = 64f;
    private const float SATELLITE_AREA = 800f;
    private const float ISLET_AREA = 8f;
    private const float BAR_PAD = 2f;
    private const float FILL_TOLERANCE = 0.05f;
    private const float OUTLET_TOLERANCE = 0.05f;
    private const float CARVE_EPSILON = 0.05f;
    private const float RIVER_REACH = 10f;

    private static readonly int[] StepX = { 1, 0, -1, 0 };
    private static readonly int[] StepZ = { 0, 1, 0, -1 };

    private readonly WaterMap _water;
    private readonly HeightMap _map;
    private readonly int _nodes;
    private readonly int _sub;
    private readonly float _step;
    private readonly short[] _owner;
    private readonly HashSet<int> _sunk = new();

    public int Lowered { get; private set; }
    public int Dropped { get; private set; }
    public int Fragments { get; private set; }
    public int Islets { get; private set; }
    public int SeaPuddles { get; private set; }

    private int MinFragment => Mathf.Max(1, Mathf.CeilToInt(MIN_FRAGMENT_AREA / (_step * _step)));
    private int IsletNodes => Mathf.Max(1, Mathf.CeilToInt(ISLET_AREA / (_step * _step)));

    public StandingWaterRegions(WaterMap water, HeightMap map)
    {
        _water = water;
        _map = map;
        _nodes = water.NodeResolution;
        _sub = WaterMap.SUBDIVISION;
        _step = water.NodeStep;
        _owner = new short[_nodes * _nodes];
    }

    public float Ground(int node)
    {
        return _map.SampleWorldSmooth(node % _nodes * _step, node / _nodes * _step);
    }

    public short Owner(int node)
    {
        return _owner[node];
    }

    public bool IsSunk(int node)
    {
        return _sunk.Contains(node);
    }

    public int NearestNode(float x, float z)
    {
        int i = Mathf.Clamp(Mathf.RoundToInt(x / _step), 0, _nodes - 1);
        int j = Mathf.Clamp(Mathf.RoundToInt(z / _step), 0, _nodes - 1);

        return j * _nodes + i;
    }

    public bool NearestBody(float x, float z, float reach, out short body, out float distance)
    {
        body = WaterMap.OWNER_NONE;
        distance = float.MaxValue;

        int minI = Mathf.Max(0, Mathf.FloorToInt((x - reach) / _step)), maxI = Mathf.Min(_nodes - 1, Mathf.CeilToInt((x + reach) / _step));
        int minJ = Mathf.Max(0, Mathf.FloorToInt((z - reach) / _step)), maxJ = Mathf.Min(_nodes - 1, Mathf.CeilToInt((z + reach) / _step));
        float nearest = reach * reach;

        for (int j = minJ; j <= maxJ; j++)
        {
            for (int i = minI; i <= maxI; i++)
            {
                short owner = _owner[j * _nodes + i];

                if (owner < 0)
                    continue;

                float dx = i * _step - x, dz = j * _step - z;
                float squared = dx * dx + dz * dz;

                if (squared >= nearest)
                    continue;

                nearest = squared;
                body = owner;
            }
        }

        if (body < 0)
            return false;

        distance = Mathf.Sqrt(nearest);
        return true;
    }

    public void Build(float shelf)
    {
        Array.Fill(_owner, WaterMap.OWNER_SEA);
        _sunk.Clear();

        BarRivers();

        List<int>[] cells = BodyCells();
        bool[] rivers = RiverBodies();

        for (int body = 0; body < _water.Bodies.Count; body++)
            Fill(body, cells[body], rivers[body], shelf);

        ClearSeaPuddles();
    }

    private void BarRivers()
    {
        if (_water.SeaLevel <= 0f)
            return;

        foreach (RiverPath river in _water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (!Above(points, i, _water.SeaLevel))
                    continue;

                Bar(points[i].Position, points[i + 1].Position, 0.5f * Mathf.Max(points[i].Width, points[i + 1].Width) + BAR_PAD, Above(points, i - 1, _water.SeaLevel), Above(points, i + 1, _water.SeaLevel));
            }
        }
    }

    private static bool Above(List<RiverPoint> points, int segment, float surface)
    {
        if (segment < 0 || segment + 1 >= points.Count)
            return false;

        RiverPoint a = points[segment], b = points[segment + 1];

        return !a.Submerged && !b.Submerged && Mathf.Min(a.Surface, b.Surface) > surface + WaterMeshes.MOUTH_BLEND;
    }

    private static bool Below(List<RiverPoint> points, int segment, float surface)
    {
        if (segment < 0 || segment + 1 >= points.Count)
            return false;

        return Mathf.Max(points[segment].Surface, points[segment + 1].Surface) < surface - OUTLET_TOLERANCE;
    }

    private void Bar(Vector2 a, Vector2 b, float radius, bool roundStart, bool roundEnd)
    {
        int minI = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius) / _step));
        int maxI = Mathf.Min(_nodes - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius) / _step));
        int minJ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - radius) / _step));
        int maxJ = Mathf.Min(_nodes - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + radius) / _step));

        Vector2 axis = b - a;
        float length = axis.sqrMagnitude;
        float limit = radius * radius;

        for (int j = minJ; j <= maxJ; j++)
        {
            for (int i = minI; i <= maxI; i++)
            {
                var point = new Vector2(i * _step, j * _step);
                float along = length < 1e-8f ? 0f : Vector2.Dot(point - a, axis) / length;

                if (along < 0f && !roundStart || along > 1f && !roundEnd)
                    continue;

                float t = Mathf.Clamp01(along);

                if ((point - (a + axis * t)).sqrMagnitude <= limit)
                    _owner[j * _nodes + i] = WaterMap.OWNER_NONE;
            }
        }
    }

    private List<int>[] BodyCells()
    {
        var cells = new List<int>[_water.Bodies.Count];

        for (int body = 0; body < cells.Length; body++)
            cells[body] = new List<int>();

        for (int cell = 0; cell < _water.BodyIds.Length; cell++)
        {
            int body = _water.BodyIds[cell];

            if (body >= 0 && body < cells.Length)
                cells[body].Add(cell);
        }

        return cells;
    }

    private bool[] RiverBodies()
    {
        var rivers = new bool[_water.Bodies.Count];
        int resolution = _water.Resolution;
        float cell = _water.CellSize;

        foreach (RiverPath river in _water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                float reach = 0.5f * point.Width + RIVER_REACH + cell;
                int minC = Mathf.Max(0, Mathf.FloorToInt((point.Position.x - reach) / cell));
                int maxC = Mathf.Min(resolution - 1, Mathf.FloorToInt((point.Position.x + reach) / cell));
                int minR = Mathf.Max(0, Mathf.FloorToInt((point.Position.y - reach) / cell));
                int maxR = Mathf.Min(resolution - 1, Mathf.FloorToInt((point.Position.y + reach) / cell));

                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        int body = _water.BodyIds[r * resolution + c];

                        if (body >= 0 && body < rivers.Length)
                            rivers[body] = true;
                    }
                }
            }
        }

        return rivers;
    }

    private sealed class Window
    {
        public int OriginI, OriginJ, Width, Height;
        public float[] Ground;
        public bool[] Allowed;
        public bool[] Home;
        public bool[] Drain;
        public bool[] Inflow;

        public int Count => Width * Height;

        public bool Contains(int i, int j)
        {
            return i >= OriginI && j >= OriginJ && i < OriginI + Width && j < OriginJ + Height;
        }

        public int Local(int i, int j)
        {
            return (j - OriginJ) * Width + i - OriginI;
        }
    }

    private void Fill(int body, List<int> cells, bool river, float shelf)
    {
        WaterBody data = _water.Bodies[body];

        if (cells.Count == 0)
            return;

        Window window = Open(cells, data.Surface);
        MarkRivers(window, data.Surface);
        float surface = data.Surface;

        List<int> seeds = Seeds(window, body, surface);

        if (seeds.Count == 0)
        {
            Settle(body, surface, 0);
            return;
        }

        bool[] flooded = Flood(window, seeds, surface, body, out bool leak);

        if (leak && !river)
        {
            float spill = Spill(window, seeds);
            float lowered = spill - LEAK_MARGIN;

            if (lowered < surface)
            {
                surface = lowered;
                Lowered++;

                if (_water.SeaLevel > 0f && surface <= _water.SeaLevel + LEAK_MARGIN)
                {
                    Dropped++;
                    Settle(body, surface, 0);
                    return;
                }

                seeds = Seeds(window, body, surface);

                if (seeds.Count == 0)
                {
                    Dropped++;
                    Settle(body, surface, 0);
                    return;
                }

                flooded = Flood(window, seeds, surface, body, out _);
            }
        }

        DropFragments(window, flooded);
        Claim(window, flooded, body);
        SinkIslets(window, body, surface, shelf);

        int wet = 0;

        for (int n = 0; n < window.Count; n++)
        {
            if (flooded[n] && _owner[Global(window, n)] == body && window.Ground[n] < surface)
                wet++;
        }

        Settle(body, surface, wet);
    }

    private void Settle(int body, float surface, int wet)
    {
        WaterBody data = _water.Bodies[body];
        data.Surface = surface;
        data.Area = wet * _step * _step;
        _water.Bodies[body] = data;
    }

    private Window Open(List<int> cells, float surface)
    {
        int resolution = _water.Resolution;
        int minC = int.MaxValue, minR = int.MaxValue, maxC = int.MinValue, maxR = int.MinValue;

        foreach (int cell in cells)
        {
            minC = Mathf.Min(minC, cell % resolution);
            maxC = Mathf.Max(maxC, cell % resolution);
            minR = Mathf.Min(minR, cell / resolution);
            maxR = Mathf.Max(maxR, cell / resolution);
        }

        int margin = OWNERSHIP_REACH + 1;
        int c0 = Mathf.Max(0, minC - margin), c1 = Mathf.Min(resolution - 1, maxC + margin);
        int r0 = Mathf.Max(0, minR - margin), r1 = Mathf.Min(resolution - 1, maxR + margin);

        var window = new Window
        {
            OriginI = c0 * _sub,
            OriginJ = r0 * _sub,
            Width = (c1 - c0 + 1) * _sub + 1,
            Height = (r1 - r0 + 1) * _sub + 1
        };

        int cellsWide = c1 - c0 + 1;
        int cellsHigh = r1 - r0 + 1;
        var allowedCells = new bool[cellsWide * cellsHigh];
        var homeCells = new bool[cellsWide * cellsHigh];

        foreach (int cell in cells)
        {
            int c = cell % resolution - c0;
            int r = cell / resolution - r0;

            homeCells[r * cellsWide + c] = true;

            for (int dr = -OWNERSHIP_REACH; dr <= OWNERSHIP_REACH; dr++)
            {
                for (int dc = -OWNERSHIP_REACH; dc <= OWNERSHIP_REACH; dc++)
                {
                    int cc = c + dc, rr = r + dr;

                    if (cc < 0 || rr < 0 || cc >= cellsWide || rr >= cellsHigh)
                        continue;

                    if (_water.Filled != null && !Border(cc + c0, rr + r0) && _water.Filled[(rr + r0) * resolution + cc + c0] < surface - FILL_TOLERANCE)
                        continue;

                    allowedCells[rr * cellsWide + cc] = true;
                }
            }
        }

        window.Ground = new float[window.Count];
        window.Allowed = new bool[window.Count];
        window.Home = new bool[window.Count];

        Parallel.For(0, window.Height, row =>
        {
            int j = window.OriginJ + row;

            for (int column = 0; column < window.Width; column++)
            {
                int i = window.OriginI + column;
                int n = row * window.Width + column;

                window.Ground[n] = _map.SampleWorldSmooth(i * _step, j * _step);
                window.Allowed[n] = AnyCell(allowedCells, cellsWide, cellsHigh, column, row);
                window.Home[n] = AnyCell(homeCells, cellsWide, cellsHigh, column, row);
            }
        });

        return window;
    }

    private bool Border(int column, int row)
    {
        int last = _water.Resolution - 1 - OWNERSHIP_REACH;

        return column < OWNERSHIP_REACH || row < OWNERSHIP_REACH || column > last || row > last;
    }

    private bool AnyCell(bool[] cells, int wide, int high, int column, int row)
    {
        int c1 = Mathf.Min(wide - 1, column / _sub);
        int r1 = Mathf.Min(high - 1, row / _sub);
        int c0 = column % _sub == 0 ? Mathf.Max(0, c1 - 1) : c1;
        int r0 = row % _sub == 0 ? Mathf.Max(0, r1 - 1) : r1;

        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (cells[r * wide + c])
                    return true;
            }
        }

        return false;
    }

    private void MarkRivers(Window window, float surface)
    {
        window.Drain = new bool[window.Count];
        window.Inflow = new bool[window.Count];

        float x0 = window.OriginI * _step, z0 = window.OriginJ * _step;
        float x1 = (window.OriginI + window.Width - 1) * _step, z1 = (window.OriginJ + window.Height - 1) * _step;

        for (int river = 0; river < _water.Rivers.Count; river++)
        {
            List<RiverPoint> points = _water.Rivers[river].Points;
            float[] reaches = _water.CarveReach != null && river < _water.CarveReach.Count ? _water.CarveReach[river] : null;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i], b = points[i + 1];
                bool below = Below(points, i, surface);

                if (!below && !Above(points, i, surface))
                    continue;

                float bar = 0.5f * Mathf.Max(a.Width, b.Width) + BAR_PAD;
                float radius = below && reaches != null && i < reaches.Length ? Mathf.Max(bar, reaches[i]) : bar;

                if (Mathf.Max(a.Position.x, b.Position.x) + radius < x0 || Mathf.Min(a.Position.x, b.Position.x) - radius > x1)
                    continue;

                if (Mathf.Max(a.Position.y, b.Position.y) + radius < z0 || Mathf.Min(a.Position.y, b.Position.y) - radius > z1)
                    continue;

                if (below)
                    Capsule(window, window.Drain, a.Position, b.Position, radius, Below(points, i - 1, surface), true, true);
                else
                    Capsule(window, window.Inflow, a.Position, b.Position, radius, Above(points, i - 1, surface), Above(points, i + 1, surface), false);
            }
        }
    }

    private bool Uncut(Vector2 point, float ground)
    {
        return _water.Uncarved != null && ground >= _water.Uncarved.SampleWorldSmooth(point.x, point.y) - CARVE_EPSILON;
    }

    private void Capsule(Window window, bool[] mask, Vector2 a, Vector2 b, float radius, bool roundStart, bool roundEnd, bool carvedOnly)
    {
        int minI = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius) / _step) - window.OriginI);
        int maxI = Mathf.Min(window.Width - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius) / _step) - window.OriginI);
        int minJ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - radius) / _step) - window.OriginJ);
        int maxJ = Mathf.Min(window.Height - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + radius) / _step) - window.OriginJ);

        Vector2 axis = b - a;
        float length = axis.sqrMagnitude;
        float limit = radius * radius;

        for (int j = minJ; j <= maxJ; j++)
        {
            for (int i = minI; i <= maxI; i++)
            {
                var point = new Vector2((window.OriginI + i) * _step, (window.OriginJ + j) * _step);
                float along = length < 1e-8f ? 0f : Vector2.Dot(point - a, axis) / length;

                if (along < 0f && !roundStart || along > 1f && !roundEnd)
                    continue;

                float t = Mathf.Clamp01(along);

                if ((point - (a + axis * t)).sqrMagnitude <= limit && !(carvedOnly && Uncut(point, window.Ground[j * window.Width + i])))
                    mask[j * window.Width + i] = true;
            }
        }
    }

    private int Global(Window window, int local)
    {
        return (window.OriginJ + local / window.Width) * _nodes + window.OriginI + local % window.Width;
    }

    private bool Wall(Window window, int local, int body)
    {
        short owner = _owner[Global(window, local)];

        if (owner != WaterMap.OWNER_SEA && owner != body)
            return true;

        return window.Drain[local] || window.Inflow[local] || _water.SeaLevel > 0f && window.Ground[local] < _water.SeaLevel;
    }

    private List<int> Seeds(Window window, int body, float surface)
    {
        var seeds = new List<int>();

        for (int n = 0; n < window.Count; n++)
        {
            if (window.Home[n] && window.Ground[n] < surface && !Wall(window, n, body))
                seeds.Add(n);
        }

        return seeds;
    }

    private bool[] Flood(Window window, List<int> seeds, float surface, int body, out bool leak)
    {
        var flooded = new bool[window.Count];
        var queue = new Queue<int>();

        leak = false;

        foreach (int seed in seeds)
        {
            flooded[seed] = true;
            queue.Enqueue(seed);
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int column = current % window.Width;
            int row = current / window.Width;

            for (int direction = 0; direction < 4; direction++)
            {
                int c = column + StepX[direction];
                int r = row + StepZ[direction];

                if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                {
                    int gi = window.OriginI + c, gj = window.OriginJ + r;

                    if (gi >= 0 && gj >= 0 && gi < _nodes && gj < _nodes && Ground(gj * _nodes + gi) < surface)
                        leak = true;

                    continue;
                }

                int next = r * window.Width + c;

                if (flooded[next] || window.Inflow[next] || window.Ground[next] >= surface)
                    continue;

                if (window.Drain[next] || _water.SeaLevel > 0f && window.Ground[next] < _water.SeaLevel)
                {
                    leak = true;
                    continue;
                }

                short owner = _owner[Global(window, next)];

                if (owner != WaterMap.OWNER_SEA && owner != body)
                    continue;

                if (!window.Allowed[next])
                {
                    leak = true;
                    continue;
                }

                flooded[next] = true;
                queue.Enqueue(next);
            }
        }

        return flooded;
    }

    private float Spill(Window window, List<int> seeds)
    {
        var level = new float[window.Count];
        var heap = new MinHeap(seeds.Count * 2 + 64);

        for (int n = 0; n < level.Length; n++)
            level[n] = float.PositiveInfinity;

        foreach (int seed in seeds)
        {
            level[seed] = window.Ground[seed];
            heap.Push(seed, level[seed]);
        }

        while (heap.TryPop(out int current))
        {
            float here = level[current];
            int column = current % window.Width;
            int row = current / window.Width;

            if (!window.Allowed[current] || window.Drain[current] || _water.SeaLevel > 0f && window.Ground[current] < _water.SeaLevel)
                return here;

            for (int direction = 0; direction < 4; direction++)
            {
                int c = column + StepX[direction];
                int r = row + StepZ[direction];

                if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                    return here;

                int next = r * window.Width + c;
                short owner = _owner[Global(window, next)];

                if (owner != WaterMap.OWNER_SEA && owner >= 0)
                    continue;

                if (owner == WaterMap.OWNER_NONE || window.Inflow[next])
                    continue;

                float reach = Mathf.Max(here, window.Ground[next]);

                if (reach >= level[next])
                    continue;

                level[next] = reach;
                heap.Push(next, reach);
            }
        }

        return float.PositiveInfinity;
    }

    private void DropFragments(Window window, bool[] flooded)
    {
        var label = new int[window.Count];
        var sizes = new List<int> { 0 };
        var queue = new Queue<int>();

        for (int start = 0; start < window.Count; start++)
        {
            if (!flooded[start] || label[start] != 0)
                continue;

            int id = sizes.Count;
            int size = 0;

            label[start] = id;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int column = current % window.Width;
                int row = current / window.Width;

                size++;

                for (int direction = 0; direction < 4; direction++)
                {
                    int c = column + StepX[direction];
                    int r = row + StepZ[direction];

                    if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                        continue;

                    int next = r * window.Width + c;

                    if (!flooded[next] || label[next] != 0)
                        continue;

                    label[next] = id;
                    queue.Enqueue(next);
                }
            }

            sizes.Add(size);
        }

        int main = 0;

        for (int id = 1; id < sizes.Count; id++)
        {
            if (sizes[id] > sizes[main])
                main = id;
        }

        var keep = new bool[sizes.Count];

        for (int id = 1; id < sizes.Count; id++)
        {
            keep[id] = id == main || sizes[id] * _step * _step >= SATELLITE_AREA;

            if (!keep[id])
                Fragments++;
        }

        for (int n = 0; n < window.Count; n++)
        {
            if (flooded[n] && !keep[label[n]])
                flooded[n] = false;
        }
    }

    private void Claim(Window window, bool[] flooded, int body)
    {
        for (int n = 0; n < window.Count; n++)
        {
            if (flooded[n])
                _owner[Global(window, n)] = (short)body;
        }
    }

    private int Neighbours(Window window, int local, int body)
    {
        int column = local % window.Width;
        int row = local / window.Width;
        int count = 0;

        for (int direction = 0; direction < 4; direction++)
        {
            int c = column + StepX[direction];
            int r = row + StepZ[direction];

            if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                continue;

            if (_owner[Global(window, r * window.Width + c)] == body)
                count++;
        }

        return count;
    }

    private void SinkIslets(Window window, int body, float surface, float shelf)
    {
        var seen = new bool[window.Count];
        var queue = new Queue<int>();
        var members = new List<int>();

        for (int start = 0; start < window.Count; start++)
        {
            if (seen[start] || _owner[Global(window, start)] == body || !Neighbouring(window, start, body))
                continue;

            members.Clear();
            queue.Clear();
            seen[start] = true;
            queue.Enqueue(start);

            bool closed = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                members.Add(current);

                if (_owner[Global(window, current)] != WaterMap.OWNER_SEA || window.Ground[current] >= surface + shelf || members.Count > IsletNodes)
                    closed = false;

                int column = current % window.Width;
                int row = current / window.Width;

                for (int direction = 0; direction < 4; direction++)
                {
                    int c = column + StepX[direction];
                    int r = row + StepZ[direction];

                    if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                    {
                        closed = false;
                        continue;
                    }

                    int next = r * window.Width + c;

                    if (seen[next] || _owner[Global(window, next)] == body)
                        continue;

                    seen[next] = true;

                    if (members.Count + queue.Count <= IsletNodes)
                        queue.Enqueue(next);
                    else
                        closed = false;
                }
            }

            if (!closed)
                continue;

            Islets++;

            foreach (int member in members)
            {
                int node = Global(window, member);
                _owner[node] = (short)body;
                _sunk.Add(node);
            }
        }
    }

    private bool Neighbouring(Window window, int local, int body)
    {
        return Neighbours(window, local, body) > 0;
    }

    private void ClearSeaPuddles()
    {
        float sea = _water.SeaLevel;

        if (sea <= 0f)
            return;

        int count = _nodes * _nodes;
        var wet = new bool[count];
        var band = new bool[count];

        Parallel.For(0, _nodes, row =>
        {
            for (int column = 0; column < _nodes; column++)
            {
                int node = row * _nodes + column;

                if (_owner[node] != WaterMap.OWNER_SEA)
                    continue;

                float ground = _map.SampleWorldSmooth(column * _step, row * _step);
                wet[node] = ground < sea;
                band[node] = !wet[node] && WaterMap.MeshInside(sea, ground);
            }
        });

        var seen = new bool[count];
        var queue = new Queue<int>();
        var members = new List<int>();

        for (int start = 0; start < count; start++)
        {
            if (!wet[start] || seen[start])
                continue;

            members.Clear();
            seen[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                if (members.Count < MinFragment)
                    members.Add(current);

                int column = current % _nodes;
                int row = current / _nodes;

                for (int direction = 0; direction < 4; direction++)
                {
                    int c = column + StepX[direction];
                    int r = row + StepZ[direction];

                    if (c < 0 || r < 0 || c >= _nodes || r >= _nodes)
                        continue;

                    int next = r * _nodes + c;

                    if (!wet[next] || seen[next])
                        continue;

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            if (members.Count >= MinFragment)
                continue;

            SeaPuddles++;

            foreach (int member in members)
            {
                _owner[member] = WaterMap.OWNER_NONE;
                wet[member] = false;
            }
        }

        Parallel.For(0, _nodes, row =>
        {
            for (int column = 0; column < _nodes; column++)
            {
                int node = row * _nodes + column;

                if (!band[node])
                    continue;

                bool shore = column > 0 && wet[node - 1] || column + 1 < _nodes && wet[node + 1] || row > 0 && wet[node - _nodes] || row + 1 < _nodes && wet[node + _nodes];

                if (!shore)
                    _owner[node] = WaterMap.OWNER_NONE;
            }
        });
    }

    public void Publish()
    {
        WaterShore.Classify(_water, _map, (cell, i, j) => _owner[j * _nodes + i], _ => true);
    }
}

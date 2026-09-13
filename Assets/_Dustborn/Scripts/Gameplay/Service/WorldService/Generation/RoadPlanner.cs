using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class RoadPlanner
{
    private static readonly int[] STEP_X = { 1, 1, 0, -1, -1, -1, 0, 1, 2, 1, -1, -2, -2, -1, 1, 2 };
    private static readonly int[] STEP_Y = { 0, 1, 1, 1, 0, -1, -1, -1, 1, 2, 2, 1, -1, -2, -2, -1 };

    private const int HEADINGS = 16;
    private const int FREE_HEADING = HEADINGS;
    private const int STATES = HEADINGS + 1;
    private const float FORD_PENALTY = 8f;
    private const float MERGE_REACH = 3f;

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;
    private readonly RoadSmoother _smoother;

    private readonly int _resolution;
    private readonly float _cellSize;

    private readonly Vector2[] _headings = new Vector2[HEADINGS];

    private readonly float[] _cost;
    private readonly int[] _previous;
    private readonly bool[] _closed;
    private readonly bool[] _used;
    private readonly bool[] _settled;
    private readonly MinHeap _open;
    private readonly float[] _stepCost;
    private readonly float[] _turnCost;

    public int SettledCells { get; private set; }

    public RoadPlanner(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;

        _cellSize = config.RoadCellSize;
        _resolution = Mathf.Max(2, Mathf.RoundToInt(config.WorldSize / _cellSize));

        for (int i = 0; i < HEADINGS; i++)
            _headings[i] = new Vector2(STEP_X[i], STEP_Y[i]).normalized;

        int cellCount = _resolution * _resolution;

        _cost = new float[cellCount * STATES];
        _previous = new int[cellCount * STATES];
        _closed = new bool[cellCount * STATES];
        _used = new bool[cellCount];
        _settled = new bool[cellCount];
        _open = new MinHeap(cellCount / 2);
        _stepCost = new float[cellCount * HEADINGS];
        _turnCost = new float[STATES * HEADINGS];

        PrecomputeSteps();

        _smoother = new RoadSmoother(map)
        {
            SimplifyTolerance = config.RoadSimplifyTolerance,
            ChaikinPasses = config.RoadSmoothPasses,
            RelaxPasses = config.RoadRelaxPasses,
            RelaxStrength = config.RoadRelaxStrength,
            Corridor = config.RoadCorridor,
            MaxGrade = config.RoadMaxGrade,
            Spacing = config.RoadPointSpacing
        };
    }

    public List<Road> Plan(IReadOnlyList<Hub> hubs, IReadOnlyList<(int From, int To)> links, IReadOnlyList<SettlementLayout> layouts)
    {
        var roads = new List<Road>();

        if (hubs.Count < 2)
        {
            Debug.LogWarning($"No roads built: {hubs.Count} settlements, at least 2 are needed");

            return roads;
        }

        MarkSettlements(layouts);

        var built = new RoadProximity(_config.WorldSize, _cellSize);

        foreach ((int from, int to) in links)
        {
            Vector2 start = Gate(hubs, layouts, from, to);
            Vector2 end = Gate(hubs, layouts, to, from);

            List<int> path = FindPath(start, end);

            if (path == null)
            {
                Debug.LogWarning($"Could not route a road between settlements {from} and {to}");
                continue;
            }

            foreach (int cell in path)
                _used[cell] = true;

            Vector2[] points = path.Count < 2 ? new[] { start, end } : ToWorld(path);

            points[0] = start;
            points[^1] = end;

            var road = new Road(Merge(_smoother.Smooth(points), built), _config.RoadHalfWidth * 2f);

            built.Add(road);
            roads.Add(road);
        }

        return roads;
    }

    private Vector2[] Merge(Vector2[] points, RoadProximity built)
    {
        if (built.SegmentCount == 0 || points.Length < 3)
            return points;

        float reach = _config.RoadHalfWidth * MERGE_REACH;
        float hold = reach * 0.5f;
        var merged = (Vector2[])points.Clone();

        for (int i = 1; i < points.Length - 1; i++)
        {
            if (!built.TryNearestPoint(points[i], reach, out Vector2 nearest))
                continue;

            float distance = Vector2.Distance(points[i], nearest);
            float weight = 1f - Mathf.SmoothStep(0f, 1f, (distance - hold) / (reach - hold));

            merged[i] = Vector2.Lerp(points[i], nearest, weight);
        }

        return merged;
    }

    private static Vector2 Gate(IReadOnlyList<Hub> hubs, IReadOnlyList<SettlementLayout> layouts, int from, int to)
    {
        if (layouts == null || from >= layouts.Count || layouts[from] == null)
            return hubs[from].Position;

        return layouts[from].GateToward(to);
    }

    private void MarkSettlements(IReadOnlyList<SettlementLayout> layouts)
    {
        System.Array.Clear(_settled, 0, _settled.Length);
        SettledCells = 0;

        if (layouts == null)
            return;

        float half = _cellSize * 0.5f;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            int minX = Mathf.Clamp(Mathf.FloorToInt((layout.Min.x - half) / _cellSize), 0, _resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((layout.Max.x + half) / _cellSize), 0, _resolution - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((layout.Min.y - half) / _cellSize), 0, _resolution - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((layout.Max.y + half) / _cellSize), 0, _resolution - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = y * _resolution + x;

                    if (_settled[cell])
                        continue;

                    var center = new Vector2((x + 0.5f) * _cellSize, (y + 0.5f) * _cellSize);

                    if (!Touches(layout, center, half))
                        continue;

                    _settled[cell] = true;
                    SettledCells++;
                }
            }
        }
    }

    private static bool Touches(SettlementLayout layout, Vector2 center, float half)
    {
        return layout.Contains(center)
            || layout.Contains(center + new Vector2(half, half))
            || layout.Contains(center + new Vector2(-half, half))
            || layout.Contains(center + new Vector2(half, -half))
            || layout.Contains(center + new Vector2(-half, -half));
    }

    private List<int> FindPath(Vector2 from, Vector2 to)
    {
        int start = CellOf(from);
        int goal = CellOf(to);

        System.Array.Fill(_cost, float.MaxValue);
        System.Array.Fill(_previous, -1);
        System.Array.Clear(_closed, 0, _closed.Length);

        int startState = start * STATES + FREE_HEADING;

        _open.Clear();
        _cost[startState] = 0f;
        _open.Push(startState, 0f);

        int goalX = goal % _resolution;
        int goalY = goal / _resolution;
        int reached = -1;

        while (_open.TryPop(out int current))
        {
            int cell = current / STATES;

            if (cell == goal)
            {
                reached = current;
                break;
            }

            if (_closed[current])
                continue;

            _closed[current] = true;

            int heading = current % STATES;
            int x = cell % _resolution;
            int y = cell / _resolution;

            for (int step = 0; step < HEADINGS; step++)
            {
                int nextX = x + STEP_X[step];
                int nextY = y + STEP_Y[step];

                if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                    continue;

                int nextCell = nextY * _resolution + nextX;
                int next = nextCell * STATES + step;

                if (_closed[next])
                    continue;

                float stepCost = _stepCost[cell * HEADINGS + step];

                if (_settled[nextCell] && nextCell != goal)
                    stepCost *= _config.SettlementRoadPenalty;

                if (_used[nextCell])
                    stepCost *= _config.RoadReuseDiscount;

                float candidate = _cost[current] + (stepCost + _turnCost[heading * HEADINGS + step]);

                if (candidate >= _cost[next])
                    continue;

                _cost[next] = candidate;
                _previous[next] = current;

                _open.Push(next, candidate + Heuristic(nextX, nextY, goalX, goalY));
            }
        }

        if (goal == start)
            return new List<int> { start };

        if (reached < 0)
            return null;

        var path = new List<int>();

        for (int state = reached; state >= 0; state = _previous[state])
        {
            path.Add(state / STATES);

            if (state == startState)
                break;
        }

        path.Reverse();

        return path;
    }

    private void PrecomputeSteps()
    {
        Parallel.For(0, _resolution, y =>
        {
            for (int x = 0; x < _resolution; x++)
            {
                for (int step = 0; step < HEADINGS; step++)
                {
                    int nextX = x + STEP_X[step];
                    int nextY = y + STEP_Y[step];

                    if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                        continue;

                    _stepCost[(y * _resolution + x) * HEADINGS + step] = BaseCost(x, y, nextX, nextY);
                }
            }
        });

        for (int heading = 0; heading < STATES; heading++)
        {
            for (int step = 0; step < HEADINGS; step++)
                _turnCost[heading * HEADINGS + step] = TurnCost(heading, step);
        }
    }

    private float BaseCost(int fromX, int fromY, int toX, int toY)
    {
        float deltaX = (toX - fromX) * _cellSize;
        float deltaY = (toY - fromY) * _cellSize;
        float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

        float fromHeight = SampleMeters(fromX, fromY);
        float toHeight = SampleMeters(toX, toY);
        float grade = Mathf.Abs(toHeight - fromHeight) / distance;
        float cross = CrossSlope(fromX, fromY, toX, toY, deltaX, deltaY, distance);

        float cost = distance * (1f + _config.RoadSlopePenalty * grade * grade + _config.RoadCrossSlopePenalty * cross * cross);

        if (_config.SeaLevel > 0f && toHeight < _config.SeaLevel + _config.ShoreMargin)
            cost *= FORD_PENALTY;

        return cost;
    }

    private float TurnCost(int heading, int step)
    {
        if (heading == FREE_HEADING || _config.RoadTurnPenalty <= 0f)
            return 0f;

        float dot = Mathf.Clamp(Vector2.Dot(_headings[heading], _headings[step]), -1f, 1f);

        return Mathf.Acos(dot) * _config.RoadTurnPenalty;
    }

    private float CrossSlope(int fromX, int fromY, int toX, int toY, float deltaX, float deltaY, float distance)
    {
        if (_config.RoadCrossSlopePenalty <= 0f)
            return 0f;

        float midX = (fromX + toX + 1f) * 0.5f * _cellSize;
        float midY = (fromY + toY + 1f) * 0.5f * _cellSize;

        float perpendicularX = -deltaY / distance;
        float perpendicularY = deltaX / distance;

        float probe = _config.RoadHalfWidth + _config.RoadShoulder;

        float left = _map.SampleWorld(new Vector3(midX - perpendicularX * probe, 0f, midY - perpendicularY * probe));
        float right = _map.SampleWorld(new Vector3(midX + perpendicularX * probe, 0f, midY + perpendicularY * probe));

        return Mathf.Abs(right - left) / (2f * probe);
    }

    private float Heuristic(int x, int y, int goalX, int goalY)
    {
        float deltaX = (x - goalX) * _cellSize;
        float deltaY = (y - goalY) * _cellSize;

        return Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private float SampleMeters(int cellX, int cellY)
    {
        return _map.SampleWorld(new Vector3((cellX + 0.5f) * _cellSize, 0f, (cellY + 0.5f) * _cellSize));
    }

    private int CellOf(Vector2 position)
    {
        int x = Mathf.Clamp((int)(position.x / _cellSize), 0, _resolution - 1);
        int y = Mathf.Clamp((int)(position.y / _cellSize), 0, _resolution - 1);

        return y * _resolution + x;
    }

    private Vector2[] ToWorld(List<int> path)
    {
        var points = new Vector2[path.Count];

        for (int i = 0; i < path.Count; i++)
        {
            int cell = path[i];

            points[i] = new Vector2((cell % _resolution + 0.5f) * _cellSize, (cell / _resolution + 0.5f) * _cellSize);
        }

        return points;
    }
}

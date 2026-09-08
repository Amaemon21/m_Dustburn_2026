using System.Collections.Generic;
using UnityEngine;

public class RoadPlanner
{
    private static readonly int[] STEP_X = { 1, 1, 0, -1, -1, -1, 0, 1, 2, 1, -1, -2, -2, -1, 1, 2 };
    private static readonly int[] STEP_Y = { 0, 1, 1, 1, 0, -1, -1, -1, 1, 2, 2, 1, -1, -2, -2, -1 };

    private const int HEADINGS = 16;
    private const int FREE_HEADING = HEADINGS;
    private const int STATES = HEADINGS + 1;
    private const float FORD_PENALTY = 8f;

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
    private readonly MinHeap _open;

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
        _open = new MinHeap(cellCount / 2);

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

    public List<Road> Plan(List<Hub> hubs)
    {
        var roads = new List<Road>();

        if (hubs.Count < 2)
        {
            Debug.LogWarning($"No roads built: {hubs.Count} hubs, at least 2 are needed");

            return roads;
        }

        foreach ((int from, int to) in BuildEdges(hubs))
        {
            List<int> path = FindPath(hubs[from], hubs[to]);

            if (path == null)
            {
                Debug.LogWarning($"Could not route a road between hubs {from} and {to}");
                continue;
            }

            foreach (int cell in path)
                _used[cell] = true;

            Vector2[] points = ToWorld(path);

            points[0] = hubs[from].Position;
            points[^1] = hubs[to].Position;

            roads.Add(new Road(_smoother.Smooth(points), _config.RoadHalfWidth * 2f));
        }

        return roads;
    }

    private List<(int, int)> BuildEdges(List<Hub> hubs)
    {
        int count = hubs.Count;

        var inTree = new bool[count];
        var best = new float[count];
        var parent = new int[count];
        var edges = new List<(int, int)>();

        for (int i = 0; i < count; i++)
        {
            best[i] = float.MaxValue;
            parent[i] = -1;
        }

        best[0] = 0f;

        for (int step = 0; step < count; step++)
        {
            int current = -1;

            for (int i = 0; i < count; i++)
            {
                if (inTree[i])
                    continue;

                if (current < 0 || best[i] < best[current])
                    current = i;
            }

            inTree[current] = true;

            if (parent[current] >= 0)
                edges.Add((parent[current], current));

            for (int i = 0; i < count; i++)
            {
                if (inTree[i])
                    continue;

                float distance = Vector2.Distance(hubs[current].Position, hubs[i].Position);

                if (distance >= best[i])
                    continue;

                best[i] = distance;
                parent[i] = current;
            }
        }

        AddExtraEdges(hubs, edges);

        return edges;
    }

    private void AddExtraEdges(List<Hub> hubs, List<(int, int)> edges)
    {
        if (_config.RoadExtraEdges <= 0)
            return;

        var candidates = new List<(int from, int to, float distance)>();

        for (int i = 0; i < hubs.Count; i++)
        {
            for (int j = i + 1; j < hubs.Count; j++)
            {
                if (edges.Contains((i, j)) || edges.Contains((j, i)))
                    continue;

                candidates.Add((i, j, Vector2.Distance(hubs[i].Position, hubs[j].Position)));
            }
        }

        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

        int extra = Mathf.Min(_config.RoadExtraEdges, candidates.Count);

        for (int i = 0; i < extra; i++)
            edges.Add((candidates[i].from, candidates[i].to));
    }

    private List<int> FindPath(Hub from, Hub to)
    {
        int start = CellOf(from.Position);
        int goal = CellOf(to.Position);

        for (int i = 0; i < _cost.Length; i++)
        {
            _cost[i] = float.MaxValue;
            _previous[i] = -1;
            _closed[i] = false;
        }

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

                int next = (nextY * _resolution + nextX) * STATES + step;

                if (_closed[next])
                    continue;

                float candidate = _cost[current] + StepCost(x, y, nextX, nextY, heading, step, _used[nextY * _resolution + nextX]);

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

    private float StepCost(int fromX, int fromY, int toX, int toY, int heading, int step, bool reused)
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

        if (reused)
            cost *= _config.RoadReuseDiscount;

        return cost + TurnCost(heading, step);
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

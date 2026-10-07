using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class StandingWaterRegions
{
    private void MarkOcean()
    {
        Array.Clear(_ocean, 0, _ocean.Length);
        float sea = _water.SeaLevel;

        if (sea <= 0f)
            return;

        var below = new bool[_ocean.Length];
        var passable = new bool[_ocean.Length];

        Parallel.For(0, _nodes, row =>
        {
            for (int column = 0; column < _nodes; column++)
            {
                float ground = _map.SampleWorldSmooth(column * _step, row * _step);
                below[row * _nodes + column] = ground < sea;
                passable[row * _nodes + column] = ground < sea + WaterMap.LAGOON_BAR;
            }
        });

        var reached = new bool[_ocean.Length];

        var queue = new Queue<int>();

        for (int node = 0; node < _ocean.Length; node++)
        {
            int column = node % _nodes, row = node / _nodes;

            if (!below[node] || column != 0 && row != 0 && column != _nodes - 1 && row != _nodes - 1)
                continue;

            reached[node] = true;
            queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int column = current % _nodes, row = current / _nodes;

            for (int direction = 0; direction < 4; direction++)
            {
                int c = column + StepX[direction], r = row + StepZ[direction];

                if (c < 0 || r < 0 || c >= _nodes || r >= _nodes)
                    continue;

                int next = r * _nodes + c;

                if (reached[next] || !passable[next])
                    continue;

                reached[next] = true;
                queue.Enqueue(next);
            }
        }

        for (int node = 0; node < _ocean.Length; node++)
            _ocean[node] = reached[node] && below[node];
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

        return !(a.Submerged && b.Submerged) && Mathf.Max(a.Surface, b.Surface) > surface + WaterMeshes.MOUTH_BLEND;
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
                    _barred[j * _nodes + i] = true;
            }
        }
    }

    private void KeepSeaOffRivers()
    {
        for (int node = 0; node < _owner.Length; node++)
        {
            if (_barred[node] && _owner[node] == WaterMap.OWNER_SEA)
                _owner[node] = WaterMap.OWNER_NONE;
        }
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
                wet[node] = ground < sea && _ocean[node];

                if (ground < sea && !_ocean[node])
                    _owner[node] = WaterMap.OWNER_NONE;
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
}

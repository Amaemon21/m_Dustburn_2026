using System.Collections.Generic;
using UnityEngine;

public sealed partial class StandingWaterRegions
{
    private void Fill(int body, List<int> cells, bool river, float shelf)
    {
        WaterBody data = _water.Bodies[body];

        if (cells.Count == 0)
            return;

        Window window = Open(cells, data.Surface);
        MarkRivers(window, data.Surface);
        ClearBowl(window, data);
        float surface = data.Surface;

        List<int> seeds = Seeds(window, body, surface);

        if (seeds.Count == 0)
        {
            Settle(body, surface, 0);
            return;
        }

        bool[] flooded = Flood(window, seeds, surface, body, out bool leak);

        bool toOcean = false;
        float spill = float.PositiveInfinity;

        if (leak)
            spill = Spill(window, seeds, out toOcean);

        if (leak && (!river || toOcean))
        {
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

    private bool Wall(Window window, int local, int body)
    {
        short owner = _owner[Global(window, local)];

        if (owner != WaterMap.OWNER_SEA && owner != body)
            return true;

        return window.Drain[local] || window.Inflow[local] || _ocean[Global(window, local)];
    }

    private List<int> Seeds(Window window, int body, float surface)
    {
        var seeds = new List<int>();

        WaterBody data = _water.Bodies[body];
        float limit = data.SeedRadius * data.SeedRadius;

        for (int n = 0; n < window.Count; n++)
        {
            if (!window.Home[n] || window.Ground[n] >= surface || Wall(window, n, body))
                continue;

            if (data.SeedRadius > 0f && (new Vector2((window.OriginI + n % window.Width) * _step, (window.OriginJ + n / window.Width) * _step) - data.Center).sqrMagnitude > limit)
                continue;

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

                if (window.Drain[next] || _ocean[Global(window, next)])
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

    private float Spill(Window window, List<int> seeds, out bool toOcean)
    {
        toOcean = false;

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

            if (window.Drain[current])
                return Mathf.Max(here, window.DrainLevel[current]);

            toOcean = _ocean[Global(window, current)];

            if (!window.Allowed[current] || toOcean)
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

                if (owner == WaterMap.OWNER_NONE || _barred[Global(window, next)] || window.Inflow[next])
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

    private bool LowerSteps()
    {
        var targets = new Dictionary<int, float>();

        for (int node = 0; node < _owner.Length; node++)
        {
            int column = node % _nodes, row = node / _nodes;

            for (int direction = 0; direction < 2; direction++)
            {
                int c = column + StepX[direction], r = row + StepZ[direction];

                if (c >= _nodes || r >= _nodes)
                    continue;

                int next = r * _nodes + c;
                short a = _owner[node], b = _owner[next];

                if (a < 0 || b < 0 || a == b)
                    continue;

                float levelA = _water.Bodies[a].Surface, levelB = _water.Bodies[b].Surface;

                if (Mathf.Abs(levelA - levelB) <= WaterMeshes.MOUTH_BLEND)
                    continue;

                int high = levelA > levelB ? a : b;
                int shore = levelA > levelB ? node : next;
                float target = Mathf.Max(Mathf.Min(levelA, levelB), Ground(shore) - LEAK_MARGIN);

                if (target < _water.Bodies[high].Surface)
                    targets[high] = targets.TryGetValue(high, out float known) ? Mathf.Min(known, target) : target;
            }
        }

        foreach (KeyValuePair<int, float> pair in targets)
        {
            WaterBody data = _water.Bodies[pair.Key];
            data.Surface = pair.Value;
            _water.Bodies[pair.Key] = data;
            Stepped++;
        }

        return targets.Count > 0;
    }
}

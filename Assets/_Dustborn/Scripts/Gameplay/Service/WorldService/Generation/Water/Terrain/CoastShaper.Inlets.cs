using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static partial class CoastShaper
{
    public static int LiftInland(HeightMap map, float seaLevel, float margin, out int inlets)
    {
        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        int cells = Mathf.Max(2, Mathf.CeilToInt(map.WorldSize / FIELD_STEP));
        float cellSize = map.WorldSize / (float)cells;
        float sea = seaLevel / map.MaxHeight;
        float target = (seaLevel + margin) / map.MaxHeight;
        float[] heights = map.Heights;
        var lowest = new float[cells * cells];

        Parallel.For(0, cells, row =>
        {
            int z0 = Mathf.FloorToInt(row * cellSize / step), z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((row + 1) * cellSize / step));

            for (int column = 0; column < cells; column++)
            {
                int x0 = Mathf.FloorToInt(column * cellSize / step), x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((column + 1) * cellSize / step));
                float low = float.MaxValue;

                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        low = Mathf.Min(low, heights[z * resolution + x]);

                lowest[row * cells + column] = low;
            }
        });

        bool[] ocean = Ocean(lowest, cells, sea);
        inlets = OpenInlets(map, lowest, ocean, cells, cellSize, seaLevel);
        int lifted = 0;
        object gate = new();

        Parallel.For(0, resolution, row =>
        {
            int count = 0;
            int coarseRow = Mathf.Min(cells - 1, (int)(row * step / cellSize));

            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;

                if (heights[index] >= sea)
                    continue;

                int coarse = coarseRow * cells + Mathf.Min(cells - 1, (int)(column * step / cellSize));

                if (NearOcean(ocean, cells, coarse))
                    continue;

                heights[index] = target;
                count++;
            }

            lock (gate)
                lifted += count;
        });

        return lifted;
    }

    public static int OpenInlets(HeightMap map, float[] lowest, bool[] ocean, int cells, float cellSize, float seaLevel)
    {
        float sea = seaLevel / map.MaxHeight;
        var pocket = new int[cells * cells];
        Array.Fill(pocket, -1);
        var members = new List<int>();
        var cost = new Dictionary<int, float>();
        var parent = new Dictionary<int, int>();
        var heap = new MinHeap(256);
        int pockets = 0, opened = 0;

        for (int seed = 0; seed < pocket.Length; seed++)
        {
            if (pocket[seed] >= 0 || ocean[seed] || lowest[seed] >= sea)
                continue;

            Label(lowest, ocean, pocket, cells, sea, seed, pockets++, members);

            if (!Inlet(map, lowest, ocean, pocket, cells, cellSize, sea, members, cost, parent, heap, out List<int> path))
                continue;

            Carve(map, path, ocean, cells, cellSize, sea, sea - INLET_DEPTH / map.MaxHeight);

            foreach (int cell in members)
                ocean[cell] = true;

            foreach (int cell in path)
                ocean[cell] = true;

            opened++;
        }

        return opened;
    }

    private static void Label(float[] lowest, bool[] ocean, int[] pocket, int cells, float sea, int seed, int id, List<int> members)
    {
        var queue = new Queue<int>();
        members.Clear();
        pocket[seed] = id;
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            members.Add(current);
            int column = current % cells, row = current / cells;

            for (int direction = 0; direction < 4; direction++)
            {
                int c = column + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                int r = row + (direction == 2 ? 1 : direction == 3 ? -1 : 0);

                if (c < 0 || r < 0 || c >= cells || r >= cells)
                    continue;

                int next = r * cells + c;

                if (pocket[next] >= 0 || ocean[next] || lowest[next] >= sea)
                    continue;

                pocket[next] = id;
                queue.Enqueue(next);
            }
        }
    }

    private static bool Inlet(HeightMap map, float[] lowest, bool[] ocean, int[] pocket, int cells, float cellSize, float sea, List<int> members,
        Dictionary<int, float> cost, Dictionary<int, int> parent, MinHeap heap, out List<int> path)
    {
        cost.Clear();
        parent.Clear();
        heap.Clear();
        int id = pocket[members[0]];
        float budget = INLET_MAX_LENGTH * (1f + INLET_CUT_WEIGHT * INLET_MAX_CUT);

        foreach (int cell in members)
        {
            cost[cell] = 0f;
            heap.Push(cell, 0f);
        }

        int reached = -1;

        while (heap.TryPop(out int current))
        {
            float spent = cost[current];

            if (ocean[current])
            {
                reached = current;
                break;
            }

            if (spent > budget)
                break;

            int column = current % cells, row = current / cells;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int c = column + dx, r = row + dz;

                    if (dx == 0 && dz == 0 || c < 0 || r < 0 || c >= cells || r >= cells)
                        continue;

                    int next = r * cells + c;

                    if (pocket[next] == id)
                        continue;

                    float cut = Mathf.Max(0f, lowest[next] - sea) * map.MaxHeight;
                    float total = spent + cellSize * (dx != 0 && dz != 0 ? DIAGONAL : 1f) * (1f + INLET_CUT_WEIGHT * cut);

                    if (cost.TryGetValue(next, out float known) && known <= total)
                        continue;

                    cost[next] = total;
                    parent[next] = current;
                    heap.Push(next, total);
                }
            }
        }

        return Trace(map, lowest, parent, reached, cellSize, sea, out path);
    }

    private static bool Trace(HeightMap map, float[] lowest, Dictionary<int, int> parent, int reached, float cellSize, float sea, out List<int> path)
    {
        path = null;

        if (reached < 0)
            return false;

        path = new List<int>();
        float deepest = 0f;
        int walk = reached;

        while (parent.TryGetValue(walk, out int previous))
        {
            path.Add(walk);
            deepest = Mathf.Max(deepest, (lowest[walk] - sea) * map.MaxHeight);
            walk = previous;
        }

        path.Add(walk);

        return path.Count * cellSize <= INLET_MAX_LENGTH && deepest <= INLET_MAX_CUT;
    }

    private static void Carve(HeightMap map, List<int> path, bool[] ocean, int cells, float cellSize, float sea, float floor)
    {
        int resolution = map.Resolution;
        float step = map.WorldSize / (float)(resolution - 1);
        float reach = INLET_HALF_WIDTH + INLET_BANK;
        int span = Mathf.CeilToInt(reach / step);
        float[] heights = map.Heights;

        foreach (int cell in path)
        {
            float cx = (cell % cells + 0.5f) * cellSize, cz = (cell / cells + 0.5f) * cellSize;
            int x0 = Mathf.RoundToInt(cx / step), z0 = Mathf.RoundToInt(cz / step);

            for (int z = Mathf.Max(0, z0 - span); z <= Mathf.Min(resolution - 1, z0 + span); z++)
            {
                for (int x = Mathf.Max(0, x0 - span); x <= Mathf.Min(resolution - 1, x0 + span); x++)
                {
                    float dx = x * step - cx, dz = z * step - cz;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);

                    if (distance >= reach)
                        continue;

                    int index = z * resolution + x;
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, (distance - INLET_HALF_WIDTH) / INLET_BANK);
                    heights[index] = Mathf.Min(heights[index], Mathf.Lerp(heights[index], floor, weight));

                    if (heights[index] < sea)
                        ocean[Mathf.Min(cells - 1, (int)(z * step / cellSize)) * cells + Mathf.Min(cells - 1, (int)(x * step / cellSize))] = true;
                }
            }
        }
    }

    private static bool[] Ocean(float[] lowest, int cells, float sea)
    {
        var ocean = new bool[cells * cells];
        var queue = new Queue<int>();

        for (int i = 0; i < ocean.Length; i++)
        {
            int column = i % cells, row = i / cells;

            if (column != 0 && row != 0 && column != cells - 1 && row != cells - 1 || lowest[i] >= sea)
                continue;

            ocean[i] = true;
            queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int column = current % cells, row = current / cells;

            for (int direction = 0; direction < 4; direction++)
            {
                int c = column + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                int r = row + (direction == 2 ? 1 : direction == 3 ? -1 : 0);

                if (c < 0 || r < 0 || c >= cells || r >= cells)
                    continue;

                int next = r * cells + c;

                if (ocean[next] || lowest[next] >= sea)
                    continue;

                ocean[next] = true;
                queue.Enqueue(next);
            }
        }

        return ocean;
    }

    private static bool NearOcean(bool[] ocean, int cells, int coarse)
    {
        int column = coarse % cells, row = coarse / cells;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int c = column + dx, r = row + dz;

                if (c >= 0 && r >= 0 && c < cells && r < cells && ocean[r * cells + c])
                    return true;
            }
        }

        return false;
    }
}

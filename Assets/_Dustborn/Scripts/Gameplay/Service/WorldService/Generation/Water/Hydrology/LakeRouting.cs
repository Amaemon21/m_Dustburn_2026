using System.Collections.Generic;
using UnityEngine;

public static class LakeRouting
{
    private const float LAKE_STEP = 0.1f;
    private const float CLIMB_PENALTY = 4f;
    private const float FLOODED = 1e-4f;
    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public static int Apply(HydrologyGrid grid, WaterMap water, IReadOnlyList<HydrologyBasin> owners)
    {
        int count = grid.Receiver.Length;
        var region = new int[count];
        System.Array.Fill(region, -1);
        var cells = new List<int>();
        var cost = new Dictionary<int, float>();
        var heap = new MinHeap(256);
        int rerouted = 0;

        for (int id = 0; id < owners.Count; id++)
        {
            int lakeCell = LakeCell(water, owners[id], id);

            if (lakeCell < 0 || region[lakeCell] >= 0)
                continue;

            Flooded(grid, lakeCell, id, region, cells);

            if (Exit(grid, region, cells, id, lakeCell, out int sill))
                rerouted += Reroute(grid, water, region, id, sill, cost, heap);
        }

        if (rerouted > 0)
            Accumulate(grid);

        return rerouted;
    }

    private static int LakeCell(WaterMap water, HydrologyBasin basin, int id)
    {
        foreach (int cell in basin.Cells)
        {
            if (water.BodyIds[cell] == id)
                return cell;
        }

        return -1;
    }

    private static void Flooded(HydrologyGrid grid, int start, int id, int[] region, List<int> cells)
    {
        int resolution = grid.Resolution;
        var queue = new Queue<int>();
        cells.Clear();
        region[start] = id;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            cells.Add(current);
            int column = current % resolution, row = current / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;

                if (region[next] >= 0 || grid.Outlet[next] || grid.Routing[next] - grid.Height[next] <= FLOODED)
                    continue;

                region[next] = id;
                queue.Enqueue(next);
            }
        }
    }

    private static bool Exit(HydrologyGrid grid, int[] region, List<int> cells, int id, int start, out int sill)
    {
        sill = -1;
        float lowest = float.MaxValue;

        foreach (int cell in cells)
            lowest = Mathf.Min(lowest, grid.Routing[cell]);

        int current = start;

        for (int guard = 0; guard < grid.Receiver.Length; guard++)
        {
            int next = grid.Receiver[current];

            if (next < 0)
                return false;

            if (region[next] >= 0 && region[next] != id)
                return false;

            if (region[next] != id && grid.Routing[next] < lowest)
            {
                sill = current;
                return true;
            }

            if (region[next] != id)
            {
                region[next] = id;
                cells.Add(next);
            }

            current = next;
        }

        return false;
    }

    private static int Reroute(HydrologyGrid grid, WaterMap water, int[] region, int id, int sill, Dictionary<int, float> cost, MinHeap heap)
    {
        int resolution = grid.Resolution;
        float diagonal = Mathf.Sqrt(2f);
        cost.Clear();
        cost[sill] = 0f;
        heap.Push(sill, 0f);
        int changed = 0;

        while (heap.TryPop(out int current))
        {
            float reached = cost[current];
            int column = current % resolution, row = current / resolution;
            bool lake = water.BodyIds[current] >= 0;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;

                if (region[next] != id || next == sill)
                    continue;

                float length = (direction & 1) == 1 ? diagonal : 1f;
                float step = lake && water.BodyIds[next] >= 0 ? LAKE_STEP * length : length;
                float climb = Mathf.Max(0f, grid.Height[current] - grid.Height[next]) / grid.CellSize;
                float total = reached + step + CLIMB_PENALTY * climb;

                if (cost.TryGetValue(next, out float known) && known <= total)
                    continue;

                cost[next] = total;
                heap.Push(next, total);

                if (grid.Receiver[next] != current)
                    changed++;

                grid.Receiver[next] = current;
            }
        }

        return changed;
    }

    private static void Accumulate(HydrologyGrid grid)
    {
        int count = grid.Receiver.Length;
        var pending = new int[count];
        var ready = new Queue<int>();

        for (int i = 0; i < count; i++)
        {
            grid.Accumulation[i] = grid.Runoff == null ? 1f : grid.Runoff[i];

            if (grid.Receiver[i] >= 0)
                pending[grid.Receiver[i]]++;
        }

        for (int i = 0; i < count; i++)
        {
            if (pending[i] == 0)
                ready.Enqueue(i);
        }

        int placed = 0;

        while (ready.Count > 0)
        {
            int cell = ready.Dequeue();
            placed++;
            int receiver = grid.Receiver[cell];

            if (receiver < 0)
                continue;

            grid.Accumulation[receiver] += grid.Accumulation[cell];

            if (--pending[receiver] == 0)
                ready.Enqueue(receiver);
        }

        if (placed != count)
            throw new System.InvalidOperationException($"Lake routing left a cycle through {count - placed} hydrology cells");
    }
}

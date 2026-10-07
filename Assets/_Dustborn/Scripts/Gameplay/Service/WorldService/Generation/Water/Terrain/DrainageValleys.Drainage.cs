using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static partial class DrainageValleys
{
    private sealed class Grid
    {
        public int Resolution;
        public float Cell;
        public float[] Height;
        public float[] Filled;
        public float[] Discharge;
        public float[] Runoff;
        public int[] Receiver;
        public bool[] Outlet;
        public bool[] Dry;
        public float DryRise;

        public Vector2 Center(int cell)
        {
            return new Vector2((cell % Resolution + 0.5f) * Cell, (cell / Resolution + 0.5f) * Cell);
        }
    }

    private static Grid Sample(HeightMap map, WorldGenerationConfig config, WaterClimate climate)
    {
        int resolution = Mathf.Max(2, Mathf.CeilToInt(config.WorldSize / config.Water.ValleyCellSize));
        float cell = config.WorldSize / (float)resolution;
        int count = resolution * resolution;

        var grid = new Grid
        {
            Resolution = resolution,
            Cell = cell,
            Height = new float[count],
            Filled = new float[count],
            Discharge = new float[count],
            Runoff = new float[count],
            Receiver = new int[count],
            Outlet = new bool[count],
            Dry = new bool[count],
            DryRise = Mathf.Max(0f, config.Water.ValleyMaxCut - 2f * DRY_FREEBOARD)
        };

        float sub = cell / SAMPLES;

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
            {
                float sum = 0f;

                for (int k = 0; k < SAMPLES * SAMPLES; k++)
                    sum += map.SampleWorldSmooth(column * cell + (k % SAMPLES + 0.5f) * sub, row * cell + (k / SAMPLES + 0.5f) * sub);

                int index = row * resolution + column;
                grid.Height[index] = sum / (SAMPLES * SAMPLES);

                Vector2 center = grid.Center(index);
                grid.Runoff[index] = cell * cell * (climate == null ? 1f : climate.Runoff(center.x, center.y, config.Water.DryBiomeRunoff));
                grid.Dry[index] = climate != null && climate.Dry(center.x, center.y);
            }
        });

        return grid;
    }

    private static void Outlets(Grid grid, WorldGenerationConfig config, DrainageValleyReport report)
    {
        int resolution = grid.Resolution;
        float sea = config.SeaLevel;
        var queue = new Queue<int>();

        if (CoastShaper.Active(config))
        {
            for (int i = 0; i < grid.Height.Length; i++)
            {
                if (!Border(i, resolution) || grid.Height[i] >= sea)
                    continue;

                grid.Outlet[i] = true;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int column = current % resolution, row = current / resolution;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;

                    if (grid.Outlet[next] || grid.Height[next] >= sea)
                        continue;

                    grid.Outlet[next] = true;
                    queue.Enqueue(next);
                }
            }
        }

        for (int i = 0; i < grid.Height.Length; i++)
        {
            if (Border(i, resolution) && !CoastShaper.Active(config))
                grid.Outlet[i] = true;

            if (grid.Outlet[i])
                report.Outlets++;
        }

        if (report.Outlets == 0)
        {
            for (int i = 0; i < grid.Height.Length; i++)
            {
                if (!Border(i, resolution))
                    continue;

                grid.Outlet[i] = true;
                report.Outlets++;
            }
        }

        for (int i = 0; i < grid.Height.Length; i++)
        {
            if (!grid.Dry[i] || grid.Outlet[i])
                continue;

            grid.Outlet[i] = true;
            report.DryCells++;
        }
    }

    private static bool Border(int cell, int resolution)
    {
        int column = cell % resolution, row = cell / resolution;

        return column == 0 || row == 0 || column == resolution - 1 || row == resolution - 1;
    }

    private static void Flood(Grid grid)
    {
        int resolution = grid.Resolution;
        var closed = new bool[grid.Height.Length];
        var heap = new MinHeap(resolution * 8);
        float epsilon = MIN_GRADE * grid.Cell;

        for (int i = 0; i < grid.Height.Length; i++)
        {
            if (!grid.Outlet[i])
                continue;

            closed[i] = true;
            grid.Filled[i] = Barrier(grid, i);
            heap.Push(i, grid.Filled[i]);
        }

        while (heap.TryPop(out int current))
        {
            int column = current % resolution, row = current / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;

                if (closed[next])
                    continue;

                closed[next] = true;
                grid.Filled[next] = Mathf.Max(Barrier(grid, next), grid.Filled[current] + epsilon);
                heap.Push(next, grid.Filled[next]);
            }
        }
    }

    private static float Barrier(Grid grid, int cell)
    {
        return grid.Height[cell] + (grid.Dry[cell] ? grid.DryRise : 0f);
    }

    private static void Route(Grid grid)
    {
        int resolution = grid.Resolution;
        float diagonal = Mathf.Sqrt(2f);

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;
                grid.Receiver[index] = -1;

                if (grid.Outlet[index])
                    continue;

                float steepest = 0f;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;
                    float drop = (grid.Filled[index] - grid.Filled[next]) / ((direction & 1) == 1 ? diagonal : 1f);

                    if (drop <= steepest)
                        continue;

                    steepest = drop;
                    grid.Receiver[index] = next;
                }
            }
        });
    }

    private static void Accumulate(Grid grid)
    {
        int count = grid.Receiver.Length;
        var pending = new int[count];
        var ready = new Queue<int>();

        for (int i = 0; i < count; i++)
        {
            grid.Discharge[i] = grid.Runoff[i];

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

            grid.Discharge[receiver] += grid.Discharge[cell];

            if (--pending[receiver] == 0)
                ready.Enqueue(receiver);
        }

        if (placed != count)
            throw new InvalidOperationException($"Drainage valleys left a cycle through {count - placed} cells");
    }

    private static void ChooseMain(WorldGenerationConfig config, WaterClimate climate, DrainageValleyReport report)
    {
        report.MainPlan = MainRiverPlanner.Choose(config, climate, report.Paths, config.Water.ValleyCellSize, out List<int> chain);

        if (!report.MainPlan.Success)
            return;

        foreach (int v in chain)
            report.Paths[v].Main = true;

        var main = new DrainageValley { Id = -1, Parent = -1, Mouth = true, Main = true };
        main.Points.AddRange(report.MainPlan.Points);
        report.Main = main;
    }
}

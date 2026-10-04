using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class Hydrology
{
    public HydrologyGrid Analyze()
    {
        int resolution = Mathf.Max(2, Mathf.CeilToInt(_config.WorldSize / _settings.CellSize));
        float cell = _config.WorldSize / (float)resolution;
        int count = resolution * resolution;

        var grid = new HydrologyGrid
        {
            Resolution = resolution,
            CellSize = cell,
            Height = new float[count],
            Filled = new float[count],
            Routing = new float[count],
            Receiver = new int[count],
            Accumulation = new float[count],
            Runoff = new float[count],
            Order = new int[count],
            Basin = new int[count],
            Outlet = new bool[count],
            Sink = new bool[count]
        };

        _grid = grid;

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
            {
                float x = (column + 0.5f) * cell, z = (row + 0.5f) * cell;
                grid.Height[row * resolution + column] = _map.SampleWorldSmooth(x, z);
                grid.Runoff[row * resolution + column] = _climate == null ? 1f : _climate.Runoff(x, z, _settings.DryBiomeRunoff);
                grid.Sink[row * resolution + column] = _climate != null && _climate.Dry(x, z);
            }
        });

        Flood(grid);
        Route(grid);
        Accumulate(grid);
        FindBasins(grid);

        return grid;
    }

    private void Flood(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        int count = resolution * resolution;
        bool[] ocean = Ocean(grid);
        grid.Ocean = ocean;

        for (int i = 0; i < count; i++)
        {
            int column = i % resolution;
            int row = i / resolution;

            bool border = column == 0 || row == 0 || column == resolution - 1 || row == resolution - 1;

            if (!border && !ocean[i] && !grid.Sink[i])
                continue;

            grid.Outlet[i] = true;
        }

        Fill(grid, grid.Filled, 0f, null);
        Fill(grid, grid.Routing, DRY_RISE, grid.Order);
    }

    private static void Fill(HydrologyGrid grid, float[] surface, float dryRise, int[] order)
    {
        int resolution = grid.Resolution;
        int count = resolution * resolution;
        var closed = new bool[count];
        var heap = new MinHeap(resolution * 8);

        for (int i = 0; i < count; i++)
        {
            if (!grid.Outlet[i])
                continue;

            surface[i] = grid.Height[i] + (grid.Sink[i] ? dryRise : 0f);
            closed[i] = true;
            heap.Push(i, surface[i]);
        }

        int processed = 0;

        while (heap.TryPop(out int current))
        {
            if (order != null)
                order[processed++] = current;

            int column = current % resolution;
            int row = current / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction];
                int r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;

                if (closed[next])
                    continue;

                closed[next] = true;
                surface[next] = Mathf.Max(grid.Height[next], surface[current] + FLOOD_EPSILON);
                heap.Push(next, surface[next]);
            }
        }
    }

    private bool[] Ocean(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        var ocean = new bool[grid.Height.Length];
        var reached = new bool[grid.Height.Length];
        float sea = _config.SeaLevel;

        if (sea <= 0f)
            return ocean;

        var queue = new Queue<int>();

        for (int i = 0; i < ocean.Length; i++)
        {
            int column = i % resolution, row = i / resolution;
            bool border = column == 0 || row == 0 || column == resolution - 1 || row == resolution - 1;

            if (!border || grid.Height[i] >= sea)
                continue;

            reached[i] = true;
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

                if (reached[next] || grid.Height[next] >= sea + WaterMap.LAGOON_BAR)
                    continue;

                reached[next] = true;
                queue.Enqueue(next);
            }
        }

        for (int i = 0; i < ocean.Length; i++)
            ocean[i] = reached[i] && grid.Height[i] < sea;

        return ocean;
    }

    private static void Route(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        float diagonal = Mathf.Sqrt(2f);

        Parallel.For(0, resolution, row =>
        {
            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;

                if (grid.Outlet[index])
                {
                    grid.Receiver[index] = -1;
                    continue;
                }

                int best = -1;
                float steepest = 0f;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction];
                    int r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;
                    float drop = (grid.Routing[index] - grid.Routing[next]) / ((direction & 1) == 1 ? diagonal : 1f);

                    if (drop <= steepest)
                        continue;

                    steepest = drop;
                    best = next;
                }

                grid.Receiver[index] = best;
            }
        });
    }

    private static void Accumulate(HydrologyGrid grid)
    {
        int count = grid.Accumulation.Length;

        for (int i = 0; i < count; i++)
            grid.Accumulation[i] = grid.Runoff == null ? 1f : grid.Runoff[i];

        for (int i = count - 1; i >= 0; i--)
        {
            int cell = grid.Order[i];
            int receiver = grid.Receiver[cell];

            if (receiver >= 0)
                grid.Accumulation[receiver] += grid.Accumulation[cell];
        }
    }

    private void FindBasins(HydrologyGrid grid)
    {
        int resolution = grid.Resolution;
        int count = resolution * resolution;
        float start = RiverStartCells(grid);
        var queue = new Queue<int>();

        for (int i = 0; i < count; i++)
            grid.Basin[i] = -1;

        for (int seed = 0; seed < count; seed++)
        {
            if (grid.Basin[seed] >= 0 || grid.Filled[seed] - grid.Height[seed] <= DEPRESSION)
                continue;

            var basin = new HydrologyBasin { Id = Basins.Count };
            Basins.Add(basin);

            grid.Basin[seed] = basin.Id;
            queue.Enqueue(seed);

            double sumX = 0, sumZ = 0;

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                basin.Cells.Add(cell);
                basin.Spill = Mathf.Min(basin.Spill, grid.Filled[cell]);
                basin.River |= grid.Accumulation[cell] >= start;

                int column = cell % resolution;
                int row = cell / resolution;

                sumX += column;
                sumZ += row;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction];
                    int r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;

                    if (grid.Basin[next] >= 0 || grid.Filled[next] - grid.Height[next] <= DEPRESSION)
                        continue;

                    grid.Basin[next] = basin.Id;
                    queue.Enqueue(next);
                }
            }

            foreach (int cell in basin.Cells)
                basin.Depth = Mathf.Max(basin.Depth, basin.Spill - grid.Height[cell]);

            basin.Center = new Vector2((float)(sumX / basin.Cells.Count + 0.5) * grid.CellSize, (float)(sumZ / basin.Cells.Count + 0.5) * grid.CellSize);
        }
    }

    private float RiverStartCells(HydrologyGrid grid)
    {
        return Mathf.Max(MIN_RIVER_CELLS, _settings.RiverStartArea * 1e6f / (grid.CellSize * grid.CellSize));
    }

    private bool SeaAt(Vector2 point)
    {
        return OceanAt(point);
    }

    private bool OceanAt(Vector2 point)
    {
        if (_grid?.Ocean == null)
            return false;

        int column = Mathf.Clamp((int)(point.x / _grid.CellSize), 0, _grid.Resolution - 1);
        int row = Mathf.Clamp((int)(point.y / _grid.CellSize), 0, _grid.Resolution - 1);

        return _grid.Ocean[row * _grid.Resolution + column];
    }

    private bool DryCell(HydrologyGrid grid, int cell)
    {
        if (_climate == null)
            return false;

        return _climate.Dry((cell % grid.Resolution + 0.5f) * grid.CellSize, (cell / grid.Resolution + 0.5f) * grid.CellSize);
    }

    private static float DryShore(HydrologyGrid grid, HydrologyBasin basin)
    {
        int resolution = grid.Resolution;
        float lowest = float.PositiveInfinity;

        foreach (int cell in basin.Cells)
        {
            int x = cell % resolution, z = cell / resolution;

            for (int dz = -BASIN_DRY_REACH; dz <= BASIN_DRY_REACH && grid.Height[cell] < lowest; dz++)
            {
                for (int dx = -BASIN_DRY_REACH; dx <= BASIN_DRY_REACH; dx++)
                {
                    int nx = x + dx, nz = z + dz;

                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution || !grid.Sink[nz * resolution + nx])
                        continue;

                    lowest = grid.Height[cell];
                    break;
                }
            }
        }

        return lowest;
    }

    private bool DryBasin(HydrologyGrid grid, HydrologyBasin basin)
    {
        if (_climate == null)
            return false;

        var seen = new HashSet<int>();
        int resolution = grid.Resolution;

        foreach (int cell in basin.Cells)
        {
            int x = cell % resolution, z = cell / resolution;

            for (int dz = -BASIN_DRY_REACH; dz <= BASIN_DRY_REACH; dz++)
            {
                for (int dx = -BASIN_DRY_REACH; dx <= BASIN_DRY_REACH; dx++)
                {
                    int nx = x + dx, nz = z + dz;

                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                        continue;

                    int near = nz * resolution + nx;

                    if (seen.Add(near) && DryCell(grid, near))
                        return true;
                }
            }
        }

        return false;
    }
}

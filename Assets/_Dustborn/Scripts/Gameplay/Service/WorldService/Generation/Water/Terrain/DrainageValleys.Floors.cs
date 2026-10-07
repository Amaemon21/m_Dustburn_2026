using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class DrainageValleys
{
    private static float[] Solve(Grid grid, WorldGenerationConfig config, DrainageValleyReport report)
    {
        int count = grid.Height.Length;
        float start = config.Water.RiverStartArea * 1e6f;
        var channel = new bool[count];
        var donors = new List<int>[count];

        for (int i = 0; i < count; i++)
        {
            channel[i] = !grid.Outlet[i] && grid.Discharge[i] >= start;

            if (channel[i])
                report.ChannelCells++;

            if (!grid.Outlet[i])
                report.LargestCatchment = Mathf.Max(report.LargestCatchment, grid.Discharge[i]);
        }

        for (int i = 0; i < count; i++)
        {
            int receiver = grid.Receiver[i];

            if (channel[i] && receiver >= 0 && channel[receiver])
                (donors[receiver] ??= new List<int>()).Add(i);
        }

        var levels = new float[count];
        Array.Fill(levels, float.NaN);
        var queue = new Queue<(int Start, int Pin, int Parent, bool Dry)>();

        for (int i = 0; i < count; i++)
        {
            int receiver = grid.Receiver[i];

            if (!channel[i] || receiver >= 0 && channel[receiver])
                continue;

            queue.Enqueue((i, receiver, -1, receiver >= 0 && grid.Dry[receiver]));
            report.Mouths++;
        }

        float[] toDry = DryDistance(grid);

        while (queue.Count > 0)
        {
            (int first, int pin, int parent, bool dry) = queue.Dequeue();
            var cells = new List<int> { first };

            for (int current = first; donors[current] != null;)
            {
                int best = donors[current][0];

                foreach (int donor in donors[current])
                {

                    if (grid.Discharge[donor] > grid.Discharge[best] || grid.Discharge[donor] == grid.Discharge[best] && donor < best)
                        best = donor;
                }

                cells.Add(best);
                current = best;
            }

            cells.Reverse();

            var valley = new DrainageValley { Id = report.Paths.Count, Parent = parent, Mouth = parent < 0 && !dry };
            float end = float.NaN;
            List<int> dropped = null;

            if (dry)
            {
                report.DryEnds++;
                end = Terminal(grid, config, cells, toDry, valley, out dropped);

                if (valley.Terminal)
                {
                    report.TerminalLakes++;
                    pin = -1;
                }
            }

            float pinLevel = pin < 0 ? float.NaN : float.IsNaN(levels[pin]) ? PinAtMouth(grid, config, pin) : levels[pin];
            Fit(grid, config, cells, pin, pinLevel, end, levels, valley, report);
            report.Paths.Add(valley);
            report.Valleys++;

            if (dropped != null)
            {
                foreach (int cell in dropped)
                    levels[cell] = grid.Height[cell];

                foreach (int cell in dropped)
                {
                    if (donors[cell] == null)
                        continue;

                    foreach (int donor in donors[cell])
                    {
                        if (float.IsNaN(levels[donor]))
                            queue.Enqueue((donor, cell, -1, true));
                    }
                }
            }

            foreach (int cell in cells)
            {
                if (donors[cell] == null)
                    continue;

                foreach (int donor in donors[cell])
                {
                    if (!float.IsNaN(levels[donor]) || cells.Contains(donor))
                        continue;

                    queue.Enqueue((donor, cell, valley.Id, false));
                    report.Junctions++;
                }
            }
        }

        return levels;
    }

    private static void Fit(Grid grid, WorldGenerationConfig config, List<int> cells, int pin, float pinLevel, float end, float[] levels, DrainageValley valley, DrainageValleyReport report)
    {
        bool pinned = pin >= 0;
        int count = cells.Count + (pinned ? 1 : 0);
        var positions = new Vector2[count];
        var ground = new float[count];
        var weights = new float[count];
        var remaining = new float[count];

        for (int i = 0; i < cells.Count; i++)
        {
            positions[i] = grid.Center(cells[i]);
            ground[i] = grid.Height[cells[i]];
            weights[i] = 1f;
        }

        if (pinned)
        {
            positions[count - 1] = grid.Center(pin);
            ground[count - 1] = pinLevel;
            weights[count - 1] = PIN_WEIGHT;
        }

        var ceiling = new float[count];

        for (int i = 0; i < count; i++)
            ceiling[i] = i < cells.Count ? Mathf.Min(ground[i], BesideDry(grid, cells[i])) : ground[i];

        bool terminal = !float.IsNaN(end);

        if (terminal)
        {
            ceiling[cells.Count - 1] = Mathf.Min(ceiling[cells.Count - 1], end);
            weights[cells.Count - 1] = PIN_WEIGHT;
        }

        for (int i = count - 2; i >= 0; i--)
            remaining[i] = remaining[i + 1] + Vector2.Distance(positions[i], positions[i + 1]);

        var target = new float[count];

        for (int i = 0; i < count; i++)
            target[i] = ceiling[i] - MIN_GRADE * remaining[i];

        float[] fitted = Isotonic(target, weights);
        float cut = config.Water.ValleyMaxCut;
        float dry = CoastShaper.Active(config) ? config.SeaLevel + SEA_CLEARANCE : float.NegativeInfinity;

        for (int i = 0; i < cells.Count; i++)
        {
            float floor = Mathf.Max(ground[i] - cut, Mathf.Min(ground[i], dry)) - MIN_GRADE * remaining[i];

            if (fitted[i] >= floor)
                continue;

            fitted[i] = floor;
            report.CutLimited++;
        }

        if (pinned || terminal)
            fitted[count - 1] = target[count - 1];

        for (int i = count - 2; i >= 0; i--)
            fitted[i] = Mathf.Max(fitted[i], fitted[i + 1]);

        for (int i = 0; i < count; i++)
        {
            float level = fitted[i] + MIN_GRADE * remaining[i];

            if (i < cells.Count)
            {
                levels[cells[i]] = level;
                report.DeepestCut = Mathf.Max(report.DeepestCut, ground[i] - level);
                report.HighestFill = Mathf.Max(report.HighestFill, level - ground[i]);

                if (level - ground[i] > config.Water.ValleyMaxFill)
                    report.LakePoints++;
            }

            valley.Points.Add(positions[i]);
            valley.Floor.Add(level);
            valley.Ground.Add(i < cells.Count ? ground[i] : level);
            valley.Discharge.Add(grid.Discharge[i < cells.Count ? cells[i] : cells[^1]]);

            if (i > 0)
                report.Length += Vector2.Distance(positions[i - 1], positions[i]);
        }
    }

    private static float Terminal(Grid grid, WorldGenerationConfig config, List<int> cells, float[] toDry, DrainageValley valley, out List<int> dropped)
    {
        dropped = null;
        float radius = TerminalRadius(grid.Discharge[cells[^1]], config.Water.RiverStartArea * 1e6f);
        int keep = cells.Count - 1;

        while (keep >= 0 && toDry[cells[keep]] < radius + TERMINAL_CLEARANCE)
            keep--;

        if (keep < TERMINAL_MIN_CELLS - 1)
            return float.NaN;

        int last = cells[keep];
        float level = Mathf.Max(LowestAround(grid, last, radius + TERMINAL_CLEARANCE + grid.Cell) - TERMINAL_DEPTH, grid.Height[last] - config.Water.ValleyMaxCut);

        if (CoastShaper.Active(config) && level < config.SeaLevel + TERMINAL_SEA_CLEARANCE)
            return float.NaN;

        dropped = cells.GetRange(keep + 1, cells.Count - keep - 1);
        cells.RemoveRange(keep + 1, cells.Count - keep - 1);
        valley.Terminal = true;
        valley.TerminalRadius = radius;

        return level;
    }

    private static float TerminalRadius(float discharge, float start)
    {
        return Mathf.Clamp(TERMINAL_MIN_RADIUS + TERMINAL_RADIUS_PER_DOUBLING * Mathf.Log(Mathf.Max(1f, discharge / start), 2f), TERMINAL_MIN_RADIUS, TERMINAL_MAX_RADIUS);
    }

    private static float LowestAround(Grid grid, int cell, float reach)
    {
        int resolution = grid.Resolution;
        int span = Mathf.CeilToInt(reach / grid.Cell);
        int column = cell % resolution, row = cell / resolution;
        float lowest = grid.Height[cell];

        for (int r = Mathf.Max(0, row - span); r <= Mathf.Min(resolution - 1, row + span); r++)
        {
            for (int c = Mathf.Max(0, column - span); c <= Mathf.Min(resolution - 1, column + span); c++)
            {
                float dx = (c - column) * grid.Cell, dz = (r - row) * grid.Cell;

                if (dx * dx + dz * dz <= reach * reach)
                    lowest = Mathf.Min(lowest, grid.Height[r * resolution + c]);
            }
        }

        return lowest;
    }

    private static float[] DryDistance(Grid grid)
    {
        int resolution = grid.Resolution;
        float straight = grid.Cell, diagonal = grid.Cell * Mathf.Sqrt(2f);
        var distance = new float[grid.Height.Length];

        for (int i = 0; i < distance.Length; i++)
            distance[i] = grid.Dry[i] ? 0f : float.PositiveInfinity;

        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int i = row * resolution + column;

                if (column > 0)
                    distance[i] = Mathf.Min(distance[i], distance[i - 1] + straight);

                if (row == 0)
                    continue;

                distance[i] = Mathf.Min(distance[i], distance[i - resolution] + straight);

                if (column > 0)
                    distance[i] = Mathf.Min(distance[i], distance[i - resolution - 1] + diagonal);

                if (column < resolution - 1)
                    distance[i] = Mathf.Min(distance[i], distance[i - resolution + 1] + diagonal);
            }
        }

        for (int row = resolution - 1; row >= 0; row--)
        {
            for (int column = resolution - 1; column >= 0; column--)
            {
                int i = row * resolution + column;

                if (column < resolution - 1)
                    distance[i] = Mathf.Min(distance[i], distance[i + 1] + straight);

                if (row == resolution - 1)
                    continue;

                distance[i] = Mathf.Min(distance[i], distance[i + resolution] + straight);

                if (column < resolution - 1)
                    distance[i] = Mathf.Min(distance[i], distance[i + resolution + 1] + diagonal);

                if (column > 0)
                    distance[i] = Mathf.Min(distance[i], distance[i + resolution - 1] + diagonal);
            }
        }

        return distance;
    }

    private static float BesideDry(Grid grid, int cell)
    {
        int receiver = grid.Receiver[cell];

        if (grid.Dry[cell] || receiver < 0 || grid.Dry[receiver])
            return float.PositiveInfinity;

        int resolution = grid.Resolution;
        int column = cell % resolution, row = cell / resolution;
        float lowest = float.PositiveInfinity;

        for (int direction = 0; direction < 8; direction++)
        {
            int c = column + OffsetX[direction], r = row + OffsetZ[direction];

            if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                continue;

            int next = r * resolution + c;

            if (grid.Dry[next])
                lowest = Mathf.Min(lowest, grid.Height[next] - DRY_FREEBOARD);
        }

        return lowest;
    }

    private static float PinAtMouth(Grid grid, WorldGenerationConfig config, int outlet)
    {
        if (!CoastShaper.Active(config) || grid.Dry[outlet])
            return grid.Height[outlet];

        return Mathf.Min(grid.Height[outlet], config.SeaLevel - MOUTH_DEPTH);
    }

    private static float[] Isotonic(float[] values, float[] weights)
    {
        int count = values.Length;
        var level = new float[count];
        var weight = new float[count];
        var size = new int[count];
        int blocks = 0;

        for (int i = 0; i < count; i++)
        {
            level[blocks] = values[i];
            weight[blocks] = weights[i];
            size[blocks] = 1;
            blocks++;

            while (blocks > 1 && level[blocks - 2] < level[blocks - 1])
            {
                float total = weight[blocks - 2] + weight[blocks - 1];
                level[blocks - 2] = (level[blocks - 2] * weight[blocks - 2] + level[blocks - 1] * weight[blocks - 1]) / total;
                weight[blocks - 2] = total;
                size[blocks - 2] += size[blocks - 1];
                blocks--;
            }
        }

        var result = new float[count];
        int at = 0;

        for (int b = 0; b < blocks; b++)
        {
            for (int k = 0; k < size[b]; k++)
                result[at++] = level[b];
        }

        return result;
    }

    private static bool[] OpenRuns(DrainageValley valley, float fill)
    {
        int count = valley.Points.Count;
        var open = new bool[count];
        int first = 0;

        while (first < count)
        {
            if (valley.Floor[first] <= valley.Ground[first])
            {
                first++;
                continue;
            }

            int last = first;
            float deepest = 0f;

            while (last < count && valley.Floor[last] > valley.Ground[last])
            {
                deepest = Mathf.Max(deepest, valley.Floor[last] - valley.Ground[last]);
                last++;
            }

            for (int i = Mathf.Max(0, first - 1); i < Mathf.Min(count, last + 1); i++)
                open[i] |= deepest > fill;

            first = last;
        }

        return open;
    }
}

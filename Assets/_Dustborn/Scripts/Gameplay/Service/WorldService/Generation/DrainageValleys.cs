using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DrainageValleyReport
{
    public int Outlets;
    public int Mouths;
    public int DryEnds;
    public int DryCells;
    public int TerminalLakes;
    public int Valleys;
    public int Junctions;
    public int LakePoints;
    public int CutLimited;
    public int ChannelCells;
    public int LiftedCells;
    public int Inlets;
    public float LargestCatchment;
    public float Length;
    public float DeepestCut;
    public float HighestFill;
    public readonly List<DrainageValley> Paths = new();
    public DrainageValley Main;
    public MainRiverPlan MainPlan;

    public string Describe()
    {
        return $"Drainage valleys: {Valleys} valleys over {Length / 1000f:0.0} km from {Mouths} mouths into {Outlets} outlet cells ({DryEnds} valleys run into the {DryCells} dry cells, {TerminalLakes} of them end in a terminal basin short of it), {Junctions} junctions, {ChannelCells} channel cells, largest catchment {LargestCatchment / 1e6f:0.0} km2, "
            + $"deepest cut {DeepestCut:0.0} m ({CutLimited} floor points held up by the cut limit), highest fill {HighestFill:0.0} m, {LakePoints} floor points left open for lakes, {Inlets} pockets under the sea opened and {LiftedCells} height cells lifted after the cut; {(MainPlan == null ? "no main river asked for" : MainPlan.Describe())}";
    }
}

public sealed class DrainageValley
{
    public int Id;
    public int Parent;
    public bool Mouth;
    public bool Main;
    public bool Terminal;
    public float TerminalRadius;
    public readonly List<Vector2> Points = new();
    public readonly List<float> Floor = new();
    public readonly List<float> Ground = new();
    public readonly List<float> Discharge = new();
}

public static class DrainageValleys
{
    private const float MIN_GRADE = 0.001f;
    private const float PIN_WEIGHT = 1000f;
    private const float WALL_CURVE = 0.002f;
    private const float MIN_FLOOR = 10f;
    private const float MAX_FLOOR = 60f;
    private const float FLOOR_PER_DOUBLING = 8f;
    private const float BLEND = 3f;
    private const float MOUTH_DEPTH = 1f;
    private const float SEA_CLEARANCE = 0.5f;
    private const float FILL_EDGE = 0.5f;
    private const float FILL_CLEARANCE = 0.05f;
    private const float SHAPE_STEP = 4f;
    private const int TILE_NODES = 16;
    private const int SAMPLES = 4;
    private const int CHAIKIN_PASSES = 2;
    private const float RESAMPLE = 16f;
    private const float DRY_FREEBOARD = 1.5f;
    private const float TERMINAL_MIN_RADIUS = 28f;
    private const float TERMINAL_MAX_RADIUS = 80f;
    private const float TERMINAL_RADIUS_PER_DOUBLING = 10f;
    private const float TERMINAL_CLEARANCE = 48f;
    private const float TERMINAL_DEPTH = 4f;
    private const float TERMINAL_SEA_CLEARANCE = 1f;
    private const int TERMINAL_MIN_CELLS = 3;

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

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

    private readonly struct Station
    {
        public readonly Vector2 Position;
        public readonly float Level;
        public readonly float Half;
        public readonly bool Fill;

        public Station(Vector2 position, float level, float half, bool fill)
        {
            Position = position;
            Level = level;
            Half = half;
            Fill = fill;
        }
    }

    private readonly struct Segment
    {
        public readonly Station A;
        public readonly Station B;
        public readonly float Reach;

        public Segment(Station a, Station b, float reach)
        {
            A = a;
            B = b;
            Reach = reach;
        }
    }

    public static bool Active(WorldGenerationConfig config)
    {
        WaterGenerationSettings water = config.Water;

        return water != null && water.Enabled && water.DrainageValleys;
    }

    public static DrainageValleyReport Shape(HeightMap map, WorldGenerationConfig config, WaterClimate climate)
    {
        var report = new DrainageValleyReport();

        if (!Active(config))
            return report;

        Grid grid = Sample(map, config, climate);
        Outlets(grid, config, report);
        Flood(grid);
        Route(grid);
        Accumulate(grid);

        float[] levels = Solve(grid, config, report);

        if (config.Water.ThroughRiver)
            ChooseMain(config, climate, report);
        List<Segment> segments = Segments(grid, config, report, levels);
        Carve(map, segments, config);

        if (CoastShaper.Active(config))
            report.LiftedCells = CoastShaper.LiftInland(map, config.SeaLevel, CoastShaper.INLAND_MARGIN, out report.Inlets);

        return report;
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

    private static List<Segment> Segments(Grid grid, WorldGenerationConfig config, DrainageValleyReport report, float[] levels)
    {
        var segments = new List<Segment>();
        float start = config.Water.RiverStartArea * 1e6f;
        float fill = config.Water.ValleyMaxFill;
        float highest = 0f;

        foreach (float height in grid.Height)
            highest = Mathf.Max(highest, height);

        foreach (DrainageValley valley in report.Paths)
        {
            var stations = new List<Station>(valley.Points.Count);
            bool[] open = OpenRuns(valley, fill);

            for (int i = 0; i < valley.Points.Count; i++)
            {
                float half = Mathf.Clamp(MIN_FLOOR + FLOOR_PER_DOUBLING * Mathf.Log(Mathf.Max(1f, valley.Discharge[i] / start), 2f), MIN_FLOOR, MAX_FLOOR);

                if (valley.Terminal && i == valley.Points.Count - 1)
                    half = Mathf.Max(half, valley.TerminalRadius);

                if (valley.Main)
                    half = Mathf.Max(half, MIN_FLOOR + 0.5f * config.Water.MainRiverWidth * config.Water.RiverWidthScale);

                stations.Add(new Station(valley.Points[i], valley.Floor[i], half, !open[i]));
            }

            for (int pass = 0; pass < CHAIKIN_PASSES; pass++)
                stations = Chaikin(stations);

            stations = Resample(stations, RESAMPLE);

            for (int i = 0; i + 1 < stations.Count; i++)
            {
                float lowest = Mathf.Min(stations[i].Level, stations[i + 1].Level);
                float half = Mathf.Max(stations[i].Half, stations[i + 1].Half);
                segments.Add(new Segment(stations[i], stations[i + 1], half + WallReach(highest - lowest, config.Water.ValleyWallGrade)));
            }
        }

        return segments;
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

    private static float WallReach(float rise, float grade)
    {
        if (rise <= 0f)
            return 0f;

        return (-grade + Mathf.Sqrt(grade * grade + 4f * WALL_CURVE * rise)) / (2f * WALL_CURVE);
    }

    private static List<Station> Chaikin(List<Station> stations)
    {
        if (stations.Count < 3)
            return stations;

        var result = new List<Station>(stations.Count * 2) { stations[0] };

        for (int i = 0; i + 1 < stations.Count; i++)
        {
            if (i > 0)
                result.Add(Lerp(stations[i], stations[i + 1], 0.25f));

            if (i + 2 < stations.Count)
                result.Add(Lerp(stations[i], stations[i + 1], 0.75f));
        }

        result.Add(stations[^1]);
        return result;
    }

    private static List<Station> Resample(List<Station> stations, float step)
    {
        var result = new List<Station> { stations[0] };

        for (int i = 0; i + 1 < stations.Count; i++)
        {
            float length = Vector2.Distance(stations[i].Position, stations[i + 1].Position);
            int pieces = Mathf.Max(1, Mathf.CeilToInt(length / step));

            for (int k = 1; k <= pieces; k++)
                result.Add(Lerp(stations[i], stations[i + 1], k / (float)pieces));
        }

        return result;
    }

    private static Station Lerp(Station a, Station b, float t)
    {
        return new Station(Vector2.Lerp(a.Position, b.Position, t), Mathf.Lerp(a.Level, b.Level, t), Mathf.Lerp(a.Half, b.Half, t), t < 0.5f ? a.Fill && (b.Fill || t < 0.25f) : b.Fill && (a.Fill || t > 0.75f));
    }

    private static void Carve(HeightMap map, List<Segment> segments, WorldGenerationConfig config)
    {
        if (segments.Count == 0)
            return;

        int nodes = Mathf.CeilToInt(map.WorldSize / SHAPE_STEP) + 1;
        float step = map.WorldSize / (float)(nodes - 1);
        var target = new float[nodes * nodes];
        var floorLevel = new float[nodes * nodes];
        var floorWeight = new float[nodes * nodes];
        Array.Fill(target, float.PositiveInfinity);

        int tiles = Mathf.CeilToInt(nodes / (float)TILE_NODES);
        float tileSize = TILE_NODES * step;
        var buckets = new List<int>[tiles * tiles];

        for (int s = 0; s < segments.Count; s++)
        {
            Segment segment = segments[s];
            Vector2 a = segment.A.Position, b = segment.B.Position;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - segment.Reach) / tileSize), 0, tiles - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, b.x) + segment.Reach) / tileSize), 0, tiles - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - segment.Reach) / tileSize), 0, tiles - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.y, b.y) + segment.Reach) / tileSize), 0, tiles - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                    (buckets[z * tiles + x] ??= new List<int>()).Add(s);
            }
        }

        float grade = config.Water.ValleyWallGrade;

        Parallel.For(0, buckets.Length, tile =>
        {
            List<int> bucket = buckets[tile];

            if (bucket == null)
                return;

            int i0 = tile % tiles * TILE_NODES, j0 = tile / tiles * TILE_NODES;
            int i1 = Mathf.Min(nodes, i0 + TILE_NODES), j1 = Mathf.Min(nodes, j0 + TILE_NODES);

            for (int j = j0; j < j1; j++)
            {
                for (int i = i0; i < i1; i++)
                {
                    var point = new Vector2(i * step, j * step);
                    float best = float.PositiveInfinity;
                    float level = 0f, weight = 0f;

                    foreach (int s in bucket)
                    {
                        Segment segment = segments[s];
                        Vector2 axis = segment.B.Position - segment.A.Position;
                        float length = axis.sqrMagnitude;
                        float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - segment.A.Position, axis) / length);
                        float distance = Vector2.Distance(point, segment.A.Position + axis * t);

                        if (distance > segment.Reach)
                            continue;

                        float floor = Mathf.Lerp(segment.A.Level, segment.B.Level, t);
                        float half = Mathf.Lerp(segment.A.Half, segment.B.Half, t);
                        float wall = Mathf.Max(0f, distance - half);
                        float value = floor + grade * wall + WALL_CURVE * wall * wall;

                        if (value >= best)
                            continue;

                        best = value;
                        bool fill = t < 0.5f ? segment.A.Fill : segment.B.Fill;
                        level = floor;
                        weight = fill ? Mathf.Clamp01((half - distance) / (half * FILL_EDGE)) : 0f;
                    }

                    int node = j * nodes + i;
                    target[node] = best;
                    floorLevel[node] = level;
                    floorWeight[node] = weight * weight * (3f - 2f * weight);
                }
            }
        });

        int resolution = map.Resolution;
        float cell = map.WorldSize / (float)(resolution - 1);
        float[] heights = map.Heights;
        float scale = 1f / map.MaxHeight;

        Parallel.For(0, resolution, row =>
        {
            float v = row * cell / step;
            int n0 = Mathf.Min((int)v, nodes - 2);
            float fz = v - n0;

            for (int column = 0; column < resolution; column++)
            {
                float u = column * cell / step;
                int m0 = Mathf.Min((int)u, nodes - 2);
                float fx = u - m0;

                int a = n0 * nodes + m0, b = a + 1, c = a + nodes, d = c + 1;

                if (float.IsPositiveInfinity(target[a]) || float.IsPositiveInfinity(target[b]) || float.IsPositiveInfinity(target[c]) || float.IsPositiveInfinity(target[d]))
                    continue;

                float shaped = Bilinear(target[a], target[b], target[c], target[d], fx, fz);
                float weight = Bilinear(floorWeight[a], floorWeight[b], floorWeight[c], floorWeight[d], fx, fz);
                float floor = Bilinear(floorLevel[a], floorLevel[b], floorLevel[c], floorLevel[d], fx, fz);

                int index = row * resolution + column;
                float height = heights[index] * map.MaxHeight;
                float result = Mathf.Max(SoftMin(height, shaped, BLEND), Mathf.Min(height, floor));

                if (weight > 0f && result < floor - FILL_CLEARANCE)
                    result = Mathf.Lerp(result, floor - FILL_CLEARANCE, weight);

                heights[index] = result * scale;
            }
        });
    }

    private static float Bilinear(float a, float b, float c, float d, float fx, float fz)
    {
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    private static float SoftMin(float a, float b, float k)
    {
        float gap = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;

        return Mathf.Min(a, b) - gap * gap * k * 0.25f;
    }
}

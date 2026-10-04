using System.Collections.Generic;
using UnityEngine;

public enum MainRiverOutlet
{
    None,
    Sea,
    Lake,
    Border
}

public sealed class MainRiverPlan
{
    public bool Success;
    public MainRiverOutlet Outlet;
    public readonly List<Vector2> Points = new();
    public int RegionCells;
    public float Length;
    public float InRegion;
    public float Span;
    public float Coverage;
    public int Attempts;
    public readonly List<string> Rejected = new();

    public string Describe()
    {
        string tried = Rejected.Count == 0 ? "" : $"; rejected: {string.Join("; ", Rejected)}";

        return Success
            ? $"Main river: {Length / 1000f:0.0} km natural drainage stem, {InRegion / 1000f:0.0} km of it in MainRiver biomes, {Span / 1000f:0.0} km source to mouth, {Coverage:P0} of its region's extent, region {RegionCells} cells, mouth in {Outlet}, {Attempts} stems compared{tried}"
            : $"Main river: no drainage stem qualifies after comparing {Attempts}{tried}";
    }
}

public static class MainRiverPlanner
{
    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public static MainRiverPlan Choose(WorldGenerationConfig config, WaterClimate climate, IReadOnlyList<DrainageValley> valleys, float cellSize, out List<int> chain)
    {
        var plan = new MainRiverPlan();
        chain = new List<int>();

        if (valleys.Count == 0)
        {
            plan.Rejected.Add("no drainage valleys");
            return plan;
        }

        int resolution = Mathf.Max(2, Mathf.CeilToInt(config.WorldSize / cellSize));
        float cell = config.WorldSize / (float)resolution;
        int[] region = Regions(climate, resolution, cell, out List<int> sizes);

        float best = 0f;
        List<Vector2> bestPoints = null;
        List<int> bestChain = null;

        for (int v = 0; v < valleys.Count; v++)
        {
            List<int> links = Chain(valleys, v);
            List<Vector2> points = Points(valleys, links);
            float inside = Inside(climate, points);
            plan.Attempts++;

            if (inside <= best)
                continue;

            best = inside;
            bestPoints = points;
            bestChain = links;
        }

        if (bestPoints == null)
        {
            plan.Rejected.Add("no drainage stem enters a MainRiver biome");
            return plan;
        }

        int home = Home(region, resolution, cell, bestPoints);
        float minimum = config.Water.MainRiverMinLength * config.WorldSize;
        Vector2 start = bestPoints[0], end = bestPoints[^1];
        float coverage = home < 0 ? 0f : Coverage(region, resolution, cell, home, start, end);

        plan.Points.AddRange(bestPoints);
        plan.Length = Length(bestPoints);
        plan.InRegion = best;
        plan.Span = Vector2.Distance(start, end);
        plan.Coverage = coverage;
        plan.RegionCells = home < 0 ? 0 : sizes[home];
        plan.Outlet = Outlet(config, valleys[bestChain[^1]]);

        if (best < minimum)
            plan.Rejected.Add($"the longest stem runs {best / 1000f:0.0} km through MainRiver biomes, under {minimum / 1000f:0.0} km");

        if (coverage < config.Water.MainRiverMinCoverage)
            plan.Rejected.Add($"it crosses {coverage:P0} of its region, under {config.Water.MainRiverMinCoverage:P0}");

        plan.Success = plan.Rejected.Count == 0;

        if (plan.Success)
            chain = bestChain;

        return plan;
    }

    private static List<int> Chain(IReadOnlyList<DrainageValley> valleys, int first)
    {
        var chain = new List<int>();

        for (int v = first; v >= 0 && chain.Count <= valleys.Count; v = valleys[v].Parent)
            chain.Add(v);

        return chain;
    }

    private static List<Vector2> Points(IReadOnlyList<DrainageValley> valleys, List<int> chain)
    {
        var points = new List<Vector2>();

        for (int k = 0; k < chain.Count; k++)
        {
            DrainageValley valley = valleys[chain[k]];
            int from = points.Count > 0 ? Nearest(valley.Points, points[^1]) : 0;
            int to = k + 1 < chain.Count ? valley.Points.Count - 1 : valley.Points.Count;

            for (int i = from; i < to; i++)
                points.Add(valley.Points[i]);
        }

        return points;
    }

    private static int Nearest(List<Vector2> line, Vector2 point)
    {
        int best = 0;

        for (int i = 1; i < line.Count; i++)
        {
            if ((line[i] - point).sqrMagnitude < (line[best] - point).sqrMagnitude)
                best = i;
        }

        return best;
    }

    private static float Inside(WaterClimate climate, List<Vector2> points)
    {
        float inside = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            if (Main(climate, points[i - 1]) && Main(climate, points[i]))
                inside += Vector2.Distance(points[i - 1], points[i]);
        }

        return inside;
    }

    private static float Length(List<Vector2> points)
    {
        float length = 0f;

        for (int i = 1; i < points.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);

        return length;
    }

    private static MainRiverOutlet Outlet(WorldGenerationConfig config, DrainageValley mouth)
    {
        if (!mouth.Mouth)
            return MainRiverOutlet.None;

        return CoastShaper.Active(config) || mouth.Floor[^1] < config.SeaLevel ? MainRiverOutlet.Sea : MainRiverOutlet.Border;
    }

    private static int[] Regions(WaterClimate climate, int resolution, float cell, out List<int> sizes)
    {
        var region = new int[resolution * resolution];
        System.Array.Fill(region, -1);
        sizes = new List<int>();
        var queue = new Queue<int>();

        for (int seed = 0; seed < region.Length; seed++)
        {
            if (region[seed] >= 0 || !Main(climate, seed, resolution, cell))
                continue;

            int id = sizes.Count;
            int size = 0;
            region[seed] = id;
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                size++;
                int column = current % resolution, row = current / resolution;

                for (int direction = 0; direction < 8; direction++)
                {
                    int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;

                    if (region[next] >= 0 || !Main(climate, next, resolution, cell))
                        continue;

                    region[next] = id;
                    queue.Enqueue(next);
                }
            }

            sizes.Add(size);
        }

        return region;
    }

    private static bool Main(WaterClimate climate, int index, int resolution, float cell)
    {
        return Main(climate, new Vector2((index % resolution + 0.5f) * cell, (index / resolution + 0.5f) * cell));
    }

    private static bool Main(WaterClimate climate, Vector2 point)
    {
        return climate == null || climate.Main(point.x, point.y);
    }

    private static int Home(int[] region, int resolution, float cell, List<Vector2> points)
    {
        var votes = new Dictionary<int, int>();
        int home = -1;

        foreach (Vector2 point in points)
        {
            int column = Mathf.Clamp((int)(point.x / cell), 0, resolution - 1), row = Mathf.Clamp((int)(point.y / cell), 0, resolution - 1);
            int id = region[row * resolution + column];

            if (id < 0)
                continue;

            votes[id] = votes.TryGetValue(id, out int count) ? count + 1 : 1;

            if (home < 0 || votes[id] > votes[home])
                home = id;
        }

        return home;
    }

    private static float Coverage(int[] region, int resolution, float cell, int home, Vector2 start, Vector2 end)
    {
        Vector2 axis = end - start;

        if (axis.sqrMagnitude < 1e-6f)
            return 0f;

        axis = axis.normalized;
        float low = float.MaxValue, high = float.MinValue;

        for (int index = 0; index < region.Length; index++)
        {
            if (region[index] != home)
                continue;

            float along = Vector2.Dot(new Vector2((index % resolution + 0.5f) * cell, (index / resolution + 0.5f) * cell), axis);
            low = Mathf.Min(low, along);
            high = Mathf.Max(high, along);
        }

        return high - low < 1e-3f ? 0f : Mathf.Clamp01((Vector2.Dot(end, axis) - Vector2.Dot(start, axis)) / (high - low));
    }
}

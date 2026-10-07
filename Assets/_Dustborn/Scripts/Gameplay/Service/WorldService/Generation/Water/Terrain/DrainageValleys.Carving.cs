using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static partial class DrainageValleys
{
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

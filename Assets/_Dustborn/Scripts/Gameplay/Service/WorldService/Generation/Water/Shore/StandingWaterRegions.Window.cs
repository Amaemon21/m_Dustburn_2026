using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class StandingWaterRegions
{
    private sealed class Window
    {
        public int OriginI, OriginJ, Width, Height;
        public float[] Ground;
        public bool[] Allowed;
        public bool[] Home;
        public bool[] Drain;
        public float[] DrainLevel;
        public bool[] Inflow;

        public int Count => Width * Height;

        public bool Contains(int i, int j)
        {
            return i >= OriginI && j >= OriginJ && i < OriginI + Width && j < OriginJ + Height;
        }

        public int Local(int i, int j)
        {
            return (j - OriginJ) * Width + i - OriginI;
        }
    }

    private Window Open(List<int> cells, float surface)
    {
        int resolution = _water.Resolution;
        int minC = int.MaxValue, minR = int.MaxValue, maxC = int.MinValue, maxR = int.MinValue;
        List<int> channels = LevelChannels(cells, surface);

        foreach (int cell in cells)
        {
            minC = Mathf.Min(minC, cell % resolution);
            maxC = Mathf.Max(maxC, cell % resolution);
            minR = Mathf.Min(minR, cell / resolution);
            maxR = Mathf.Max(maxR, cell / resolution);
        }

        foreach (int cell in channels)
        {
            minC = Mathf.Min(minC, cell % resolution);
            maxC = Mathf.Max(maxC, cell % resolution);
            minR = Mathf.Min(minR, cell / resolution);
            maxR = Mathf.Max(maxR, cell / resolution);
        }

        int margin = OWNERSHIP_REACH + 1;
        int c0 = Mathf.Max(0, minC - margin), c1 = Mathf.Min(resolution - 1, maxC + margin);
        int r0 = Mathf.Max(0, minR - margin), r1 = Mathf.Min(resolution - 1, maxR + margin);

        var window = new Window
        {
            OriginI = c0 * _sub,
            OriginJ = r0 * _sub,
            Width = (c1 - c0 + 1) * _sub + 1,
            Height = (r1 - r0 + 1) * _sub + 1
        };

        int cellsWide = c1 - c0 + 1;
        int cellsHigh = r1 - r0 + 1;
        var allowedCells = new bool[cellsWide * cellsHigh];
        var homeCells = new bool[cellsWide * cellsHigh];

        foreach (int cell in cells)
        {
            int c = cell % resolution - c0;
            int r = cell / resolution - r0;

            homeCells[r * cellsWide + c] = true;

            for (int dr = -OWNERSHIP_REACH; dr <= OWNERSHIP_REACH; dr++)
            {
                for (int dc = -OWNERSHIP_REACH; dc <= OWNERSHIP_REACH; dc++)
                {
                    int cc = c + dc, rr = r + dr;

                    if (cc < 0 || rr < 0 || cc >= cellsWide || rr >= cellsHigh)
                        continue;

                    if (_water.Filled != null && !Border(cc + c0, rr + r0) && _water.Filled[(rr + r0) * resolution + cc + c0] < surface - FILL_TOLERANCE)
                        continue;

                    allowedCells[rr * cellsWide + cc] = true;
                }
            }
        }

        foreach (int cell in channels)
            allowedCells[(cell / resolution - r0) * cellsWide + cell % resolution - c0] = true;

        window.Ground = new float[window.Count];
        window.Allowed = new bool[window.Count];
        window.Home = new bool[window.Count];

        Parallel.For(0, window.Height, row =>
        {
            int j = window.OriginJ + row;

            for (int column = 0; column < window.Width; column++)
            {
                int i = window.OriginI + column;
                int n = row * window.Width + column;

                window.Ground[n] = _map.SampleWorldSmooth(i * _step, j * _step);
                window.Allowed[n] = AnyCell(allowedCells, cellsWide, cellsHigh, column, row);
                window.Home[n] = AnyCell(homeCells, cellsWide, cellsHigh, column, row);
            }
        });

        return window;
    }

    private List<int> LevelChannels(List<int> cells, float surface)
    {
        int resolution = _water.Resolution;
        var near = new HashSet<int>();
        var channels = new HashSet<int>();

        foreach (int cell in cells)
        {
            int c = cell % resolution, r = cell / resolution;

            for (int dr = -OWNERSHIP_REACH; dr <= OWNERSHIP_REACH; dr++)
            {
                for (int dc = -OWNERSHIP_REACH; dc <= OWNERSHIP_REACH; dc++)
                {
                    if (c + dc >= 0 && r + dr >= 0 && c + dc < resolution && r + dr < resolution)
                        near.Add((r + dr) * resolution + c + dc);
                }
            }
        }

        foreach (RiverPath river in _water.Rivers)
        {
            List<RiverPoint> points = river.Points;
            int segments = points.Count - 1;

            if (segments <= 0)
                continue;

            var marked = new bool[segments];
            var footprints = new List<int>[segments];

            for (int i = 0; i < segments; i++)
            {
                if (!AtLevel(points, i, surface))
                    continue;

                footprints[i] = ChannelFootprint(points[i], points[i + 1]);

                foreach (int cell in footprints[i])
                {
                    if (!near.Contains(cell))
                        continue;

                    marked[i] = true;
                    break;
                }
            }

            for (int i = 1; i < segments; i++)
                marked[i] |= marked[i - 1] && footprints[i] != null;

            for (int i = segments - 2; i >= 0; i--)
                marked[i] |= marked[i + 1] && footprints[i] != null;

            for (int i = 0; i < segments; i++)
            {
                if (marked[i])
                    channels.UnionWith(footprints[i]);
            }
        }

        return new List<int>(channels);
    }

    private static bool AtLevel(List<RiverPoint> points, int segment, float surface)
    {
        RiverPoint a = points[segment], b = points[segment + 1];

        return (a.Submerged || b.Submerged) && Mathf.Abs(a.Surface - surface) <= WaterMeshes.MOUTH_BLEND && Mathf.Abs(b.Surface - surface) <= WaterMeshes.MOUTH_BLEND;
    }

    private List<int> ChannelFootprint(RiverPoint a, RiverPoint b)
    {
        int resolution = _water.Resolution;
        float cell = _water.CellSize;
        float radius = 0.5f * Mathf.Max(a.Width, b.Width) + WaterMeshes.RIBBON_PAD + cell;
        var footprint = new List<int>();

        int minC = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - radius) / cell));
        int maxC = Mathf.Min(resolution - 1, Mathf.FloorToInt((Mathf.Max(a.Position.x, b.Position.x) + radius) / cell));
        int minR = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - radius) / cell));
        int maxR = Mathf.Min(resolution - 1, Mathf.FloorToInt((Mathf.Max(a.Position.y, b.Position.y) + radius) / cell));

        Vector2 axis = b.Position - a.Position;
        float length = axis.sqrMagnitude;

        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                var center = new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
                float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(center - a.Position, axis) / length);

                if ((center - (a.Position + axis * t)).sqrMagnitude <= radius * radius)
                    footprint.Add(r * resolution + c);
            }
        }

        return footprint;
    }

    private bool Border(int column, int row)
    {
        int last = _water.Resolution - 1 - OWNERSHIP_REACH;

        return column < OWNERSHIP_REACH || row < OWNERSHIP_REACH || column > last || row > last;
    }

    private bool AnyCell(bool[] cells, int wide, int high, int column, int row)
    {
        int c1 = Mathf.Min(wide - 1, column / _sub);
        int r1 = Mathf.Min(high - 1, row / _sub);
        int c0 = column % _sub == 0 ? Mathf.Max(0, c1 - 1) : c1;
        int r0 = row % _sub == 0 ? Mathf.Max(0, r1 - 1) : r1;

        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (cells[r * wide + c])
                    return true;
            }
        }

        return false;
    }

    private void MarkRivers(Window window, float surface)
    {
        window.Drain = new bool[window.Count];
        window.DrainLevel = new float[window.Count];
        Array.Fill(window.DrainLevel, float.NegativeInfinity);
        window.Inflow = new bool[window.Count];

        float x0 = window.OriginI * _step, z0 = window.OriginJ * _step;
        float x1 = (window.OriginI + window.Width - 1) * _step, z1 = (window.OriginJ + window.Height - 1) * _step;

        for (int river = 0; river < _water.Rivers.Count; river++)
        {
            List<RiverPoint> points = _water.Rivers[river].Points;
            float[] reaches = _water.CarveReach != null && river < _water.CarveReach.Count ? _water.CarveReach[river] : null;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i], b = points[i + 1];
                bool below = Below(points, i, surface);

                if (!below && !Above(points, i, surface))
                    continue;

                float bar = 0.5f * Mathf.Max(a.Width, b.Width) + BAR_PAD;
                float radius = below && reaches != null && i < reaches.Length ? Mathf.Max(bar, reaches[i]) : bar;

                if (Mathf.Max(a.Position.x, b.Position.x) + radius < x0 || Mathf.Min(a.Position.x, b.Position.x) - radius > x1)
                    continue;

                if (Mathf.Max(a.Position.y, b.Position.y) + radius < z0 || Mathf.Min(a.Position.y, b.Position.y) - radius > z1)
                    continue;

                if (below)
                    Capsule(window, window.Drain, a.Position, b.Position, radius, Below(points, i - 1, surface), true, true, window.DrainLevel, Mathf.Max(a.Surface, b.Surface));
                else
                    Capsule(window, window.Inflow, a.Position, b.Position, radius, Above(points, i - 1, surface), Above(points, i + 1, surface), false);
            }
        }
    }

    private void ClearBowl(Window window, WaterBody data)
    {
        if (data.SeedRadius <= 0f)
            return;

        float limit = data.SeedRadius * data.SeedRadius;

        for (int n = 0; n < window.Count; n++)
        {
            var point = new Vector2((window.OriginI + n % window.Width) * _step, (window.OriginJ + n / window.Width) * _step);

            if ((point - data.Center).sqrMagnitude > limit)
                continue;

            window.Drain[n] = false;
            window.Inflow[n] = false;
        }
    }

    private bool Uncut(Vector2 point, float ground)
    {
        return _water.Uncarved != null && ground >= _water.Uncarved.SampleWorldSmooth(point.x, point.y) - CARVE_EPSILON;
    }

    private void Capsule(Window window, bool[] mask, Vector2 a, Vector2 b, float radius, bool roundStart, bool roundEnd, bool carvedOnly, float[] levels = null, float level = 0f)
    {
        int minI = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius) / _step) - window.OriginI);
        int maxI = Mathf.Min(window.Width - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius) / _step) - window.OriginI);
        int minJ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - radius) / _step) - window.OriginJ);
        int maxJ = Mathf.Min(window.Height - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + radius) / _step) - window.OriginJ);

        Vector2 axis = b - a;
        float length = axis.sqrMagnitude;
        float limit = radius * radius;

        for (int j = minJ; j <= maxJ; j++)
        {
            for (int i = minI; i <= maxI; i++)
            {
                var point = new Vector2((window.OriginI + i) * _step, (window.OriginJ + j) * _step);
                float along = length < 1e-8f ? 0f : Vector2.Dot(point - a, axis) / length;

                if (along < 0f && !roundStart || along > 1f && !roundEnd)
                    continue;

                float t = Mathf.Clamp01(along);

                if ((point - (a + axis * t)).sqrMagnitude > limit || carvedOnly && Uncut(point, window.Ground[j * window.Width + i]))
                    continue;

                mask[j * window.Width + i] = true;

                if (levels != null)
                    levels[j * window.Width + i] = Mathf.Max(levels[j * window.Width + i], level);
            }
        }
    }

    private int Global(Window window, int local)
    {
        return (window.OriginJ + local / window.Width) * _nodes + window.OriginI + local % window.Width;
    }
}

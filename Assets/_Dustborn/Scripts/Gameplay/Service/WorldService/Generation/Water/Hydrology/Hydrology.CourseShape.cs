using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private void Macro(HydrologyGrid grid, List<int> cells, out List<Vector2> points, out List<float> areas)
    {
        int resolution = grid.Resolution;
        var positions = new List<Vector2>(cells.Count);
        var flows = new List<float>(cells.Count);

        foreach (int cell in cells)
        {
            positions.Add(new Vector2((cell % resolution + 0.5f) * grid.CellSize, (cell / resolution + 0.5f) * grid.CellSize));
            flows.Add(Mathf.Max(grid.Area(cell), flows.Count > 0 ? flows[^1] : 0f));
        }

        Simplify(positions, flows, SIMPLIFY_CELLS * grid.CellSize);

        for (int pass = 0; pass < CHAIKIN_PASSES; pass++)
            Chaikin(positions, flows);

        Resample(positions, flows, Mathf.Min(RESAMPLE_STEP, grid.CellSize * 0.5f), out points, out areas);
    }

    private static void Simplify(List<Vector2> positions, List<float> flows, float tolerance)
    {
        int count = positions.Count;

        if (count < 3)
            return;

        var keep = new bool[count];
        var stack = new Stack<(int From, int To)>();

        keep[0] = true;
        keep[count - 1] = true;
        stack.Push((0, count - 1));

        while (stack.Count > 0)
        {
            (int from, int to) = stack.Pop();
            Vector2 a = positions[from];
            Vector2 axis = positions[to] - a;
            float length = axis.sqrMagnitude;
            int farthest = -1;
            float worst = tolerance * tolerance;

            for (int i = from + 1; i < to; i++)
            {
                float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(positions[i] - a, axis) / length);
                float distance = (positions[i] - (a + axis * t)).sqrMagnitude;

                if (distance <= worst)
                    continue;

                worst = distance;
                farthest = i;
            }

            if (farthest < 0)
                continue;

            keep[farthest] = true;
            stack.Push((from, farthest));
            stack.Push((farthest, to));
        }

        int write = 0;

        for (int i = 0; i < count; i++)
        {
            if (!keep[i])
                continue;

            positions[write] = positions[i];
            flows[write] = flows[i];
            write++;
        }

        positions.RemoveRange(write, count - write);
        flows.RemoveRange(write, count - write);
    }

    private void Bend(List<Vector2> points, List<float> areas, float maxWidth)
    {
        for (int pass = 0; pass < BEND_PASSES; pass++)
        {
            for (int i = 1; i + 1 < points.Count; i++)
            {
                Vector2 before = points[i] - points[i - 1];
                Vector2 after = points[i + 1] - points[i];
                float first = before.magnitude, second = after.magnitude;
                float chord = 0.5f * (first + second);
                float turn = first * second < 1e-8f ? 0f : Mathf.Acos(Mathf.Clamp(Vector2.Dot(before, after) / (first * second), -1f, 1f));

                if (turn < 1e-4f || chord / turn >= Mathf.Max(BEND_MIN_RADIUS, BEND_WIDTHS * Width(areas[i], maxWidth)))
                    continue;

                points[i] = Vector2.Lerp(points[i], 0.5f * (points[i - 1] + points[i + 1]), 0.5f);
            }
        }
    }

    private void Settle(List<Vector2> points, List<float> areas, float cell)
    {
        int count = points.Count;

        if (count < 3)
            return;

        var offsets = new float[count];
        var normals = new Vector2[count];

        for (int i = 1; i + 1 < count; i++)
        {
            Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
            normals[i] = new Vector2(-tangent.y, tangent.x);

            float reach = Mathf.Min(0.6f * cell, Mathf.Max(3f, 0.75f * Width(areas[i])));
            float best = float.MaxValue;

            for (int k = -SETTLE_TAPS; k <= SETTLE_TAPS; k++)
            {
                float offset = reach * k / SETTLE_TAPS;
                Vector2 probe = points[i] + normals[i] * offset;
                float ground = _map.SampleWorldSmooth(probe.x, probe.y) + Mathf.Abs(offset) * 0.01f;

                if (ground >= best)
                    continue;

                best = ground;
                offsets[i] = offset;
            }
        }

        var smoothed = new float[count];

        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 1; i + 1 < count; i++)
            {
                float sum = 0f;
                int taps = 0;

                for (int k = -SETTLE_SMOOTHING; k <= SETTLE_SMOOTHING; k++)
                {
                    int j = i + k;

                    if (j <= 0 || j >= count - 1)
                        continue;

                    sum += offsets[j];
                    taps++;
                }

                smoothed[i] = sum / taps;
            }

            Array.Copy(smoothed, offsets, count);
        }

        for (int i = 1; i + 1 < count; i++)
        {
            float taper = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / 3f);
            points[i] += normals[i] * (offsets[i] * taper);
        }
    }

    private static void Chaikin(List<Vector2> positions, List<float> flows)
    {
        if (positions.Count < 3)
            return;

        var smoothed = new List<Vector2>(positions.Count * 2) { positions[0] };
        var carried = new List<float>(positions.Count * 2) { flows[0] };

        for (int i = 0; i + 1 < positions.Count; i++)
        {
            Vector2 a = positions[i];
            Vector2 b = positions[i + 1];

            if (i > 0)
            {
                smoothed.Add(Vector2.Lerp(a, b, 0.25f));
                carried.Add(Mathf.Lerp(flows[i], flows[i + 1], 0.25f));
            }

            if (i + 2 < positions.Count)
            {
                smoothed.Add(Vector2.Lerp(a, b, 0.75f));
                carried.Add(Mathf.Lerp(flows[i], flows[i + 1], 0.75f));
            }
        }

        smoothed.Add(positions[^1]);
        carried.Add(flows[^1]);

        positions.Clear();
        positions.AddRange(smoothed);
        flows.Clear();
        flows.AddRange(carried);
    }

    private void Meander(List<Vector2> points, List<float> areas, float cell, int index)
    {
        int count = points.Count;

        if (_settings.Meander <= 0f || count < 4)
            return;

        var random = new Unity.Mathematics.Random((uint)(_config.Seed * 7919 + index * 104729) | 1u);
        var along = new float[count];

        for (int i = 1; i < count; i++)
            along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        var knots = new List<(float At, float Value)> { (0f, random.NextFloat(-1f, 1f)) };

        while (knots[^1].At < along[count - 1])
        {
            float width = Width(areas[Mathf.Min(count - 1, Index(along, knots[^1].At))]);
            float spacing = Mathf.Max(MEANDER_WAVE_CELLS * cell, MEANDER_WAVE_WIDTHS * width) * random.NextFloat(0.6f, 1.4f);

            knots.Add((knots[^1].At + spacing, random.NextFloat(-1f, 1f)));
        }

        var freedom = new float[count];

        for (int i = 1; i + 1 < count; i++)
            freedom[i] = Freedom(points[i], (points[i + 1] - points[i - 1]).normalized, Width(areas[i]), cell);

        Blur(freedom, 4);

        var offsets = new Vector2[count];
        int knot = 0;

        for (int i = 1; i + 1 < count; i++)
        {
            while (knot + 2 < knots.Count && knots[knot + 1].At < along[i])
                knot++;

            float t = Mathf.Clamp01((along[i] - knots[knot].At) / Mathf.Max(1e-3f, knots[knot + 1].At - knots[knot].At));
            float wave = Mathf.Lerp(knots[knot].Value, knots[knot + 1].Value, t * t * (3f - 2f * t));

            Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
            float amplitude = _settings.Meander * freedom[i] * Mathf.Min(MEANDER_CELLS * cell, MEANDER_WIDTHS * Width(areas[i]) + 2f);
            float taper = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / 6f);

            offsets[i] = new Vector2(-tangent.y, tangent.x) * (amplitude * taper * wave);
        }

        for (int i = 1; i + 1 < count; i++)
            points[i] += offsets[i];
    }

    private static int Index(float[] along, float at)
    {
        int index = Array.BinarySearch(along, at);

        return index >= 0 ? index : Mathf.Max(0, ~index - 1);
    }

    public float Freedom(Vector2 point, Vector2 tangent, float width, float cell)
    {
        var normal = new Vector2(-tangent.y, tangent.x);
        float reach = cell + width;
        float center = _map.SampleWorldSmooth(point.x, point.y);
        Vector2 left = point + normal * reach;
        Vector2 right = point - normal * reach;
        float rise = Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)) - center;
        float t = Mathf.Clamp01((rise / reach - OPEN_GRADE) / (CONFINED_GRADE - OPEN_GRADE));

        return 1f - t * t * (3f - 2f * t);
    }

    private static void Blur(float[] values, int radius)
    {
        var copy = (float[])values.Clone();

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int taps = 0;

            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;

                if (j < 0 || j >= values.Length)
                    continue;

                sum += copy[j];
                taps++;
            }

            values[i] = sum / taps;
        }
    }

    private static void Resample(List<Vector2> positions, List<float> flows, float step, out List<Vector2> points, out List<float> areas)
    {
        points = new List<Vector2> { positions[0] };
        areas = new List<float> { flows[0] };

        float carry = 0f;

        for (int i = 0; i + 1 < positions.Count; i++)
        {
            Vector2 a = positions[i];
            Vector2 b = positions[i + 1];
            float length = Vector2.Distance(a, b);

            if (length < 1e-4f)
                continue;

            float along = step - carry;

            while (along < length)
            {
                float t = along / length;

                points.Add(Vector2.Lerp(a, b, t));
                areas.Add(Mathf.Lerp(flows[i], flows[i + 1], t));
                along += step;
            }

            carry = length - (along - step);
        }

        if (Vector2.Distance(points[^1], positions[^1]) > 1e-3f)
        {
            points.Add(positions[^1]);
            areas.Add(flows[^1]);
        }
    }
}

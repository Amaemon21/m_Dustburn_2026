using System.Collections.Generic;
using UnityEngine;

public static class Delaunay
{
    private const double SUPER_SCALE = 64.0;
    private const double CIRCLE_EPSILON = 1e-9;

    private struct Triangle
    {
        public int A;
        public int B;
        public int C;
        public double CenterX;
        public double CenterY;
        public double RadiusSqr;
    }

    public static List<(int From, int To)> Edges(IReadOnlyList<Vector2> points)
    {
        var result = new List<(int From, int To)>();
        int count = points.Count;

        if (count < 2)
            return result;

        if (count == 2)
        {
            result.Add((0, 1));
            return result;
        }

        var xs = new double[count + 3];
        var ys = new double[count + 3];

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < count; i++)
        {
            xs[i] = points[i].x;
            ys[i] = points[i].y;
            minX = System.Math.Min(minX, xs[i]);
            minY = System.Math.Min(minY, ys[i]);
            maxX = System.Math.Max(maxX, xs[i]);
            maxY = System.Math.Max(maxY, ys[i]);
        }

        double span = System.Math.Max(1.0, System.Math.Max(maxX - minX, maxY - minY)) * SUPER_SCALE;
        double middleX = (minX + maxX) * 0.5;
        double middleY = (minY + maxY) * 0.5;

        xs[count] = middleX - span;
        ys[count] = middleY - span;
        xs[count + 1] = middleX + span;
        ys[count + 1] = middleY - span;
        xs[count + 2] = middleX;
        ys[count + 2] = middleY + span;

        var triangles = new List<Triangle> { Make(count, count + 1, count + 2, xs, ys) };
        var boundary = new List<(int, int)>();
        var survivors = new List<Triangle>();

        for (int point = 0; point < count; point++)
        {
            boundary.Clear();
            survivors.Clear();

            var edgeCounts = new Dictionary<long, int>();
            var bad = new List<Triangle>();

            foreach (Triangle triangle in triangles)
            {
                double deltaX = xs[point] - triangle.CenterX;
                double deltaY = ys[point] - triangle.CenterY;

                if (deltaX * deltaX + deltaY * deltaY < triangle.RadiusSqr * (1.0 - CIRCLE_EPSILON))
                {
                    bad.Add(triangle);
                    Count(edgeCounts, triangle.A, triangle.B);
                    Count(edgeCounts, triangle.B, triangle.C);
                    Count(edgeCounts, triangle.C, triangle.A);
                    continue;
                }

                survivors.Add(triangle);
            }

            foreach (Triangle triangle in bad)
            {
                Collect(edgeCounts, boundary, triangle.A, triangle.B);
                Collect(edgeCounts, boundary, triangle.B, triangle.C);
                Collect(edgeCounts, boundary, triangle.C, triangle.A);
            }

            triangles.Clear();
            triangles.AddRange(survivors);

            foreach ((int from, int to) in boundary)
                triangles.Add(Make(from, to, point, xs, ys));
        }

        var unique = new SortedSet<long>();

        foreach (Triangle triangle in triangles)
        {
            AddEdge(unique, triangle.A, triangle.B, count);
            AddEdge(unique, triangle.B, triangle.C, count);
            AddEdge(unique, triangle.C, triangle.A, count);
        }

        foreach (long key in unique)
            result.Add(((int)(key >> 32), (int)(key & 0xffffffffL)));

        return result;
    }

    public static List<(int From, int To)> Nearest(IReadOnlyList<Vector2> points, int neighbours)
    {
        var unique = new SortedSet<long>();

        for (int i = 0; i < points.Count; i++)
        {
            var order = new List<int>(points.Count);

            for (int j = 0; j < points.Count; j++)
            {
                if (j != i)
                    order.Add(j);
            }

            int origin = i;
            order.Sort((left, right) =>
            {
                int compare = (points[left] - points[origin]).sqrMagnitude.CompareTo((points[right] - points[origin]).sqrMagnitude);
                return compare != 0 ? compare : left.CompareTo(right);
            });

            for (int k = 0; k < Mathf.Min(neighbours, order.Count); k++)
                AddEdge(unique, i, order[k], points.Count);
        }

        var result = new List<(int From, int To)>(unique.Count);

        foreach (long key in unique)
            result.Add(((int)(key >> 32), (int)(key & 0xffffffffL)));

        return result;
    }

    private static Triangle Make(int a, int b, int c, double[] xs, double[] ys)
    {
        double ax = xs[a], ay = ys[a];
        double bx = xs[b], by = ys[b];
        double cx = xs[c], cy = ys[c];

        double determinant = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

        if (System.Math.Abs(determinant) < 1e-12)
            return new Triangle { A = a, B = b, C = c, CenterX = 0.0, CenterY = 0.0, RadiusSqr = double.MaxValue };

        double aa = ax * ax + ay * ay;
        double bb = bx * bx + by * by;
        double cc = cx * cx + cy * cy;

        double centerX = (aa * (by - cy) + bb * (cy - ay) + cc * (ay - by)) / determinant;
        double centerY = (aa * (cx - bx) + bb * (ax - cx) + cc * (bx - ax)) / determinant;
        double deltaX = ax - centerX;
        double deltaY = ay - centerY;

        return new Triangle { A = a, B = b, C = c, CenterX = centerX, CenterY = centerY, RadiusSqr = deltaX * deltaX + deltaY * deltaY };
    }

    private static long Key(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);

        return ((long)low << 32) | (uint)high;
    }

    private static void Count(Dictionary<long, int> counts, int a, int b)
    {
        long key = Key(a, b);
        counts.TryGetValue(key, out int value);
        counts[key] = value + 1;
    }

    private static void Collect(Dictionary<long, int> counts, List<(int, int)> boundary, int a, int b)
    {
        if (counts[Key(a, b)] == 1)
            boundary.Add((a, b));
    }

    private static void AddEdge(SortedSet<long> unique, int a, int b, int count)
    {
        if (a >= count || b >= count || a == b)
            return;

        unique.Add(Key(a, b));
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

static class RoadSurfaceReference
{
    private const float EDGE_MARGIN = 0.003f;
    private const float OWNER_JUMP = 0.05f;

    private static readonly Vector2[] Offsets =
    {
        new(EDGE_MARGIN, 0f), new(-EDGE_MARGIN, 0f), new(0f, EDGE_MARGIN), new(0f, -EDGE_MARGIN)
    };

    public static bool Expected(RoadSurfaceMesh planner, Vector2 point, out bool covered, out Vector2 uv, out Vector3 marks)
    {
        covered = planner.Sample(point, out uv, out marks);

        foreach (Vector2 offset in Offsets)
        {
            bool near = planner.Sample(point + offset, out Vector2 nearUv, out _);

            if (near != covered || (covered && (nearUv - uv).magnitude > OWNER_JUMP))
                return false;
        }

        return true;
    }

    public static float CorridorGap(IReadOnlyList<Road> roads, Vector2 point)
    {
        float gap = float.MaxValue;

        foreach (Road road in roads)
        {
            if (road.Surface != RoadSurface.Asphalt)
                continue;

            for (int i = 1; i < road.Points.Length; i++)
            {
                Vector2 a = road.Points[i - 1], d = road.Points[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, d) / Mathf.Max(d.sqrMagnitude, 1e-9f));
                gap = Math.Min(gap, ((point - (a + d * t)).magnitude - road.HalfWidth) / road.HalfWidth);
            }
        }

        return gap;
    }

    public static int Locate(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Vector3> markings, Vector2 point, out Vector2 uv, out Vector3 marks)
    {
        int count = 0;
        uv = Vector2.zero;
        marks = Vector3.zero;

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            var a = new Vector2(vertices[ia].x, vertices[ia].z);
            var b = new Vector2(vertices[ib].x, vertices[ib].z);
            var c = new Vector2(vertices[ic].x, vertices[ic].z);
            float area = Cross(b - a, c - a);

            if (Math.Abs(area) < 1e-7f)
                continue;

            float u = Cross(b - point, c - point) / area;
            float v = Cross(c - point, a - point) / area;
            float w = 1f - u - v;

            float least = Math.Min(u, Math.Min(v, w));

            if (Math.Abs(least) <= 1e-5f)
                return -1;

            if (least < 0f)
                continue;

            count++;
            uv = uvs[ia] * u + uvs[ib] * v + uvs[ic] * w;
            marks = markings[ia] * u + markings[ib] * v + markings[ic] * w;
        }

        return count;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}

static class RoadSurfaceDebug
{
    private static string P(Vector2 v) => $"({v.x:0.0000}, {v.y:0.0000})";

    public static void Dump(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector2 point)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            var a = new Vector2(vertices[ia].x, vertices[ia].z);
            var b = new Vector2(vertices[ib].x, vertices[ib].z);
            var c = new Vector2(vertices[ic].x, vertices[ic].z);
            float area = (b - a).x * (c - a).y - (b - a).y * (c - a).x;

            if (Math.Abs(area) < 1e-7f)
                continue;

            float u = ((b - point).x * (c - point).y - (b - point).y * (c - point).x) / area;
            float v = ((c - point).x * (a - point).y - (c - point).y * (a - point).x) / area;
            float w = 1f - u - v;

            if (u < -1e-5f || v < -1e-5f || w < -1e-5f)
                continue;

            Console.WriteLine($"  tri {i / 3}: {P(a)} {P(b)} {P(c)} area {area} bary {u} {v} {w} uv {P(uvs[ia])} {P(uvs[ib])} {P(uvs[ic])}");
        }
    }
}

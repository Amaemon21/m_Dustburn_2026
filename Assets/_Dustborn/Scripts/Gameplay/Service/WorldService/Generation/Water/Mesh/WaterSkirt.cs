using System.Collections.Generic;
using UnityEngine;

public static class WaterSkirt
{
    public const float DEPTH = 3f;
    private const float PROBE = 0.5f;

    public static int Append(WaterMap water, List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
    {
        var edges = new Dictionary<(int, int), (int Count, int Third, int A, int B)>();
        int count = triangles.Count;

        for (int t = 0; t < count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = triangles[t + k], b = triangles[t + (k + 1) % 3], c = triangles[t + (k + 2) % 3];
                var key = a < b ? (a, b) : (b, a);

                edges[key] = edges.TryGetValue(key, out var edge) ? (edge.Count + 1, edge.Third, edge.A, edge.B) : (1, c, a, b);
            }
        }

        int added = 0;

        foreach (var edge in edges.Values)
        {
            if (edge.Count != 1)
                continue;

            Vector3 a = vertices[edge.A], b = vertices[edge.B], c = vertices[edge.Third];
            var along = new Vector2(b.x - a.x, b.z - a.z);

            if (along.sqrMagnitude < 1e-6f)
                continue;

            var outward = new Vector2(along.y, -along.x).normalized;

            if (Vector2.Dot(outward, new Vector2(c.x - a.x, c.z - a.z)) > 0f)
                outward = -outward;

            Vector2 probe = new Vector2(0.5f * (a.x + b.x), 0.5f * (a.z + b.z)) + outward * PROBE;

            if (probe.x < 0f || probe.y < 0f || probe.x > water.WorldSize || probe.y > water.WorldSize || water.Sample(probe.x, probe.y).IsWater)
                continue;

            AddQuad(vertices, uv, triangles, edge.A, edge.B, new Vector3(outward.x, 0f, outward.y));
            added++;
        }

        return added;
    }

    private static void AddQuad(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, int a, int b, Vector3 outward)
    {
        int start = vertices.Count;
        var down = new Vector3(0f, -DEPTH, 0f);

        vertices.Add(vertices[a]);
        vertices.Add(vertices[b]);
        vertices.Add(vertices[b] + down);
        vertices.Add(vertices[a] + down);
        uv.Add(uv[a]);
        uv.Add(uv[b]);
        uv.Add(uv[b]);
        uv.Add(uv[a]);

        Vector3 normal = Vector3.Cross(vertices[start + 1] - vertices[start], vertices[start + 2] - vertices[start]);

        if (Vector3.Dot(normal, outward) >= 0f)
        {
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            return;
        }

        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
    }
}

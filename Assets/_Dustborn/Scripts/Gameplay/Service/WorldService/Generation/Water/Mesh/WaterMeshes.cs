using System.Collections.Generic;
using UnityEngine;

public static partial class WaterMeshes
{
    public const float TILE = 1024f;
    public const float RIBBON_PAD = 0.5f;
    public const float UNDERLAP = WaterMap.RIVER_UNDERLAP;
    public const float MOUTH_BLEND = 0.05f;
    public const int MOUTH_REACH = 4;
    public const float COLUMN = 4f;
    private const int CROSSING_STEPS = 12;
    private const int SPLIT_DEPTH = 3;
    private const int COVER_LATTICE = 4;

    public static List<WaterMeshPart> Build(WaterMap water)
    {
        var parts = new Dictionary<(WaterKind, int, int, bool), WaterMeshPart>();

        Rivers(water, parts);
        StandingWaterMesh.Build(water, parts);

        var list = new List<WaterMeshPart>();

        foreach (WaterMeshPart part in parts.Values)
        {
            if (part.Triangles.Count > 0)
                list.Add(part);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public static WaterMeshPart Part(Dictionary<(WaterKind, int, int, bool), WaterMeshPart> parts, WaterKind kind, Vector2 at, bool frozen = false)
    {
        var key = (kind, Mathf.FloorToInt(at.x / TILE), Mathf.FloorToInt(at.y / TILE), frozen);

        if (!parts.TryGetValue(key, out WaterMeshPart part))
            parts[key] = part = new WaterMeshPart { Kind = kind, Frozen = frozen, Name = frozen ? $"{kind} {key.Item2} {key.Item3} ice" : $"{kind} {key.Item2} {key.Item3}" };

        return part;
    }

    public static void Tessellate(List<Vector3> vertices, List<int> triangles, float maxEdge)
    {
        float limit = maxEdge * maxEdge;
        var pending = new Stack<(int, int, int)>();
        var source = new List<int>(triangles);
        triangles.Clear();

        for (int i = 0; i < source.Count; i += 3)
            pending.Push((source[i], source[i + 1], source[i + 2]));

        while (pending.Count > 0)
        {
            (int a, int b, int c) = pending.Pop();
            float ab = Square(vertices[a], vertices[b]), bc = Square(vertices[b], vertices[c]), ca = Square(vertices[c], vertices[a]);

            if (ab <= limit && bc <= limit && ca <= limit)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                continue;
            }

            if (bc >= ab && bc >= ca)
                (a, b, c) = (b, c, a);
            else if (ca >= ab && ca >= bc)
                (a, b, c) = (c, a, b);

            int middle = vertices.Count;
            vertices.Add(new Vector3(0.5f * (vertices[a].x + vertices[b].x), 0.5f * (vertices[a].y + vertices[b].y), 0.5f * (vertices[a].z + vertices[b].z)));
            pending.Push((a, middle, c));
            pending.Push((middle, b, c));
        }
    }

    private static float Square(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
        return x * x + y * y + z * z;
    }

    public const float OFFSHORE_STEP = 512f;
    public const float FLOOR_EDGE_STEP = 32f;
    private const float BORDER_EPSILON = 1e-3f;
    private const float OFFSHORE_UV = 1f / 16f;
}

using System.Collections.Generic;
using UnityEngine;

public static partial class WaterMeshes
{
    public static WaterMeshPart Offshore(WaterMap water, IReadOnlyList<WaterMeshPart> parts, float reach)
    {
        var inner = new List<Vector2>();

        foreach (WaterMeshPart part in parts)
        {
            if (part.Kind != WaterKind.Sea || part.Frozen)
                continue;

            foreach (Vector3 vertex in part.Vertices)
            {
                if (Mathf.Abs(vertex.y - water.SeaLevel) < BORDER_EPSILON && OnBorder(vertex.x, vertex.z, water.WorldSize))
                    inner.Add(new Vector2(vertex.x, vertex.z));
            }
        }

        var ring = new WaterMeshPart { Name = "Sea offshore", Kind = WaterKind.Sea };
        Ring(ring, water.WorldSize, water.SeaLevel, reach, inner, OFFSHORE_UV, false);
        return ring;
    }

    public static WaterMeshPart OffshoreFloor(float worldSize, float floor, float reach)
    {
        var inner = new List<Vector2>();

        for (float along = 0f; along <= worldSize; along += FLOOR_EDGE_STEP)
        {
            inner.Add(new Vector2(along, 0f));
            inner.Add(new Vector2(along, worldSize));
            inner.Add(new Vector2(0f, along));
            inner.Add(new Vector2(worldSize, along));
        }

        var ring = new WaterMeshPart { Name = "Sea floor offshore", Kind = WaterKind.None };
        Ring(ring, worldSize, floor, reach, inner, 1f / worldSize, true);
        return ring;
    }

    private static bool OnBorder(float x, float z, float world)
    {
        return Mathf.Abs(x) < BORDER_EPSILON || Mathf.Abs(z) < BORDER_EPSILON || Mathf.Abs(x - world) < BORDER_EPSILON || Mathf.Abs(z - world) < BORDER_EPSILON;
    }

    private static void Ring(WaterMeshPart part, float world, float height, float reach, List<Vector2> inner, float uvScale, bool clampUv)
    {
        if (reach <= 0f)
            return;

        int outside = Mathf.Max(1, Mathf.CeilToInt(reach / OFFSHORE_STEP));
        float step = OFFSHORE_STEP;
        var welds = new Dictionary<(long, long), int>();

        int Vertex(Vector2 at)
        {
            var key = ((long)System.Math.Round(at.x * 1000f), (long)System.Math.Round(at.y * 1000f));

            if (welds.TryGetValue(key, out int index))
                return index;

            index = part.Vertices.Count;
            welds[key] = index;
            part.Vertices.Add(new Vector3(at.x, height, at.y));
            part.Uv.Add(clampUv ? new Vector2(Mathf.Clamp01(at.x * uvScale), Mathf.Clamp01(at.y * uvScale)) : at * uvScale);
            return index;
        }

        void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            float facing = (b.y - a.y) * (c.x - a.x) - (b.x - a.x) * (c.y - a.y);

            if (Mathf.Abs(facing) < 1e-6f)
                return;

            if (facing < 0f)
                (b, c) = (c, b);

            part.Triangles.Add(Vertex(a));
            part.Triangles.Add(Vertex(b));
            part.Triangles.Add(Vertex(c));
            part.Sources.Add(-1);
        }

        for (int side = 0; side < 4; side++)
        {
            var edge = new List<float> { 0f, world };

            foreach (Vector2 point in inner)
            {
                if (Side(point, world) == side)
                    edge.Add(Along(point, side));
            }

            edge.Sort();
            var near = new List<Vector2>();

            foreach (float along in edge)
            {
                if (near.Count == 0 || along - Along(near[^1], side) > BORDER_EPSILON)
                    near.Add(Place(side, along, 0f, world));
            }

            var outer = new List<Vector2>();

            for (float along = 0f; along < world + 0.5f * step; along += step)
                outer.Add(Place(side, Mathf.Min(along, world), step, world));

            Zip(near, outer, side, Triangle);

            for (int band = 1; band < outside; band++)
            {
                for (float along = 0f; along < world - 0.5f; along += step)
                {
                    float next = Mathf.Min(world, along + step);
                    Vector2 a = Place(side, along, band * step, world), b = Place(side, next, band * step, world);
                    Vector2 c = Place(side, next, (band + 1) * step, world), d = Place(side, along, (band + 1) * step, world);
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
            }
        }

        foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(world, 0f), new Vector2(world, world), new Vector2(0f, world) })
        {
            float sx = corner.x > 0f ? 1f : -1f, sz = corner.y > 0f ? 1f : -1f;

            for (int u = 0; u < outside; u++)
            {
                for (int v = 0; v < outside; v++)
                {
                    Vector2 a = corner + new Vector2(sx * u * step, sz * v * step);
                    Vector2 b = corner + new Vector2(sx * (u + 1) * step, sz * v * step);
                    Vector2 c = corner + new Vector2(sx * (u + 1) * step, sz * (v + 1) * step);
                    Vector2 d = corner + new Vector2(sx * u * step, sz * (v + 1) * step);
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
            }
        }
    }

    private static int Side(Vector2 point, float world)
    {
        if (Mathf.Abs(point.y) < BORDER_EPSILON)
            return 0;

        if (Mathf.Abs(point.x - world) < BORDER_EPSILON)
            return 1;

        if (Mathf.Abs(point.y - world) < BORDER_EPSILON)
            return 2;

        return Mathf.Abs(point.x) < BORDER_EPSILON ? 3 : -1;
    }

    private static float Along(Vector2 point, int side)
    {
        return side == 0 || side == 2 ? point.x : point.y;
    }

    private static Vector2 Place(int side, float along, float outward, float world)
    {
        return side switch
        {
            0 => new Vector2(along, -outward),
            1 => new Vector2(world + outward, along),
            2 => new Vector2(along, world + outward),
            _ => new Vector2(-outward, along)
        };
    }

    private static void Zip(List<Vector2> near, List<Vector2> outer, int side, System.Action<Vector2, Vector2, Vector2> triangle)
    {
        int i = 0, j = 0;

        while (i + 1 < near.Count || j + 1 < outer.Count)
        {
            bool advanceNear = j + 1 >= outer.Count || i + 1 < near.Count && Along(near[i + 1], side) <= Along(outer[j + 1], side);

            if (advanceNear)
            {
                triangle(near[i], near[i + 1], outer[j]);
                i++;
            }
            else
            {
                triangle(near[i], outer[j + 1], outer[j]);
                j++;
            }
        }
    }
}

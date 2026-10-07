using System.Collections.Generic;
using UnityEngine;

public static partial class StandingWaterMesh
{
    private static WaterMeshPart Part(Builder builder, short owner, int cell)
    {
        float x = cell % builder.Water.Resolution * builder.Water.CellSize;
        float z = cell / builder.Water.Resolution * builder.Water.CellSize;

        return WaterMeshes.Part(builder.Parts, owner == WaterMap.OWNER_SEA ? WaterKind.Sea : WaterKind.Lake, new Vector2(x, z), builder.Water.FrozenOwner(owner, cell));
    }

    private static int Source(short owner)
    {
        return owner == WaterMap.OWNER_SEA ? -1 : -(owner + 2);
    }

    private static int NodeVertex(Builder builder, WaterMeshPart part, short owner, int i, int j)
    {
        long key = Key(owner, j * builder.Nodes + i, NODE_VERTEX);

        return Weld(builder, part, key, new Vector2(i * builder.Step, j * builder.Step), builder.Water.LevelOf(owner));
    }

    private static long Key(short owner, int node, int type)
    {
        return ((long)(owner + 3) << 40) | ((long)node << 2) | (long)type;
    }

    private static int Weld(Builder builder, WaterMeshPart part, long key, Vector2 at, float surface)
    {
        if (!builder.Welds.TryGetValue(part, out Dictionary<long, int> welds))
            builder.Welds[part] = welds = new Dictionary<long, int>();

        if (welds.TryGetValue(key, out int index))
            return index;

        index = Vertex(part, at, surface);
        welds[key] = index;
        return index;
    }

    private static int Vertex(WaterMeshPart part, Vector2 at, float surface)
    {
        int index = part.Vertices.Count;

        part.Vertices.Add(new Vector3(at.x, surface, at.y));
        part.Uv.Add(at * UV_SCALE);

        return index;
    }

    private static void Triangle(WaterMeshPart part, int source, int a, int b, int c)
    {
        Vector3 first = part.Vertices[a];
        Vector3 second = part.Vertices[b];
        Vector3 third = part.Vertices[c];

        float facing = (second.z - first.z) * (third.x - first.x) - (second.x - first.x) * (third.z - first.z);

        if (facing <= MIN_FACING)
            return;

        part.Triangles.Add(a);
        part.Triangles.Add(b);
        part.Triangles.Add(c);
        part.Sources.Add(source);
    }
}

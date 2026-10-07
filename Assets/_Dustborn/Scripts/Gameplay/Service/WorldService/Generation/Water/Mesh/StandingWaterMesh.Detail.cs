using System.Collections.Generic;
using UnityEngine;

public static partial class StandingWaterMesh
{
    private static void Detail(Builder builder, int cell)
    {
        WaterMap water = builder.Water;
        int start = water.DetailStart(cell);
        var owners = new List<short>(2);

        for (int k = 0; k < WaterMap.CELL_NODES; k++)
        {
            short owner = water.DetailOwner(start + k);

            if (owner == WaterMap.OWNER_NONE || float.IsNegativeInfinity(water.LevelOf(owner)) || owners.Contains(owner))
                continue;

            owners.Add(owner);
        }

        var values = new float[WaterMap.CELL_NODES];

        foreach (short owner in owners)
        {
            bool any = false;

            for (int k = 0; k < WaterMap.CELL_NODES; k++)
            {
                values[k] = water.NodeValue(owner, water.DetailOwner(start + k), water.DetailGround(start + k));
                any |= values[k] > 0f;
            }

            if (any)
                March(builder, cell, owner, values);
        }
    }

    private static void March(Builder builder, int cell, short owner, float[] values)
    {
        WaterMap water = builder.Water;
        int sub = WaterMap.SUBDIVISION;
        int side = WaterMap.CELL_SIDE_NODES;
        int i0 = cell % water.Resolution * sub;
        int j0 = cell / water.Resolution * sub;
        WaterMeshPart part = Part(builder, owner, cell);
        int source = Source(owner);

        var corners = new int[4];
        var inside = new bool[4];
        var level = new float[4];
        List<int> polygon = builder.Polygon;

        for (int fz = 0; fz < sub; fz++)
        {
            for (int fx = 0; fx < sub; fx++)
            {
                int any = 0;

                for (int k = 0; k < 4; k++)
                {
                    int cx = fx + (k == 1 || k == 2 ? 1 : 0);
                    int cz = fz + (k >= 2 ? 1 : 0);

                    corners[k] = cz * side + cx;
                    level[k] = values[corners[k]];
                    inside[k] = level[k] > 0f;
                    any += inside[k] ? 1 : 0;
                }

                if (any == 0)
                    continue;

                polygon.Clear();

                for (int k = 0; k < 4; k++)
                {
                    int next = (k + 1) & 3;

                    if (inside[k])
                        polygon.Add(CornerVertex(builder, part, owner, i0, j0, corners[k]));

                    if (inside[k] != inside[next])
                        polygon.Add(CrossingVertex(builder, part, owner, i0, j0, corners[k], level[k], corners[next], level[next]));
                }

                if (MarchingCell.Split(level[0], level[1], level[2], level[3]))
                {
                    for (int p = 0; p < polygon.Count; p++)
                    {
                        if (!IsCornerSlot(polygon.Count, p, inside[0]))
                            continue;

                        Triangle(part, source, polygon[p], polygon[(p + polygon.Count - 1) % polygon.Count], polygon[(p + 1) % polygon.Count]);
                    }

                    continue;
                }

                for (int p = 1; p + 1 < polygon.Count; p++)
                    Triangle(part, source, polygon[0], polygon[p + 1], polygon[p]);
            }
        }
    }

    private static bool IsCornerSlot(int count, int slot, bool firstInside)
    {
        return firstInside ? slot == 0 || slot == 3 : slot == 1 || slot == 4;
    }

    private static int CornerVertex(Builder builder, WaterMeshPart part, short owner, int i0, int j0, int local)
    {
        int side = WaterMap.CELL_SIDE_NODES;

        return NodeVertex(builder, part, owner, i0 + local % side, j0 + local / side);
    }

    private static int CrossingVertex(Builder builder, WaterMeshPart part, short owner, int i0, int j0, int localA, float valueA, int localB, float valueB)
    {
        if (localB < localA)
        {
            (localA, localB) = (localB, localA);
            (valueA, valueB) = (valueB, valueA);
        }

        int side = WaterMap.CELL_SIDE_NODES;
        int ia = i0 + localA % side, ja = j0 + localA / side;
        int ib = i0 + localB % side, jb = j0 + localB / side;
        int type = jb == ja ? EAST_CROSSING : NORTH_CROSSING;
        long key = Key(owner, ja * builder.Nodes + ia, type);

        float t = MarchingCell.Crossing(valueA, valueB);
        var a = new Vector2(ia * builder.Step, ja * builder.Step);
        var b = new Vector2(ib * builder.Step, jb * builder.Step);

        return Weld(builder, part, key, Vector2.Lerp(a, b, t), builder.Water.LevelOf(owner));
    }

    private static void Fan(Builder builder, int cell, short owner)
    {
        WaterMap water = builder.Water;
        int sub = WaterMap.SUBDIVISION;
        int column = cell % water.Resolution;
        int row = cell / water.Resolution;
        int i0 = column * sub, j0 = row * sub;

        WaterMeshPart part = Part(builder, owner, cell);
        List<int> ring = builder.Polygon;
        ring.Clear();

        bool south = Split(water, column, row - 1, owner);
        bool east = Split(water, column + 1, row, owner);
        bool north = Split(water, column, row + 1, owner);
        bool west = Split(water, column - 1, row, owner);

        for (int k = 0; k < sub; k++)
        {
            if (k == 0 || south)
                ring.Add(NodeVertex(builder, part, owner, i0 + k, j0));
        }

        for (int k = 0; k < sub; k++)
        {
            if (k == 0 || east)
                ring.Add(NodeVertex(builder, part, owner, i0 + sub, j0 + k));
        }

        for (int k = 0; k < sub; k++)
        {
            if (k == 0 || north)
                ring.Add(NodeVertex(builder, part, owner, i0 + sub - k, j0 + sub));
        }

        for (int k = 0; k < sub; k++)
        {
            if (k == 0 || west)
                ring.Add(NodeVertex(builder, part, owner, i0, j0 + sub - k));
        }

        if (!south && !east && !north && !west)
        {
            int source = Source(owner);
            Triangle(part, source, ring[0], ring[2], ring[1]);
            Triangle(part, source, ring[0], ring[3], ring[2]);
            return;
        }

        var center = new Vector2((column + 0.5f) * water.CellSize, (row + 0.5f) * water.CellSize);
        FanAround(part, Source(owner), Vertex(part, center, water.LevelOf(owner)), ring);
    }

    private static void FanAround(WaterMeshPart part, int source, int center, List<int> ring)
    {
        for (int k = 0; k < ring.Count; k++)
            Triangle(part, source, center, ring[(k + 1) % ring.Count], ring[k]);
    }
}

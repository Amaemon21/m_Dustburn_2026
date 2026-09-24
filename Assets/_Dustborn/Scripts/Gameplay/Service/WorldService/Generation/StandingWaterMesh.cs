using System.Collections.Generic;
using UnityEngine;

public static class StandingWaterMesh
{
    private const float UV_SCALE = 1f / 16f;
    private const float MIN_FACING = 1e-6f;

    private const int NODE_VERTEX = 0;
    private const int EAST_CROSSING = 1;
    private const int NORTH_CROSSING = 2;

    private sealed class Builder
    {
        public readonly WaterMap Water;
        public readonly Dictionary<(WaterKind, int, int), WaterMeshPart> Parts;
        public readonly Dictionary<WaterMeshPart, Dictionary<long, int>> Welds = new();
        public readonly int Nodes;
        public readonly float Step;
        public readonly List<int> Polygon = new(8);

        public Builder(WaterMap water, Dictionary<(WaterKind, int, int), WaterMeshPart> parts)
        {
            Water = water;
            Parts = parts;
            Nodes = water.NodeResolution;
            Step = water.NodeStep;
        }
    }

    private readonly struct Block
    {
        public readonly int C0, R0, C1, R1;
        public readonly short Owner;

        public Block(int c0, int r0, int c1, int r1, short owner)
        {
            C0 = c0;
            R0 = r0;
            C1 = c1;
            R1 = r1;
            Owner = owner;
        }
    }

    public static void Build(WaterMap water, Dictionary<(WaterKind, int, int), WaterMeshPart> parts)
    {
        var builder = new Builder(water, parts);
        int resolution = water.Resolution;
        var deep = new bool[resolution * resolution];
        var corners = new bool[(resolution + 1) * (resolution + 1)];

        for (int cell = 0; cell < deep.Length; cell++)
        {
            bool detail = water.DetailSlot[cell] >= 0;

            if (!detail && !water.IsFull(cell))
                continue;

            if (!detail && Deep(water, cell, water.CellOwner(cell)))
            {
                deep[cell] = true;
                continue;
            }

            MarkCorners(corners, resolution, cell % resolution, cell / resolution, cell % resolution + 1, cell / resolution + 1);
        }

        List<Block> blocks = Blocks(water, deep);

        foreach (Block block in blocks)
            MarkCorners(corners, resolution, block.C0, block.R0, block.C1, block.R1);

        for (int cell = 0; cell < deep.Length; cell++)
        {
            if (water.DetailSlot[cell] >= 0)
            {
                Detail(builder, cell);
                continue;
            }

            if (water.IsFull(cell) && !deep[cell])
                Fan(builder, cell, water.CellOwner(cell));
        }

        foreach (Block block in blocks)
            Rectangle(builder, corners, block);
    }

    private static void MarkCorners(bool[] corners, int resolution, int c0, int r0, int c1, int r1)
    {
        int side = resolution + 1;

        corners[r0 * side + c0] = true;
        corners[r0 * side + c1] = true;
        corners[r1 * side + c0] = true;
        corners[r1 * side + c1] = true;
    }

    private static bool Full(WaterMap water, int column, int row, short owner)
    {
        int resolution = water.Resolution;

        if (column < 0 || row < 0 || column >= resolution || row >= resolution)
            return false;

        int cell = row * resolution + column;

        return water.IsFull(cell) && water.CellOwner(cell) == owner;
    }

    private static bool Deep(WaterMap water, int cell, short owner)
    {
        int column = cell % water.Resolution;
        int row = cell / water.Resolution;

        return Full(water, column + 1, row, owner) && Full(water, column - 1, row, owner) && Full(water, column, row + 1, owner) && Full(water, column, row - 1, owner);
    }

    private static WaterMeshPart Part(Builder builder, short owner, int cell)
    {
        float x = cell % builder.Water.Resolution * builder.Water.CellSize;
        float z = cell / builder.Water.Resolution * builder.Water.CellSize;

        return WaterMeshes.Part(builder.Parts, owner == WaterMap.OWNER_SEA ? WaterKind.Sea : WaterKind.Lake, new Vector2(x, z));
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

    private static void FanAround(WaterMeshPart part, int source, int center, List<int> ring)
    {
        for (int k = 0; k < ring.Count; k++)
            Triangle(part, source, center, ring[(k + 1) % ring.Count], ring[k]);
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

    private static bool Split(WaterMap water, int column, int row, short owner)
    {
        int resolution = water.Resolution;

        if (column < 0 || row < 0 || column >= resolution || row >= resolution)
            return false;

        return !Full(water, column, row, owner);
    }

    private static List<Block> Blocks(WaterMap water, bool[] deep)
    {
        int resolution = water.Resolution;
        int tileCells = Mathf.Max(1, Mathf.RoundToInt(WaterMeshes.TILE / water.CellSize));
        var used = new bool[deep.Length];
        var blocks = new List<Block>();

        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;

                if (!deep[index] || used[index])
                    continue;

                short owner = water.CellOwner(index);
                int limitColumn = Mathf.Min(resolution, (column / tileCells + 1) * tileCells);
                int limitRow = Mathf.Min(resolution, (row / tileCells + 1) * tileCells);
                int size = 1;

                while (column + size < limitColumn && row + size < limitRow
                    && Span(water, deep, used, owner, column + size, row, column + size + 1, row + size + 1)
                    && Span(water, deep, used, owner, column, row + size, column + size, row + size + 1))
                    size++;

                int right = column + size;

                while (right < limitColumn && Span(water, deep, used, owner, right, row, right + 1, row + size))
                    right++;

                int top = row + size;

                while (top < limitRow && Span(water, deep, used, owner, column, top, right, top + 1))
                    top++;

                for (int r = row; r < top; r++)
                {
                    for (int c = column; c < right; c++)
                        used[r * resolution + c] = true;
                }

                blocks.Add(new Block(column, row, right, top, owner));
            }
        }

        return blocks;
    }

    private static bool Span(WaterMap water, bool[] deep, bool[] used, short owner, int c0, int r0, int c1, int r1)
    {
        for (int r = r0; r < r1; r++)
        {
            for (int c = c0; c < c1; c++)
            {
                int index = r * water.Resolution + c;

                if (!deep[index] || used[index] || water.CellOwner(index) != owner)
                    return false;
            }
        }

        return true;
    }

    private static void Rectangle(Builder builder, bool[] corners, Block block)
    {
        WaterMap water = builder.Water;
        int sub = WaterMap.SUBDIVISION;
        int side = water.Resolution + 1;
        short owner = block.Owner;
        WaterMeshPart part = Part(builder, owner, block.R0 * water.Resolution + block.C0);
        List<int> ring = builder.Polygon;
        ring.Clear();

        for (int c = block.C0; c < block.C1; c++)
        {
            if (corners[block.R0 * side + c])
                ring.Add(NodeVertex(builder, part, owner, c * sub, block.R0 * sub));
        }

        for (int r = block.R0; r < block.R1; r++)
        {
            if (corners[r * side + block.C1])
                ring.Add(NodeVertex(builder, part, owner, block.C1 * sub, r * sub));
        }

        for (int c = block.C1; c > block.C0; c--)
        {
            if (corners[block.R1 * side + c])
                ring.Add(NodeVertex(builder, part, owner, c * sub, block.R1 * sub));
        }

        for (int r = block.R1; r > block.R0; r--)
        {
            if (corners[r * side + block.C0])
                ring.Add(NodeVertex(builder, part, owner, block.C0 * sub, r * sub));
        }

        if (ring.Count == 4)
        {
            Triangle(part, Source(owner), ring[0], ring[2], ring[1]);
            Triangle(part, Source(owner), ring[0], ring[3], ring[2]);
            return;
        }

        var center = new Vector2(0.5f * (block.C0 + block.C1) * water.CellSize, 0.5f * (block.R0 + block.R1) * water.CellSize);
        FanAround(part, Source(owner), Vertex(part, center, water.LevelOf(owner)), ring);
    }

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
}

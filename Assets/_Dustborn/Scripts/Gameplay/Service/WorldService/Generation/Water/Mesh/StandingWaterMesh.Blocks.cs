using System.Collections.Generic;
using UnityEngine;

public static partial class StandingWaterMesh
{
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

    private static void MarkCorners(bool[] corners, int resolution, int c0, int r0, int c1, int r1)
    {
        int side = resolution + 1;

        corners[r0 * side + c0] = true;
        corners[r0 * side + c1] = true;
        corners[r1 * side + c0] = true;
        corners[r1 * side + c1] = true;
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static partial class WaterShore
{
    public const float SEAL_DEPTH = 4f;
    public const float SEAL_RAMP = 0.5f;
    private const float SEAL_RIVER_PAD = 2f;
    private const int SEAL_PASSES = 3;
    private const int SEAL_REACH = 3;
    private const int SEAL_FADE = 3;
    private const int SEAL_MARGIN = SEAL_REACH + SEAL_FADE;
    private const float DIAGONAL = 1.41421356f;
    private const byte DRY = 0;
    private const byte STANDING = 1;
    private const byte RIVER = 2;

    private static int Seal(WaterMap water, HeightMap map)
    {
        var cells = new List<int>();

        for (int cell = 0; cell < water.Kinds.Length; cell++)
        {
            if (water.DetailSlot[cell] >= 0 || water.Kinds[cell] == 0 && water.ShoreDistance[cell] <= water.CellSize)
                cells.Add(cell);
        }

        int raised = 0;

        Parallel.For(0, cells.Count, () => new SealWindow(), (k, _, window) =>
        {
            int count = SealCell(water, map, cells[k], window);

            if (count > 0)
                Interlocked.Add(ref raised, count);

            return window;
        }, _ => { });

        return raised;
    }

    private sealed class SealWindow
    {
        public byte[] State = new byte[0];
        public float[] Level = new float[0];
        public float[] Wet = new float[0];
        public float[] Dry = new float[0];

        public int Left;
        public int Bottom;
        public int Width;
        public int Height;

        public void Ensure(int size)
        {
            if (State.Length >= size)
                return;

            State = new byte[size];
            Level = new float[size];
            Wet = new float[size];
            Dry = new float[size];
        }
    }

    private static int SealCell(WaterMap water, HeightMap map, int cell, SealWindow window)
    {
        int resolution = map.Resolution;
        float texel = (float)map.WorldSize / (resolution - 1);
        float maxHeight = map.MaxHeight;
        float[] heights = map.Heights;
        int cellX = cell % water.Resolution, cellZ = cell / water.Resolution;
        int c0 = Mathf.CeilToInt(cellX * water.CellSize / texel), c1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((cellX + 1) * water.CellSize / texel) - 1);
        int r0 = Mathf.CeilToInt(cellZ * water.CellSize / texel), r1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((cellZ + 1) * water.CellSize / texel) - 1);

        if (c1 < c0 || r1 < r0)
            return 0;

        Classify(water, window, texel, resolution, Mathf.Max(0, c0 - SEAL_MARGIN), Mathf.Max(0, r0 - SEAL_MARGIN),
            Mathf.Min(resolution - 1, c1 + SEAL_MARGIN), Mathf.Min(resolution - 1, r1 + SEAL_MARGIN));

        int count = 0;

        for (int row = r0; row <= r1; row++)
        {
            for (int column = c0; column <= c1; column++)
            {
                int local = (row - window.Bottom) * window.Width + column - window.Left;
                int index = row * resolution + column;
                float ground = heights[index] * maxHeight;
                float x = column * texel, z = row * texel;
                float target;

                switch (window.State[local])
                {
                    case DRY:
                        if (!DryTarget(water, window, cell, local, x, z, ground, texel, out target))
                            continue;

                        break;
                    case STANDING:
                        if (window.Dry[local] > SEAL_MARGIN)
                            continue;

                        target = window.Level[local] + WaterMap.MESH_UNDERLAP - SEAL_RAMP * window.Dry[local] * texel;
                        break;
                    default:
                        continue;
                }

                if (ground >= target)
                    continue;

                heights[index] = target / maxHeight;
                count++;
            }
        }

        return count;
    }

    private static bool DryTarget(WaterMap water, SealWindow window, int cell, int local, float x, float z, float ground, float texel, out float target)
    {
        target = 0f;
        float distance = window.Wet[local];

        if (distance > SEAL_MARGIN)
            return false;

        float level = DryLevel(water, cell, x, z);

        if (float.IsNegativeInfinity(level) || ground >= level + WaterMap.MESH_UNDERLAP || ground < level - SEAL_DEPTH)
            return false;

        if (distance <= SEAL_REACH)
        {
            target = Mathf.Max(level + WaterMap.MESH_UNDERLAP, NodeGround(water, cell, x, z));
            return true;
        }

        target = level + WaterMap.MESH_UNDERLAP - SEAL_RAMP * (distance - SEAL_REACH) * texel;
        return true;
    }

    private static void Classify(WaterMap water, SealWindow window, float texel, int resolution, int left, int bottom, int right, int top)
    {
        window.Left = left;
        window.Bottom = bottom;
        window.Width = right - left + 1;
        window.Height = top - bottom + 1;
        window.Ensure(window.Width * window.Height);

        for (int row = bottom; row <= top; row++)
        {
            for (int column = left; column <= right; column++)
            {
                int local = (row - bottom) * window.Width + column - left;
                float x = column * texel, z = row * texel;

                if (water.Covers(x, z, out short owner))
                {
                    window.State[local] = STANDING;
                    window.Level[local] = water.LevelOf(owner);
                }
                else
                {
                    window.State[local] = water.TryRiver(x, z, out _) ? RIVER : DRY;
                }

                bool dry = window.State[local] == DRY;
                window.Wet[local] = dry ? float.MaxValue : 0f;
                window.Dry[local] = dry ? 0f : float.MaxValue;
            }
        }

        Chamfer(window, window.Wet);
        Chamfer(window, window.Dry);
    }

    private static void Chamfer(SealWindow window, float[] field)
    {
        int width = window.Width, height = window.Height;

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int i = row * width + column;
                float value = field[i];

                if (column > 0)
                    value = Mathf.Min(value, field[i - 1] + 1f);

                if (row > 0)
                {
                    value = Mathf.Min(value, field[i - width] + 1f);

                    if (column > 0)
                        value = Mathf.Min(value, field[i - width - 1] + DIAGONAL);

                    if (column < width - 1)
                        value = Mathf.Min(value, field[i - width + 1] + DIAGONAL);
                }

                field[i] = value;
            }
        }

        for (int row = height - 1; row >= 0; row--)
        {
            for (int column = width - 1; column >= 0; column--)
            {
                int i = row * width + column;
                float value = field[i];

                if (column < width - 1)
                    value = Mathf.Min(value, field[i + 1] + 1f);

                if (row < height - 1)
                {
                    value = Mathf.Min(value, field[i + width] + 1f);

                    if (column < width - 1)
                        value = Mathf.Min(value, field[i + width + 1] + DIAGONAL);

                    if (column > 0)
                        value = Mathf.Min(value, field[i + width - 1] + DIAGONAL);
                }

                field[i] = value;
            }
        }
    }

    private static float DryLevel(WaterMap water, int cell, float x, float z)
    {
        if (water.InsideDrawnEarlierRiver(water.Rivers.Count, new Vector2(x, z), SEAL_RIVER_PAD, out float river))
            return river;

        short owner = water.DetailSlot[cell] >= 0 ? water.OwnerAt(x, z) : WaterMap.OWNER_NONE;

        return owner >= 0 ? water.LevelOf(owner) : water.ShoreSurface[cell];
    }

    private static float NodeGround(WaterMap water, int cell, float x, float z)
    {
        int start = water.DetailStart(cell);

        if (start < 0)
            return float.NegativeInfinity;

        float step = water.NodeStep;
        float u = Mathf.Clamp((x - cell % water.Resolution * water.CellSize) / step, 0f, WaterMap.SUBDIVISION - 1e-4f);
        float v = Mathf.Clamp((z - cell / water.Resolution * water.CellSize) / step, 0f, WaterMap.SUBDIVISION - 1e-4f);
        int fx = (int)u, fz = (int)v;
        int node = start + fz * WaterMap.CELL_SIDE_NODES + fx;
        int above = node + WaterMap.CELL_SIDE_NODES;
        float low = Mathf.Lerp(water.DetailGround(node), water.DetailGround(node + 1), u - fx);
        float high = Mathf.Lerp(water.DetailGround(above), water.DetailGround(above + 1), u - fx);

        return Mathf.Lerp(low, high, v - fz);
    }
}

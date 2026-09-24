using System;
using System.Collections.Generic;
using UnityEngine;

public static class WaterStampTracer
{
    public const int GRID = 256;
    public const float CORE_LEVEL = 0.9f;
    public const float EDGE_LEVEL = 0.003f;
    public const float LAKE_LEVEL = 0.5f;
    public const float STEP = 6f;

    private const float OFF_CORE_COST = 400f;
    private const float CENTER_PULL = 25f;
    private const int SNAP_RADIUS = 16;
    private const int SMOOTH_PASSES = 3;
    private const float SHOULDER_CELLS = 2.5f;
    private const float CORRIDOR_PERCENTILE = 0.9f;

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private sealed class Field
    {
        public float[] Mask;
        public float[] Distance;
        public float CellX;
        public float CellZ;
        public float MaxDistance;
        public Vector2 Size;

        public Vector2 Center(int cell)
        {
            return new Vector2((cell % GRID + 0.5f) * CellX, (cell / GRID + 0.5f) * CellZ);
        }

        public int CellAt(Vector2 stamp)
        {
            int i = Mathf.Clamp((int)(stamp.x / CellX), 0, GRID - 1);
            int j = Mathf.Clamp((int)(stamp.y / CellZ), 0, GRID - 1);

            return j * GRID + i;
        }

        public float MaskAt(Vector2 stamp)
        {
            return Bilinear(Mask, stamp);
        }

        public float DistanceAt(Vector2 stamp)
        {
            return Bilinear(Distance, stamp);
        }

        public bool Inside(Vector2 stamp)
        {
            return stamp.x >= 0f && stamp.y >= 0f && stamp.x <= Size.x && stamp.y <= Size.y;
        }

        private float Bilinear(float[] values, Vector2 stamp)
        {
            float u = Mathf.Clamp(stamp.x / CellX - 0.5f, 0f, GRID - 1.001f);
            float v = Mathf.Clamp(stamp.y / CellZ - 0.5f, 0f, GRID - 1.001f);
            int i = (int)u, j = (int)v;
            float tx = u - i, tz = v - j;
            int origin = j * GRID + i;

            float top = values[origin] + (values[origin + 1] - values[origin]) * tx;
            float bottom = values[origin + GRID] + (values[origin + GRID + 1] - values[origin + GRID]) * tx;

            return top + (bottom - top) * tz;
        }
    }

    public static void Trace(WaterStampDefinition definition, WaterStampShape shape)
    {
        if (definition.IsRiver)
            TraceRiver(definition, shape);
        else
            MeasureLake(definition, shape);
    }

    public static void TraceRiver(WaterStampDefinition definition, WaterStampShape shape)
    {
        Field field = Build(shape, definition.RecommendedSize);
        Vector2 entry = definition.ToStamp(definition.Entry);
        Vector2 exit = definition.ToStamp(definition.Exit);

        int start = Snap(field, entry, definition.Name, "entry");
        int goal = Snap(field, exit, definition.Name, "exit");

        List<int> cells = Path(field, start, new HashSet<int> { goal }, out _)
            ?? throw new InvalidOperationException($"Water stamp {definition.Name}: no channel connects entry and exit in the mask");

        Vector2[] centerline = Finish(field, cells, entry, exit);
        var halfWidths = new float[centerline.Length];
        var shoulders = new List<float>();
        var reaches = new List<float>();

        for (int i = 0; i < centerline.Length; i++)
        {
            Vector2 normal = Normal(centerline, i);
            halfWidths[i] = Mathf.Max(Mathf.Min(field.CellX, field.CellZ), field.DistanceAt(centerline[i]));

            foreach (float side in new[] { 1f, -1f })
            {
                reaches.Add(Reach(field, centerline[i], normal * side));

                Vector2 probe = centerline[i] + normal * (side * (halfWidths[i] + SHOULDER_CELLS * Mathf.Max(field.CellX, field.CellZ)));

                if (field.Inside(probe))
                    shoulders.Add(field.MaskAt(probe));
            }
        }

        shoulders.Sort();
        reaches.Sort();
        float corridor = reaches.Count == 0 ? 0f : reaches[(int)(reaches.Count * CORRIDOR_PERCENTILE)];
        float shoulder = shoulders.Count == 0 ? 0.25f : Mathf.Clamp(shoulders[shoulders.Count / 2], 0.05f, 0.9f);

        definition.SetRiverTrace(centerline, halfWidths, Mathf.Max(corridor, Max(halfWidths)), shoulder);

        foreach (WaterStampBranch branch in definition.Branches)
            TraceBranch(definition, field, branch);
    }

    private static void TraceBranch(WaterStampDefinition definition, Field field, WaterStampBranch branch)
    {
        Vector2[] main = definition.Centerline;
        var targets = new HashSet<int>();
        int margin = Mathf.Max(1, main.Length / 12);

        for (int i = margin; i < main.Length - margin; i++)
            targets.Add(field.CellAt(main[i]));

        Vector2 socket = definition.ToStamp(branch.Position);
        int start = Snap(field, socket, definition.Name, branch.Kind.ToString());

        List<int> cells = Path(field, start, targets, out int reached)
            ?? throw new InvalidOperationException($"Water stamp {definition.Name}: branch {branch.Kind} does not reach the main channel in the mask");

        Vector2 target = field.Center(reached);
        int junction = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < main.Length; i++)
        {
            float distance = (main[i] - target).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            junction = i;
        }

        Vector2[] path = Finish(field, cells, socket, main[junction]);

        if (branch.Kind == WaterStampBranchKind.BranchOut)
            Array.Reverse(path);

        branch.SetPath(path, junction);
    }

    public static void MeasureLake(WaterStampDefinition definition, WaterStampShape shape)
    {
        Field field = Build(shape, definition.RecommendedSize);
        double count = 0, sumX = 0, sumZ = 0;

        for (int cell = 0; cell < field.Mask.Length; cell++)
        {
            if (field.Mask[cell] < LAKE_LEVEL)
                continue;

            Vector2 center = field.Center(cell);
            count++;
            sumX += center.x;
            sumZ += center.y;
        }

        if (count < 4)
            throw new InvalidOperationException($"Water stamp {definition.Name}: the mask holds no water above {LAKE_LEVEL}");

        var centroid = new Vector2((float)(sumX / count), (float)(sumZ / count));
        double xx = 0, zz = 0, xz = 0;

        for (int cell = 0; cell < field.Mask.Length; cell++)
        {
            if (field.Mask[cell] < LAKE_LEVEL)
                continue;

            Vector2 offset = field.Center(cell) - centroid;
            xx += offset.x * offset.x;
            zz += offset.y * offset.y;
            xz += offset.x * offset.y;
        }

        xx /= count;
        zz /= count;
        xz /= count;

        double half = 0.5 * (xx + zz);
        double root = Math.Sqrt(0.25 * (xx - zz) * (xx - zz) + xz * xz);
        float angle = (float)(0.5 * Math.Atan2(2.0 * xz, xx - zz)) * Mathf.Rad2Deg;

        definition.SetLakeTrace((float)(count * field.CellX * field.CellZ), centroid, angle, (float)Math.Sqrt(half + root), (float)Math.Sqrt(Math.Max(1e-6, half - root)));
    }

    private static Field Build(WaterStampShape shape, Vector2 size)
    {
        var field = new Field
        {
            Mask = shape.Downsample(GRID),
            Distance = new float[GRID * GRID],
            CellX = size.x / GRID,
            CellZ = size.y / GRID,
            Size = size
        };

        float diagonal = Mathf.Sqrt(field.CellX * field.CellX + field.CellZ * field.CellZ);

        for (int cell = 0; cell < field.Distance.Length; cell++)
            field.Distance[cell] = field.Mask[cell] >= CORE_LEVEL ? float.MaxValue : 0f;

        for (int j = 0; j < GRID; j++)
        {
            for (int i = 0; i < GRID; i++)
            {
                Relax(field.Distance, i, j, i - 1, j, field.CellX);
                Relax(field.Distance, i, j, i, j - 1, field.CellZ);
                Relax(field.Distance, i, j, i - 1, j - 1, diagonal);
                Relax(field.Distance, i, j, i + 1, j - 1, diagonal);
            }
        }

        for (int j = GRID - 1; j >= 0; j--)
        {
            for (int i = GRID - 1; i >= 0; i--)
            {
                Relax(field.Distance, i, j, i + 1, j, field.CellX);
                Relax(field.Distance, i, j, i, j + 1, field.CellZ);
                Relax(field.Distance, i, j, i + 1, j + 1, diagonal);
                Relax(field.Distance, i, j, i - 1, j + 1, diagonal);
            }
        }

        float reach = Mathf.Max(size.x, size.y);

        for (int cell = 0; cell < field.Distance.Length; cell++)
        {
            if (field.Distance[cell] == float.MaxValue)
                field.Distance[cell] = reach;

            field.MaxDistance = Mathf.Max(field.MaxDistance, field.Distance[cell]);
        }

        return field;
    }

    private static void Relax(float[] distance, int i, int j, int ni, int nj, float step)
    {
        if (ni < 0 || nj < 0 || ni >= GRID || nj >= GRID)
            return;

        float candidate = distance[nj * GRID + ni];

        if (candidate == float.MaxValue)
            return;

        candidate += step;

        if (candidate < distance[j * GRID + i])
            distance[j * GRID + i] = candidate;
    }

    private static int Snap(Field field, Vector2 socket, string name, string label)
    {
        int center = field.CellAt(socket);
        int ci = center % GRID, cj = center / GRID;
        int best = -1;
        float bestScore = float.MinValue;

        for (int dj = -SNAP_RADIUS; dj <= SNAP_RADIUS; dj++)
        {
            for (int di = -SNAP_RADIUS; di <= SNAP_RADIUS; di++)
            {
                int i = ci + di, j = cj + dj;

                if (i < 0 || j < 0 || i >= GRID || j >= GRID)
                    continue;

                int cell = j * GRID + i;

                if (field.Mask[cell] < CORE_LEVEL)
                    continue;

                float score = field.Distance[cell] - 0.5f * Vector2.Distance(field.Center(cell), socket);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = cell;
            }
        }

        if (best < 0)
            throw new InvalidOperationException($"Water stamp {name}: the {label} socket at {socket} has no channel core within {SNAP_RADIUS} cells");

        return best;
    }

    private static List<int> Path(Field field, int start, HashSet<int> goals, out int reached)
    {
        int count = GRID * GRID;
        var cost = new float[count];
        var from = new int[count];
        var closed = new bool[count];
        var heap = new MinHeap(count / 4);

        for (int i = 0; i < count; i++)
        {
            cost[i] = float.MaxValue;
            from[i] = -1;
        }

        cost[start] = 0f;
        heap.Push(start, 0f);
        reached = -1;

        while (heap.TryPop(out int current))
        {
            if (closed[current])
                continue;

            closed[current] = true;

            if (goals.Contains(current))
            {
                reached = current;
                break;
            }

            int ci = current % GRID, cj = current / GRID;

            for (int direction = 0; direction < 8; direction++)
            {
                int i = ci + OffsetX[direction], j = cj + OffsetZ[direction];

                if (i < 0 || j < 0 || i >= GRID || j >= GRID)
                    continue;

                int next = j * GRID + i;

                if (closed[next])
                    continue;

                float length = Mathf.Sqrt(OffsetX[direction] * OffsetX[direction] * field.CellX * field.CellX + OffsetZ[direction] * OffsetZ[direction] * field.CellZ * field.CellZ);
                float candidate = cost[current] + length * 0.5f * (Density(field, current) + Density(field, next));

                if (candidate >= cost[next])
                    continue;

                cost[next] = candidate;
                from[next] = current;
                heap.Push(next, candidate);
            }
        }

        if (reached < 0)
            return null;

        var path = new List<int>();

        for (int cell = reached; cell >= 0; cell = from[cell])
            path.Add(cell);

        path.Reverse();
        return path;
    }

    private static float Density(Field field, int cell)
    {
        if (field.Mask[cell] < CORE_LEVEL)
            return OFF_CORE_COST;

        float off = 1f - Mathf.Clamp01(field.Distance[cell] / Mathf.Max(1e-3f, field.MaxDistance));

        return 1f + CENTER_PULL * off * off;
    }

    private static Vector2[] Finish(Field field, List<int> cells, Vector2 first, Vector2 last)
    {
        var points = new List<Vector2>(cells.Count + 2) { first };

        foreach (int cell in cells)
            points.Add(field.Center(cell));

        points.Add(last);

        float median = MedianDistance(field, cells);
        int radius = Mathf.Clamp(Mathf.RoundToInt(0.8f * median / Mathf.Max(field.CellX, field.CellZ)), 2, 12);
        var smoothed = new Vector2[points.Count];

        for (int pass = 0; pass < SMOOTH_PASSES; pass++)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (i == 0 || i == points.Count - 1)
                {
                    smoothed[i] = points[i];
                    continue;
                }

                int reach = Mathf.Min(radius, Mathf.Min(i, points.Count - 1 - i));
                Vector2 sum = Vector2.zero;

                for (int k = -reach; k <= reach; k++)
                    sum += points[i + k];

                smoothed[i] = sum / (2 * reach + 1);
            }

            for (int i = 0; i < points.Count; i++)
                points[i] = smoothed[i];
        }

        return Resample(points, STEP);
    }

    private static float MedianDistance(Field field, List<int> cells)
    {
        var values = new List<float>(cells.Count);

        foreach (int cell in cells)
            values.Add(field.Distance[cell]);

        values.Sort();

        return values.Count == 0 ? field.CellX : values[values.Count / 2];
    }

    public static Vector2[] Resample(List<Vector2> points, float step)
    {
        var result = new List<Vector2> { points[0] };
        float carry = 0f;

        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float length = Vector2.Distance(a, b);

            if (length < 1e-5f)
                continue;

            float along = step - carry;

            while (along < length)
            {
                result.Add(Vector2.Lerp(a, b, along / length));
                along += step;
            }

            carry = length - (along - step);
        }

        if (Vector2.Distance(result[^1], points[^1]) > 0.25f * step)
            result.Add(points[^1]);
        else
            result[^1] = points[^1];

        return result.ToArray();
    }

    private static float Reach(Field field, Vector2 from, Vector2 direction)
    {
        float step = Mathf.Min(field.CellX, field.CellZ);
        float limit = Mathf.Max(field.Size.x, field.Size.y) * 0.5f;

        float lowest = 1f;

        for (float distance = step; distance < limit; distance += step)
        {
            Vector2 probe = from + direction * distance;

            if (!field.Inside(probe))
                return distance;

            float mask = field.MaskAt(probe);

            if (mask <= EDGE_LEVEL || lowest < 0.2f && mask > lowest + 0.2f)
                return distance;

            lowest = Mathf.Min(lowest, mask);
        }

        return limit;
    }

    public static Vector2 Normal(Vector2[] line, int i)
    {
        Vector2 tangent = line[Mathf.Min(line.Length - 1, i + 1)] - line[Mathf.Max(0, i - 1)];

        if (tangent.sqrMagnitude < 1e-10f)
            return new Vector2(0f, 1f);

        tangent = tangent.normalized;
        return new Vector2(-tangent.y, tangent.x);
    }

    private static float Max(float[] values)
    {
        float max = 0f;

        foreach (float value in values)
            max = Mathf.Max(max, value);

        return max;
    }
}

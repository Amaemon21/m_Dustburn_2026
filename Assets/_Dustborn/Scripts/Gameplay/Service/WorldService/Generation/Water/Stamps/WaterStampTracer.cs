using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class WaterStampTracer
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

    public static Vector2 Normal(Vector2[] line, int i)
    {
        Vector2 tangent = line[Mathf.Min(line.Length - 1, i + 1)] - line[Mathf.Max(0, i - 1)];

        if (tangent.sqrMagnitude < 1e-10f)
            return new Vector2(0f, 1f);

        tangent = tangent.normalized;
        return new Vector2(-tangent.y, tangent.x);
    }
}

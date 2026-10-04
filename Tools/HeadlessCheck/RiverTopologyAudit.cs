using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

static class RiverTopologyAudit
{
    private const float INDEX_CELL = 64f;
    private const float JOIN_PAD = 10f;
    private const float SIDE_GAP = 30f;
    private const float SIDE_CONFLUENCE_WIDTHS = 2f;
    private const float PARALLEL_COS = 0.866f;
    private const float UPSTREAM_ANGLE = 100f;
    private const float WIDTH_TOLERANCE = 0.5f;
    private const float WIDTH_PROBE_WIDTHS = 2f;
    private const float WIDTH_RAMP = 0.15f;
    private const float APPROACH_MIN = 20f;
    private const float CLUSTER = 60f;
    private const float SIDE_RUN_REPORT = 100f;
    private const int EXAMPLES = 8;
    private const float STUB_LENGTH = 150f;
    private const float STUB_WIDTHS = 8f;

    public sealed class Result
    {
        public int Rivers, Crossings, SideOutflows, Confluences, UpstreamJoins, NarrowingJoins, SideRuns, OppositeJoins, Stubs;
        public float SideLength, WorstAngle;
        public readonly List<string> CrossingExamples = new();
        public readonly List<string> OutflowExamples = new();
        public readonly List<string> SideExamples = new();
        public readonly List<string> UpstreamExamples = new();
        public readonly List<string> NarrowExamples = new();
        public readonly List<string> OppositeExamples = new();
        public readonly List<string> StubExamples = new();

        public int Violations => Crossings + SideOutflows + UpstreamJoins + NarrowingJoins + OppositeJoins;

        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine($"  сеть рек: {Rivers} рек, пересечений река-река {Crossings}, истоков из борта другой реки {SideOutflows}" + Examples(CrossingExamples.Concat(OutflowExamples)));
            text.AppendLine($"  рядом и параллельно: {SideLength / 1000f:0.00} км русла в {SideRuns} отрезках длиннее {SIDE_RUN_REPORT:0} м" + Examples(SideExamples));
            text.AppendLine($"  слияния: {Confluences}, против течения (угол больше {UPSTREAM_ANGLE:0}°) {UpstreamJoins}, наибольший угол {WorstAngle:0}°, ниже слияния уже притока {NarrowingJoins}" + Examples(UpstreamExamples.Concat(NarrowExamples)));
            text.Append($"  слияний крестом (два притока с разных берегов в одно место) {OppositeJoins}, притоков-обрубков короче {STUB_LENGTH:0} м {Stubs}" + Examples(OppositeExamples.Concat(StubExamples)));

            return text.ToString();
        }

        private static string Examples(IEnumerable<string> examples)
        {
            List<string> list = examples.Take(EXAMPLES).ToList();

            return list.Count == 0 ? "" : $"; {string.Join(" ", list)}";
        }
    }

    private sealed class Line
    {
        public Vector2[] Points;
        public float[] Widths;
        public float[] Along;
        public bool[] Submerged;
        public bool[] Standing;
        public float Length;
    }

    public static Result Measure(WaterMap water)
    {
        var result = new Result();
        List<Line> lines = Lines(water);
        result.Rivers = lines.Count(line => line.Points.Length >= 2);
        Dictionary<long, List<(int River, int Segment)>> index = Index(lines);

        MeasureCrossings(lines, index, result);
        MeasureSideRuns(lines, index, result);
        MeasureConfluences(lines, index, result);

        return result;
    }

    private static List<Line> Lines(WaterMap water)
    {
        var lines = new List<Line>(water.Rivers.Count);

        foreach (RiverPath river in water.Rivers)
        {
            int count = river.Points.Count;
            var line = new Line { Points = new Vector2[count], Widths = new float[count], Along = new float[count], Submerged = new bool[count], Standing = new bool[count] };

            for (int i = 0; i < count; i++)
            {
                RiverPoint point = river.Points[i];
                line.Points[i] = point.Position;
                line.Widths[i] = point.Width;
                line.Submerged[i] = point.Submerged;
                line.Standing[i] = point.Submerged && Standing(water, point.Position);

                if (i > 0)
                    line.Along[i] = line.Along[i - 1] + Vector2.Distance(line.Points[i - 1], point.Position);
            }

            line.Length = count == 0 ? 0f : line.Along[count - 1];
            lines.Add(line);
        }

        return lines;
    }

    private static bool Standing(WaterMap water, Vector2 point)
    {
        if (point.x < 0f || point.y < 0f || point.x > water.WorldSize || point.y > water.WorldSize)
            return true;

        int cell = water.CellIndex(point.x, point.y);

        if (water.BodyIds[cell] >= 0)
            return true;

        var kind = (WaterKind)water.Kinds[cell];

        return kind == WaterKind.Sea || kind == WaterKind.Lake || kind == WaterKind.Pond || water.StandingAt(point.x, point.y, out _);
    }

    private static Dictionary<long, List<(int River, int Segment)>> Index(List<Line> lines)
    {
        var index = new Dictionary<long, List<(int, int)>>();

        for (int river = 0; river < lines.Count; river++)
        {
            Line line = lines[river];

            for (int i = 0; i + 1 < line.Points.Length; i++)
            {
                Vector2 a = line.Points[i], b = line.Points[i + 1];
                int minX = Cell(Mathf.Min(a.x, b.x)), maxX = Cell(Mathf.Max(a.x, b.x));
                int minZ = Cell(Mathf.Min(a.y, b.y)), maxZ = Cell(Mathf.Max(a.y, b.y));

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        long key = Key(x, z);

                        if (!index.TryGetValue(key, out List<(int, int)> list))
                            index[key] = list = new List<(int, int)>();

                        list.Add((river, i));
                    }
                }
            }
        }

        return index;
    }

    private static int Cell(float value) => Mathf.FloorToInt(value / INDEX_CELL);

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static IEnumerable<(int River, int Segment)> Near(Dictionary<long, List<(int River, int Segment)>> index, Vector2 point, float reach)
    {
        var seen = new HashSet<(int, int)>();
        int minX = Cell(point.x - reach), maxX = Cell(point.x + reach);
        int minZ = Cell(point.y - reach), maxZ = Cell(point.y + reach);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!index.TryGetValue(Key(x, z), out List<(int River, int Segment)> list))
                    continue;

                foreach ((int River, int Segment) entry in list)
                {
                    if (seen.Add(entry))
                        yield return entry;
                }
            }
        }
    }

    private static void MeasureCrossings(List<Line> lines, Dictionary<long, List<(int River, int Segment)>> index, Result result)
    {
        var counted = new List<(int A, int B, Vector2 At)>();

        foreach (List<(int River, int Segment)> cell in index.Values)
        {
            for (int m = 0; m < cell.Count; m++)
            {
                for (int n = m + 1; n < cell.Count; n++)
                {
                    (int ra, int sa) = cell[m];
                    (int rb, int sb) = cell[n];

                    if (ra == rb)
                        continue;

                    if (ra > rb)
                        (ra, sa, rb, sb) = (rb, sb, ra, sa);

                    Line a = lines[ra], b = lines[rb];

                    if (a.Standing[sa] && a.Standing[sa + 1] || b.Standing[sb] && b.Standing[sb + 1])
                        continue;

                    if (!Intersect(a.Points[sa], a.Points[sa + 1], b.Points[sb], b.Points[sb + 1], out float t, out float u))
                        continue;

                    Vector2 at = Vector2.Lerp(a.Points[sa], a.Points[sa + 1], t);

                    if (counted.Exists(entry => entry.A == ra && entry.B == rb && (entry.At - at).sqrMagnitude < CLUSTER * CLUSTER))
                        continue;

                    counted.Add((ra, rb, at));
                    Classify(lines, ra, sa, t, rb, sb, u, at, result);
                }
            }
        }
    }

    private static void Classify(List<Line> lines, int ra, int sa, float t, int rb, int sb, float u, Vector2 at, Result result)
    {
        Line a = lines[ra], b = lines[rb];
        float widthA = Mathf.Lerp(a.Widths[sa], a.Widths[sa + 1], t);
        float widthB = Mathf.Lerp(b.Widths[sb], b.Widths[sb + 1], u);
        float alongA = Mathf.Lerp(a.Along[sa], a.Along[sa + 1], t);
        float alongB = Mathf.Lerp(b.Along[sb], b.Along[sb + 1], u);
        float allowA = 0.5f * widthB + widthA + JOIN_PAD;
        float allowB = 0.5f * widthA + widthB + JOIN_PAD;

        if (a.Length - alongA <= allowA || b.Length - alongB <= allowB)
            return;

        if (alongA <= allowA || alongB <= allowB)
        {
            result.SideOutflows++;

            if (result.OutflowExamples.Count < EXAMPLES)
                result.OutflowExamples.Add($"исток {(alongA <= allowA ? ra : rb)} из {(alongA <= allowA ? rb : ra)} {Format(at)}");

            return;
        }

        result.Crossings++;

        if (result.CrossingExamples.Count < EXAMPLES)
            result.CrossingExamples.Add($"X {ra}x{rb} {Format(at)} ({widthA:0}/{widthB:0} м)");
    }

    private static bool Intersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float t, out float u)
    {
        t = u = 0f;
        Vector2 r = p2 - p, s = q2 - q;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-9f)
            return false;

        Vector2 offset = q - p;
        t = (offset.x * s.y - offset.y * s.x) / denominator;
        u = (offset.x * r.y - offset.y * r.x) / denominator;

        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    private static bool Closest(Line line, int segment, Vector2 point, out Vector2 closest, out float t)
    {
        Vector2 a = line.Points[segment], axis = line.Points[segment + 1] - a;
        float length = axis.sqrMagnitude;
        t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, axis) / length);
        closest = a + axis * t;

        return length >= 1e-8f;
    }

    private static void MeasureSideRuns(List<Line> lines, Dictionary<long, List<(int River, int Segment)>> index, Result result)
    {
        float widest = 0f;

        foreach (Line line in lines)
            foreach (float width in line.Widths)
                widest = Mathf.Max(widest, width);

        float search = widest + SIDE_GAP;
        var runs = new List<(float Length, Vector2 At, int A, int B)>();

        for (int ra = 0; ra < lines.Count; ra++)
        {
            Line a = lines[ra];
            float run = 0f;
            int runWith = -1;
            Vector2 runAt = default;

            for (int k = 1; k + 1 < a.Points.Length; k++)
            {
                int beside = a.Submerged[k] ? -1 : Beside(lines, index, ra, k, search);
                float step = 0.5f * (a.Along[k + 1] - a.Along[k - 1]);

                if (beside >= 0)
                {
                    result.SideLength += step;

                    if (beside != runWith)
                    {
                        Flush(runs, run, runAt, ra, runWith);
                        run = 0f;
                        runWith = beside;
                        runAt = a.Points[k];
                    }

                    run += step;
                    continue;
                }

                Flush(runs, run, runAt, ra, runWith);
                run = 0f;
                runWith = -1;
            }

            Flush(runs, run, runAt, ra, runWith);
        }

        result.SideRuns = runs.Count;

        foreach ((float length, Vector2 at, int ra, int rb) in runs.OrderByDescending(entry => entry.Length).Take(EXAMPLES))
            result.SideExamples.Add($"{ra}||{rb} {length:0} м от {Format(at)}");
    }

    private static void Flush(List<(float Length, Vector2 At, int A, int B)> runs, float run, Vector2 at, int ra, int rb)
    {
        if (rb >= 0 && run >= SIDE_RUN_REPORT)
            runs.Add((run, at, ra, rb));
    }

    private static int Beside(List<Line> lines, Dictionary<long, List<(int River, int Segment)>> index, int ra, int k, float search)
    {
        Line a = lines[ra];
        Vector2 point = a.Points[k];
        Vector2 tangent = (a.Points[k + 1] - a.Points[k - 1]).normalized;
        float widthA = a.Widths[k];

        foreach ((int rb, int sb) in Near(index, point, search))
        {
            if (rb == ra)
                continue;

            Line b = lines[rb];

            if (b.Standing[sb] && b.Standing[sb + 1] || !Closest(b, sb, point, out Vector2 closest, out float u))
                continue;

            float widthB = Mathf.Lerp(b.Widths[sb], b.Widths[sb + 1], u);
            float reach = 0.5f * (widthA + widthB) + SIDE_GAP;

            if ((closest - point).sqrMagnitude > reach * reach)
                continue;

            Vector2 direction = (b.Points[sb + 1] - b.Points[sb]).normalized;

            if (Mathf.Abs(Vector2.Dot(direction, tangent)) < PARALLEL_COS)
                continue;

            float confluence = SIDE_CONFLUENCE_WIDTHS * (widthA + widthB) + SIDE_GAP;
            float alongB = Mathf.Lerp(b.Along[sb], b.Along[sb + 1], u);

            if (a.Length - a.Along[k] <= confluence || a.Along[k] <= confluence || b.Length - alongB <= confluence || alongB <= confluence)
                continue;

            return rb;
        }

        return -1;
    }

    private static void MeasureConfluences(List<Line> lines, Dictionary<long, List<(int River, int Segment)>> index, Result result)
    {
        var junctions = new List<(int Trunk, float Along, int Side, int Tributary, float Width, Vector2 At)>();

        for (int ra = 0; ra < lines.Count; ra++)
        {
            Line a = lines[ra];
            int last = a.Points.Length - 1;

            if (last < 1 || a.Standing[last])
                continue;

            if (!Target(lines, index, ra, out int rb, out int sb, out float u))
                continue;

            result.Confluences++;
            Line b = lines[rb];
            float alongB = Mathf.Lerp(b.Along[sb], b.Along[sb + 1], u);

            if (a.Length < Mathf.Max(STUB_LENGTH, STUB_WIDTHS * a.Widths[last]))
            {
                result.Stubs++;

                if (result.StubExamples.Count < EXAMPLES)
                    result.StubExamples.Add($"{ra}->{rb} {a.Length:0} м {Format(a.Points[last])}");
            }

            int entry = Entry(a, b, last);
            float approach = Mathf.Max(APPROACH_MIN, 2f * a.Widths[entry]);
            int from = entry;

            while (from > 0 && a.Along[entry] - a.Along[from] < approach)
                from--;

            if (from == entry)
                continue;

            Vector2 tributary = (a.Points[entry] - a.Points[from]).normalized;
            Vector2 trunk = (b.Points[sb + 1] - b.Points[sb]).normalized;
            Vector2 offset = a.Points[from] - Vector2.Lerp(b.Points[sb], b.Points[sb + 1], u);
            junctions.Add((rb, alongB, trunk.x * offset.y - trunk.y * offset.x >= 0f ? 1 : -1, ra, a.Widths[entry], a.Points[last]));

            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(tributary, trunk), -1f, 1f)) * Mathf.Rad2Deg;
            result.WorstAngle = Mathf.Max(result.WorstAngle, angle);

            if (angle > UPSTREAM_ANGLE)
            {
                result.UpstreamJoins++;

                if (result.UpstreamExamples.Count < EXAMPLES)
                    result.UpstreamExamples.Add($"{ra}->{rb} {angle:0}° {Format(a.Points[last])}");
            }

            float trunkWidth = Mathf.Lerp(b.Widths[sb], b.Widths[sb + 1], u);
            float probe = WIDTH_PROBE_WIDTHS * trunkWidth;
            int up = At(b, alongB - probe);
            float inputs = Mathf.Max(a.Widths[entry], up < 0 ? 0f : b.Widths[up]);
            int down = At(b, alongB + probe + Mathf.Max(0f, inputs - trunkWidth) / WIDTH_RAMP);

            if (down < 0 || b.Standing[down] || b.Submerged[down])
                continue;

            if (b.Widths[down] >= inputs - WIDTH_TOLERANCE)
                continue;

            result.NarrowingJoins++;

            if (result.NarrowExamples.Count < EXAMPLES)
                result.NarrowExamples.Add($"{ra}->{rb} ниже {b.Widths[down]:0} м при притоке {a.Widths[entry]:0} м {Format(a.Points[last])}");
        }

        MeasureOpposite(lines, junctions, result);
    }

    private static void MeasureOpposite(List<Line> lines, List<(int Trunk, float Along, int Side, int Tributary, float Width, Vector2 At)> junctions, Result result)
    {
        for (int i = 0; i < junctions.Count; i++)
        {
            for (int j = i + 1; j < junctions.Count; j++)
            {
                (int trunkIndex, float along, int side, int tributary, float width, Vector2 at) = junctions[i];
                (int otherTrunk, float otherAlong, int otherSide, int otherTributary, float otherWidth, _) = junctions[j];

                if (trunkIndex != otherTrunk || side == otherSide)
                    continue;

                Line trunk = lines[trunkIndex];
                int near = At(trunk, along);
                float gap = 0.5f * (near < 0 ? 0f : trunk.Widths[near]) + width + otherWidth;

                if (Mathf.Abs(along - otherAlong) > gap)
                    continue;

                result.OppositeJoins++;

                if (result.OppositeExamples.Count < EXAMPLES)
                    result.OppositeExamples.Add($"{tributary}+{otherTributary}->{trunkIndex} {Format(at)}");
            }
        }
    }

    private static bool Target(List<Line> lines, Dictionary<long, List<(int River, int Segment)>> index, int ra, out int river, out int segment, out float along)
    {
        Line a = lines[ra];
        Vector2 end = a.Points[^1];
        float widthA = a.Widths[^1];
        float best = float.MaxValue;
        river = segment = -1;
        along = 0f;

        foreach ((int rb, int sb) in Near(index, end, widthA + SIDE_GAP * 2f))
        {
            if (rb == ra)
                continue;

            Line b = lines[rb];

            if (b.Standing[sb] && b.Standing[sb + 1] || !Closest(b, sb, end, out Vector2 closest, out float u))
                continue;

            float reach = 0.5f * (Mathf.Lerp(b.Widths[sb], b.Widths[sb + 1], u) + widthA) + JOIN_PAD;
            float distance = (closest - end).sqrMagnitude;
            float rank = distance + (b.Submerged[sb] && b.Submerged[sb + 1] ? reach * reach : 0f);

            if (distance > reach * reach || rank >= best)
                continue;

            best = rank;
            river = rb;
            segment = sb;
            along = u;
        }

        return river >= 0;
    }

    private static int Entry(Line a, Line b, int last)
    {
        int entry = last;

        while (entry > 0 && Inside(b, a.Points[entry]))
            entry--;

        return entry;
    }

    private static bool Inside(Line b, Vector2 point)
    {
        for (int i = 0; i + 1 < b.Points.Length; i++)
        {
            if (!Closest(b, i, point, out Vector2 closest, out float u))
                continue;

            float half = 0.5f * Mathf.Lerp(b.Widths[i], b.Widths[i + 1], u);

            if ((closest - point).sqrMagnitude <= half * half)
                return true;
        }

        return false;
    }

    private static int At(Line line, float along)
    {
        if (along < 0f || along > line.Length)
            return -1;

        int index = Array.BinarySearch(line.Along, along);

        return index >= 0 ? index : Mathf.Min(line.Along.Length - 1, ~index);
    }

    private static string Format(Vector2 point) => $"({point.x:0}, {point.y:0})";
}

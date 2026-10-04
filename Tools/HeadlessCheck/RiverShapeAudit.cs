using System;
using System.Collections.Generic;
using UnityEngine;

static class RiverShapeAudit
{
    private const float MARCH_STEP = 0.1f;
    private const float MARCH_SLACK = 3f;
    private const float STRAIGHT_RUN = 60f;
    private const float STRAIGHT_TOLERANCE = 0.2f;
    private const float PERCHED_BANK = 2f;
    private const float PERCHED_FREEBOARD = 0.3f;
    private const float POOL_GRADE = 0.01f;
    private const float PLANE_GRADE = 0.03f;
    private const float PLANE_RUN = 40f;
    private const float ABRUPT_CAP = 4f;
    private const float CAP_REACH = 4f;
    private const int EXAMPLES = 6;
    private static readonly float[] BANDS = { 3f, 5f, 10f, 15f, 25f };

    public sealed class Result
    {
        public int Springs;
        public readonly List<float> SourceWidths = new();
        public readonly List<float> SourceVisible = new();
        public readonly List<float> WidthAt100 = new();
        public readonly List<float> WidthAt300 = new();
        public float SteepestSourceRamp;
        public int OpenPoints, Perched, Waterlines;
        public float EdgeLength, StraightLength, LongestStraight;
        public int StraightRuns;
        public float OpenLength, PlaneLength, LongestPlane, LongestPlaneGrade;
        public int PlaneRuns;
        public int Caps, AbruptCaps;
        public readonly List<float> CapWidths = new();
        public readonly float[] BandLength = new float[BANDS.Length + 1];
        public readonly Dictionary<string, List<string>> Examples = new();

        public float PerchedShare => Waterlines == 0 ? 0f : Perched / (float)Waterlines;
        public float StraightShare => EdgeLength <= 0f ? 0f : StraightLength / EdgeLength;
        public float PlaneShare => OpenLength <= 0f ? 0f : PlaneLength / OpenLength;

        public void Example(string kind, Vector2 at, string note = "")
        {
            if (!Examples.TryGetValue(kind, out List<string> list))
                Examples[kind] = list = new List<string>();

            if (list.Count < EXAMPLES)
                list.Add($"({at.x:0}, {at.y:0}){note}");
        }

        public string Describe()
        {
            string bands = "";
            float total = 0f;

            foreach (float length in BandLength)
                total += length;

            for (int b = 0; b <= BANDS.Length; b++)
            {
                string label = b == 0 ? $"<{BANDS[0]:0}" : b == BANDS.Length ? $"≥{BANDS[^1]:0}" : $"{BANDS[b - 1]:0}–{BANDS[b]:0}";
                bands += $"{label} м {(total <= 0f ? 0f : BandLength[b] / total):P0}{(b < BANDS.Length ? ", " : "")}";
            }

            string text = "форма рек:\n"
                + $"  истоки-родники {Springs}: ширина у истока {Stats(SourceWidths)}, видимая вода у истока {Stats(SourceVisible)}\n"
                + $"  ширина в 100 м ниже истока {Stats(WidthAt100)}, в 300 м {Stats(WidthAt300)}, самый резкий рост ширины у истока {SteepestSourceRamp:0.00} м/м\n"
                + $"  длина открытых рек по ширине: {bands}\n"
                + $"  вода не в русле (нижний берег ниже {PERCHED_FREEBOARD:0.0} м над водой в {PERCHED_BANK:0} м от уреза): {Perched} из {Waterlines} сечений ({PerchedShare:P1})\n"
                + $"  прямые как по линейке урезы (в {STRAIGHT_TOLERANCE:0.0} м от прямой дольше {STRAIGHT_RUN:0} м): {StraightRuns} участков, {StraightLength / 1000f:0.00} км из {EdgeLength / 1000f:0.00} км уреза ({StraightShare:P1}), самый длинный {LongestStraight:0} м\n"
                + $"  наклонная плоскость (уклон больше {PLANE_GRADE:P0} без плёсов дольше {PLANE_RUN:0} м): {PlaneRuns} участков, {PlaneLength / 1000f:0.00} км из {OpenLength / 1000f:0.00} км ({PlaneShare:P1}), самый длинный {LongestPlane:0} м при уклоне {LongestPlaneGrade:P1}\n"
                + $"  концы видимой воды без другой воды рядом: {Caps}, ширина среза {Stats(CapWidths)}, рубленых шире {ABRUPT_CAP:0} м {AbruptCaps}";

            foreach (KeyValuePair<string, List<string>> pair in Examples)
                text += $"\n  примеры {pair.Key}: {string.Join(" ", pair.Value)}";

            return text;
        }

        private static string Stats(List<float> values)
        {
            if (values.Count == 0)
                return "—";

            var sorted = new List<float>(values);
            sorted.Sort();

            return $"мин {sorted[0]:0.0} / медиана {sorted[sorted.Count / 2]:0.0} / макс {sorted[^1]:0.0} м";
        }
    }

    private struct Line
    {
        public bool Valid;
        public float Left, Right;
        public Vector2 Normal;
    }

    public static Result Measure(HeightMap carved, WaterMap water)
    {
        var result = new Result();

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            if (points.Count < 2)
                continue;

            var lines = new Line[points.Count];
            var open = new bool[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                open[i] = water.RiverOpen(points[i]) && !NearOtherWater(water, river, points[i]);
                lines[i] = open[i] ? Waterline(carved, points, i) : default;
            }

            Sources(water, river, points, lines, open, result);
            Bands(points, open, result);
            Perched(carved, points, lines, result);
            Straight(points, lines, result);
            Planes(points, open, result);
            Caps(water, river, points, lines, open, result);
        }

        return result;
    }

    private static bool NearOtherWater(WaterMap water, int river, RiverPoint point)
    {
        float reach = 0.5f * point.Width + MARCH_SLACK + 2f;

        if (water.InsideOtherRiver(river, point.Position, reach))
            return true;

        for (int k = 0; k < 8; k++)
        {
            float angle = k * Mathf.PI / 4f;
            Vector2 at = point.Position + reach * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            if (water.StandingAt(Mathf.Clamp(at.x, 0f, water.WorldSize), Mathf.Clamp(at.y, 0f, water.WorldSize), out _))
                return true;
        }

        return false;
    }

    private static Vector2 Normal(List<RiverPoint> points, int i)
    {
        Vector2 before = points[Mathf.Max(0, i - 1)].Position;
        Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)].Position;
        Vector2 tangent = (after - before).normalized;

        return new Vector2(-tangent.y, tangent.x);
    }

    private static Line Waterline(HeightMap carved, List<RiverPoint> points, int i)
    {
        RiverPoint point = points[i];
        Vector2 normal = Normal(points, i);
        var line = new Line { Normal = normal };

        if (carved.SampleWorldSmooth(point.Position.x, point.Position.y) >= point.Surface)
            return line;

        float limit = 0.5f * point.Width + MARCH_SLACK;

        line.Left = March(carved, point.Position, normal, point.Surface, limit);
        line.Right = March(carved, point.Position, -normal, point.Surface, limit);
        line.Valid = line.Left >= 0f && line.Right >= 0f;

        return line;
    }

    private static float March(HeightMap carved, Vector2 origin, Vector2 direction, float surface, float limit)
    {
        float previous = surface - carved.SampleWorldSmooth(origin.x, origin.y);

        for (float d = MARCH_STEP; d <= limit; d += MARCH_STEP)
        {
            Vector2 at = origin + direction * d;
            float depth = surface - carved.SampleWorldSmooth(at.x, at.y);

            if (depth <= 0f)
                return d - MARCH_STEP * depth / Mathf.Min(-1e-6f, depth - previous);

            previous = depth;
        }

        return -1f;
    }

    private static void Sources(WaterMap water, int river, List<RiverPoint> points, Line[] lines, bool[] open, Result result)
    {
        if (RiverEnds.Start(water, river) != RiverEnd.Loose)
            return;

        result.Springs++;
        result.SourceWidths.Add(points[0].Width);

        int first = -1;

        for (int i = 0; i < points.Count && first < 0; i++)
        {
            if (open[i])
                first = i;
        }

        if (first < 0)
            return;

        result.SourceVisible.Add(lines[first].Valid ? lines[first].Left + lines[first].Right : 0f);

        if (points[0].Width > ABRUPT_CAP)
            result.Example("широкий исток", points[0].Position, $" {points[0].Width:0.0} м");

        float along = 0f;
        bool at100 = false, at300 = false;

        for (int i = 1; i < points.Count && along < 300f; i++)
        {
            float step = Vector2.Distance(points[i - 1].Position, points[i].Position);
            along += step;

            if (step > 1e-3f)
                result.SteepestSourceRamp = Mathf.Max(result.SteepestSourceRamp, (points[i].Width - points[i - 1].Width) / step);

            if (!at100 && along >= 100f)
            {
                at100 = true;
                result.WidthAt100.Add(points[i].Width);
            }

            if (!at300 && along >= 300f)
            {
                at300 = true;
                result.WidthAt300.Add(points[i].Width);
            }
        }
    }

    private static void Bands(List<RiverPoint> points, bool[] open, Result result)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (!open[i] || !open[i - 1])
                continue;

            float length = Vector2.Distance(points[i - 1].Position, points[i].Position);
            float width = 0.5f * (points[i - 1].Width + points[i].Width);
            int band = 0;

            while (band < BANDS.Length && width >= BANDS[band])
                band++;

            result.BandLength[band] += length;
            result.OpenLength += length;
        }
    }

    private static void Perched(HeightMap carved, List<RiverPoint> points, Line[] lines, Result result)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (!lines[i].Valid)
                continue;

            RiverPoint point = points[i];
            Vector2 left = point.Position + lines[i].Normal * (lines[i].Left + PERCHED_BANK);
            Vector2 right = point.Position - lines[i].Normal * (lines[i].Right + PERCHED_BANK);
            float lower = Mathf.Min(carved.SampleWorldSmooth(left.x, left.y), carved.SampleWorldSmooth(right.x, right.y));

            result.Waterlines++;

            if (lower - point.Surface >= PERCHED_FREEBOARD)
                continue;

            result.Perched++;
            result.Example("вода вровень с берегом", point.Position, $" {lower - point.Surface:0.00} м");
        }
    }

    private static void Straight(List<RiverPoint> points, Line[] lines, Result result)
    {
        foreach (float side in new[] { 1f, -1f })
        {
            var run = new List<Vector2>();

            for (int i = 0; i <= points.Count; i++)
            {
                bool valid = i < points.Count && lines[i].Valid;

                if (valid)
                {
                    Vector2 edge = points[i].Position + lines[i].Normal * (side > 0f ? lines[i].Left : -lines[i].Right);

                    if (run.Count > 0)
                        result.EdgeLength += Vector2.Distance(run[^1], edge);

                    run.Add(edge);

                    if (run.Count < 3 || Within(run))
                        continue;

                    Close(run, run.Count - 1, result);
                    run.RemoveRange(0, run.Count - 2);
                    continue;
                }

                Close(run, run.Count, result);
                run.Clear();
            }
        }
    }

    private static bool Within(List<Vector2> run)
    {
        Vector2 a = run[0], b = run[^1];
        Vector2 axis = b - a;
        float length = axis.magnitude;

        if (length < 1e-3f)
            return true;

        axis /= length;

        for (int k = 1; k + 1 < run.Count; k++)
        {
            Vector2 offset = run[k] - a;

            if (Mathf.Abs(offset.x * axis.y - offset.y * axis.x) > STRAIGHT_TOLERANCE)
                return false;
        }

        return true;
    }

    private static void Close(List<Vector2> run, int count, Result result)
    {
        if (count < 2)
            return;

        float length = 0f;

        for (int k = 1; k < count; k++)
            length += Vector2.Distance(run[k - 1], run[k]);

        if (length < STRAIGHT_RUN)
            return;

        result.StraightRuns++;
        result.StraightLength += length;

        if (length > result.LongestStraight)
            result.LongestStraight = length;

        result.Example("прямой урез", run[0], $" {length:0} м");
    }

    private static void Planes(List<RiverPoint> points, bool[] open, Result result)
    {
        int start = -1;
        float length = 0f;

        for (int i = 1; i <= points.Count; i++)
        {
            bool inclined = i < points.Count && open[i] && open[i - 1] && Grade(points[i - 1], points[i]) > POOL_GRADE;

            if (inclined)
            {
                if (start < 0)
                {
                    start = i - 1;
                    length = 0f;
                }

                length += Vector2.Distance(points[i - 1].Position, points[i].Position);
                continue;
            }

            if (start < 0)
                continue;

            float drop = points[start].Surface - points[i - 1].Surface;

            if (length >= PLANE_RUN && drop / length > PLANE_GRADE)
            {
                result.PlaneRuns++;
                result.PlaneLength += length;

                if (length > result.LongestPlane)
                {
                    result.LongestPlane = length;
                    result.LongestPlaneGrade = drop / length;
                }

                result.Example("наклонная плоскость", points[start].Position, $" {length:0} м {drop / length:P0}");
            }

            start = -1;
        }
    }

    private static float Grade(RiverPoint a, RiverPoint b)
    {
        return (a.Surface - b.Surface) / Mathf.Max(0.01f, Vector2.Distance(a.Position, b.Position));
    }

    private static void Caps(WaterMap water, int river, List<RiverPoint> points, Line[] lines, bool[] open, Result result)
    {
        if (open[0])
            Cap(water, river, points, lines, 0, -1, result);

        if (open[^1])
            Cap(water, river, points, lines, points.Count - 1, 1, result);
    }

    private static void Cap(WaterMap water, int river, List<RiverPoint> points, Line[] lines, int i, int direction, Result result)
    {
        RiverPoint point = points[i];
        Vector2 outward = (point.Position - points[Mathf.Clamp(i - direction, 0, points.Count - 1)].Position).normalized;
        Vector2 ahead = point.Position + outward * (0.5f * point.Width + CAP_REACH);
        float margin = water.CellSize * 4f;

        if (ahead.x <= margin || ahead.y <= margin || ahead.x >= water.WorldSize - margin || ahead.y >= water.WorldSize - margin)
            return;

        if (water.StandingAt(ahead.x, ahead.y, out _) || water.InsideOtherRiver(river, ahead, CAP_REACH))
            return;

        float width = lines[i].Valid ? lines[i].Left + lines[i].Right : 0f;
        result.Caps++;
        result.CapWidths.Add(width);

        if (width <= ABRUPT_CAP)
            return;

        result.AbruptCaps++;
        result.Example("рубленый конец", point.Position, $" {width:0.0} м");
    }
}

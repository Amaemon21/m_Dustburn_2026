using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private static float[] Along(List<RiverPoint> points)
    {
        var along = new float[points.Count];

        for (int i = 1; i < points.Count; i++)
            along[i] = along[i - 1] + Vector2.Distance(points[i - 1].Position, points[i].Position);

        return along;
    }

    private static void Deepen(List<RiverPoint> points, List<float> channels)
    {
        int count = points.Count;

        if (count < 2)
            return;

        float[] along = Along(points);
        var extra = new float[count];

        for (int i = 0; i < count; i++)
            extra[i] = points[i].Submerged ? 0f : Mathf.Clamp(points[i].Surface - channels[i], 0f, MAX_DEEPEN);

        for (int i = 1; i < count; i++)
            extra[i] = Mathf.Max(extra[i], extra[i - 1] - DEEPEN_GRADE * (along[i] - along[i - 1]));

        for (int i = count - 2; i >= 0; i--)
            extra[i] = Mathf.Max(extra[i], extra[i + 1] - DEEPEN_GRADE * (along[i + 1] - along[i]));

        float fromWater = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            fromWater = points[i].Submerged ? 0f : fromWater + (i > 0 ? along[i] - along[i - 1] : 0f);
            extra[i] = points[i].Submerged ? 0f : Mathf.Min(extra[i], DEEPEN_GRADE * fromWater);
        }

        float toWater = 0f;

        for (int i = count - 1; i >= 0; i--)
        {
            toWater = points[i].Submerged ? 0f : toWater + (i < count - 1 ? along[i + 1] - along[i] : 0f);
            extra[i] = Mathf.Min(extra[i], DEEPEN_GRADE * toWater);
        }

        float next = float.NegativeInfinity;

        for (int i = count - 1; i >= 0; i--)
        {
            RiverPoint point = points[i];

            if (point.Submerged)
            {
                next = point.Surface;
                continue;
            }

            float surface = Mathf.Min(point.Surface, Mathf.Max(point.Surface - extra[i], next));
            point.Bed -= point.Surface - surface;
            point.Surface = surface;
            points[i] = point;
            next = surface;
        }
    }

    public static void Pools(List<RiverPoint> points, int from = 0, int to = int.MaxValue)
    {
        float[] along = Along(points);
        int first = 0;

        while (first < points.Count)
        {
            if (points[first].Submerged)
            {
                first++;
                continue;
            }

            int last = first;

            while (last + 1 < points.Count && !points[last + 1].Submerged)
                last++;

            int start = Mathf.Max(first, from), end = Mathf.Min(last, to);

            if (start < end)
                Stairs(points, along, start, end);

            first = last + 1;
        }
    }

    private static void Stairs(List<RiverPoint> points, float[] along, int first, int last)
    {
        var levels = new float[last - first + 1];

        for (int i = first; i <= last; i++)
            levels[i - first] = points[i].Surface;

        int from = first;

        while (from < last)
        {
            float width = points[from].Width;
            float spacing = Mathf.Clamp(POOL_WIDTHS * width, POOL_MIN_SPACING, POOL_MAX_SPACING);
            int to = Ahead(along, from, last, spacing);
            float top = points[from].Surface;
            float grade = (top - points[to].Surface) / Mathf.Max(1e-3f, along[to] - along[from]);

            if (grade < POOL_MIN_GRADE)
            {
                from++;
                continue;
            }

            grade = Mathf.Min(grade, MAX_WATER_GRADE);

            float pool = Mathf.Clamp(spacing * (1f - grade / RIFFLE_GRADE), POOL_MIN_LENGTH, Mathf.Max(POOL_MIN_LENGTH, PoolDrop(width) / grade));
            float run = Mathf.Max(spacing - pool, grade * pool / (RIFFLE_GRADE - grade));

            to = Ahead(along, from, last, Mathf.Min(pool + run, MAX_RIFFLE * RIFFLE_GRADE / grade));

            while (to > from + 1 && (top - points[to].Surface) / RIFFLE_GRADE > MAX_RIFFLE)
                to--;

            float level = points[to].Surface;
            float drop = top - level;
            float length = along[to] - along[from];
            float riffle = drop / RIFFLE_GRADE;

            if (length - riffle < POOL_MIN_LENGTH)
                riffle = Mathf.Max(drop / STEEPEST_RIFFLE, length - POOL_MIN_LENGTH);

            for (int i = from + 1; i <= to; i++)
            {
                float travelled = along[i] - along[from];
                levels[i - first] = travelled < riffle ? top - drop * travelled / riffle : level;
            }

            from = to;
        }

        for (int i = first + 1; i <= last; i++)
        {
            RiverPoint point = points[i];
            float surface = Mathf.Min(point.Surface, levels[i - first]);

            point.Bed -= point.Surface - surface;
            point.Surface = surface;
            points[i] = point;
        }
    }

    private static int Ahead(float[] along, int from, int limit, float distance)
    {
        for (int i = from + 1; i <= limit; i++)
        {
            if (along[i] - along[from] >= distance)
                return i;
        }

        return limit;
    }

    private static void Spring(List<RiverPoint> points)
    {
        if (points.Count == 0 || points[0].Submerged)
            return;

        RiverPoint source = points[0];
        source.Width = Mathf.Min(source.Width, SOURCE_WIDTH);
        points[0] = source;
    }

    private void Estuary(List<RiverPoint> points)
    {
        float sea = _config.SeaLevel;

        if (sea <= 0f || points.Count == 0 || !points[^1].Submerged || points[^1].Surface > sea)
            return;

        for (int i = points.Count - 1; i > 0; i--)
        {
            RiverPoint point = points[i];

            if (point.Submerged)
                continue;

            RiverPoint before = points[i - 1];
            float reach = sea + STEEPEST_RIFFLE * Vector2.Distance(before.Position, point.Position);

            if (point.Surface > sea + ESTUARY_BAND || !before.Submerged && before.Surface > reach)
                break;

            point.Bed -= point.Surface - sea;
            point.Surface = sea;
            point.Submerged = true;
            points[i] = point;
        }
    }

    public static void Reprofile(List<RiverPoint> points)
    {
        Backwater(points);
        Ease(points);
        Pools(points);
    }

    public static void Reprofile(List<RiverPoint> points, List<(int First, int Last)> ranges)
    {
        var before = new float[points.Count];

        for (int i = 0; i < points.Count; i++)
            before[i] = points[i].Surface;

        Backwater(points);

        var moved = new bool[points.Count];

        foreach ((int first, int last) in ranges)
        {
            for (int i = Mathf.Max(0, first); i <= last && i < points.Count; i++)
                moved[i] = true;
        }

        for (int i = 0; i < points.Count; i++)
            moved[i] |= points[i].Surface != before[i];

        EaseMoved(points, moved);

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].Surface == before[i])
                continue;

            for (int k = Mathf.Max(0, i - 1); k <= Mathf.Min(points.Count - 1, i + 1); k++)
                moved[k] = true;
        }

        for (int start = 0; start < points.Count; start++)
        {
            if (!moved[start])
                continue;

            int first = start, last = start;

            while (last + 1 < points.Count && moved[last + 1])
                last++;

            while (first > 0 && Falls(points, first - 1))
                first--;

            Pools(points, first, last);
            start = last;
        }
    }

    private static bool Falls(List<RiverPoint> points, int segment)
    {
        RiverPoint a = points[segment], b = points[segment + 1];

        return !a.Submerged && !b.Submerged && a.Surface - b.Surface > POOL_MIN_GRADE * Vector2.Distance(a.Position, b.Position);
    }

    private static void EaseMoved(List<RiverPoint> points, bool[] moved)
    {
        var seeds = (bool[])moved.Clone();
        bool chain = false;

        for (int i = points.Count - 2; i >= 0; i--)
        {
            chain |= seeds[i + 1];

            if (!chain)
                continue;

            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface > limit)
            {
                point.Bed -= point.Surface - limit;
                point.Surface = limit;
                points[i] = point;
                moved[i] = true;
                continue;
            }

            if (Falls(points, i))
            {
                moved[i] = true;
                continue;
            }

            chain = false;
        }
    }

    public static void Backwater(List<RiverPoint> points)
    {
        float[] along = Along(points);

        for (int mouth = 1; mouth < points.Count; mouth++)
        {
            if (!points[mouth].Submerged || points[mouth - 1].Submerged)
                continue;

            float level = points[mouth].Surface;
            float reach = Mathf.Max(BACKWATER_MIN, BACKWATER_WIDTHS * points[mouth].Width);

            for (int i = mouth - 1; i >= 0 && !points[i].Submerged && along[mouth] - along[i] <= reach; i--)
            {
                RiverPoint point = points[i];

                if (point.Surface <= level)
                    continue;

                point.Bed -= point.Surface - level;
                point.Surface = level;
                points[i] = point;
            }
        }
    }

    private static void Ramp(List<RiverPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            RiverPoint point = points[i];
            float limit = points[i - 1].Width + WIDTH_RAMP * Vector2.Distance(points[i - 1].Position, point.Position);

            if (point.Width <= limit)
                continue;

            point.Width = limit;
            points[i] = point;
        }
    }

    private static void Taper(List<RiverPoint> points)
    {
        for (int i = points.Count - 2; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Width + WIDTH_RAMP * Vector2.Distance(points[i + 1].Position, point.Position);

            if (point.Width <= limit)
                continue;

            point.Width = limit;
            points[i] = point;
        }
    }

    private static void Ease(List<RiverPoint> points)
    {
        for (int i = points.Count - 2; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface <= limit)
                continue;

            point.Bed -= point.Surface - limit;
            point.Surface = limit;
            points[i] = point;
        }
    }

    private static void Bridge(List<RiverPoint> points)
    {
        int last = -1;

        for (int i = 0; i < points.Count; i++)
        {
            if (!points[i].Submerged)
                continue;

            if (last >= 0 && i - last - 1 <= BRIDGE_GAP)
            {
                for (int j = last + 1; j < i; j++)
                {
                    RiverPoint point = points[j];
                    point.Submerged = true;
                    point.Surface = Mathf.Min(point.Surface, points[last].Surface);
                    points[j] = point;
                }
            }

            last = i;
        }
    }
}

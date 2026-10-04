using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class Hydrology
{
    private void Profile(HydrologyGrid grid, WaterMap water, List<RiverCourse> courses, Dictionary<int, float> lowered)
    {
        water.Rivers.Clear();
        Stamps?.Tracks.Clear();

        var surfaces = new Dictionary<int, float>();
        var shapedPaths = new RiverPath[courses.Count];

        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];
            WaterStampCourse stamp = course.Stamp;
            RiverPath shaped = Level(water, course.Points, course.Areas, WidthScale(course), stamp?.DepthScale, lowered, course.MaxWidth);

            if (shaped.Points.Count < 2)
                continue;

            if (stamp == null)
            {
                if (course.JoinPath >= 0 && course.JoinPath < c && shapedPaths[course.JoinPath] != null)
                    Join(shaped, SurfaceNear(shapedPaths[course.JoinPath], shaped.Points[^1].Position.x, shaped.Points[^1].Position.y));

                foreach (int cell in course.Cells)
                {
                    if (!surfaces.ContainsKey(cell))
                        surfaces[cell] = SurfaceNear(shaped, grid.CellSize * (cell % grid.Resolution + 0.5f), grid.CellSize * (cell / grid.Resolution + 0.5f));
                }
            }
            else
            {
                if (course.JoinPath >= 0 && course.JoinPath < c && shapedPaths[course.JoinPath] != null)
                    Join(shaped, SurfaceNear(shapedPaths[course.JoinPath], shaped.Points[^1].Position.x, shaped.Points[^1].Position.y), stamp.DepthScale);

                if (stamp.Parent >= 0 && shapedPaths[stamp.Parent] != null)
                    Cap(shaped, SurfaceNear(shapedPaths[stamp.Parent], shaped.Points[0].Position.x, shaped.Points[0].Position.y));
            }

            if (course.Cells.Count > 0)
            {
                shaped.SourceArea = grid.Area(course.Cells[0]);
                shaped.SourceDonor = LargestDonor(grid, course.Cells[0]);
            }
            else if (stamp is { Parent: >= 0 })
            {
                shaped.Source = RiverSource.Distributary;
            }

            shapedPaths[c] = shaped;
            water.Rivers.Add(shaped);
            Stamps?.Tracks.Add(stamp?.Track);
        }

        for (int c = 0; c < courses.Count; c++)
        {
            int parent = courses[c].JoinPath;

            if (shapedPaths[c] != null && parent >= 0 && parent < courses.Count && shapedPaths[parent] != null)
                shapedPaths[c].Parent = water.Rivers.IndexOf(shapedPaths[parent]);
        }

        Widen(courses, shapedPaths);
        Confluences(courses, shapedPaths);
    }

    private static float LargestDonor(HydrologyGrid grid, int cell)
    {
        int resolution = grid.Resolution;
        int column = cell % resolution, row = cell / resolution;
        float largest = 0f;

        for (int direction = 0; direction < 8; direction++)
        {
            int c = column + OffsetX[direction], r = row + OffsetZ[direction];

            if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                continue;

            int next = r * resolution + c;

            if (grid.Receiver[next] == cell)
                largest = Mathf.Max(largest, grid.Area(next));
        }

        return largest;
    }

    private void Confluences(List<RiverCourse> courses, RiverPath[] shapedPaths)
    {
        for (int round = 0; round < CONFLUENCE_ROUNDS; round++)
        {
            bool changed = false;

            for (int c = 0; c < courses.Count; c++)
            {
                RiverCourse course = courses[c];

                if (course.JoinPath < 0 || course.JoinPath >= c || shapedPaths[c] == null || shapedPaths[course.JoinPath] == null)
                    continue;

                RiverPath tributary = shapedPaths[c];
                RiverPath trunk = shapedPaths[course.JoinPath];
                RiverPoint end = tributary.Points[^1];
                int at = NearestPoint(trunk, end.Position);
                float level = trunk.Points[at].Surface;

                if (trunk.Points[at].Submerged)
                    continue;

                if (end.Surface > level + MERGE_TOLERANCE)
                {
                    Join(tributary, level, course.Stamp?.DepthScale);
                    changed = true;
                }
                else if (end.Surface < level - CONFLUENCE_TOLERANCE)
                {
                    changed |= Drawdown(trunk, at, end.Surface);
                }
            }

            if (!changed)
                break;
        }
    }

    private static bool Drawdown(RiverPath trunk, int from, float level)
    {
        List<RiverPoint> points = trunk.Points;
        int last = from;

        while (last + 1 < points.Count && !points[last + 1].Submerged)
            last++;

        if (last + 1 < points.Count && points[last + 1].Surface > level)
            return false;

        for (int i = from; i <= last; i++)
        {
            RiverPoint point = points[i];

            if (point.Surface <= level)
                break;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            points[i] = point;
        }

        for (int i = from - 1; i >= 0; i--)
        {
            RiverPoint point = points[i];
            float limit = points[i + 1].Surface + MAX_WATER_GRADE * Vector2.Distance(point.Position, points[i + 1].Position);

            if (point.Surface <= limit)
                break;

            point.Bed -= point.Surface - limit;
            point.Surface = limit;
            points[i] = point;
        }

        return true;
    }

    private static int NearestPoint(RiverPath path, Vector2 position)
    {
        int best = 0;
        float nearest = float.MaxValue;

        for (int i = 0; i < path.Points.Count; i++)
        {
            float distance = (path.Points[i].Position - position).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = i;
        }

        return best;
    }

    private static void Cap(RiverPath path, float level)
    {
        for (int i = 0; i < path.Points.Count; i++)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                continue;

            point.Bed -= point.Surface - level;
            point.Surface = level;
            path.Points[i] = point;
        }
    }

    private void Join(RiverPath path, float joined, float[] depthScale = null)
    {
        float level = joined;
        bool lowered = false;

        for (int i = path.Points.Count - 1; i >= 0; i--)
        {
            RiverPoint point = path.Points[i];

            if (point.Surface <= level)
                break;

            point.Surface = level;
            point.Bed = Mathf.Min(point.Bed, level - ChannelDepth(point.Flow, depthScale == null ? 1f : depthScale[i]));
            path.Points[i] = point;
            lowered = true;

            level += MAX_JOIN_GRADE * (i > 0 ? Vector2.Distance(path.Points[i - 1].Position, point.Position) : 0f);
        }

        if (lowered)
            Pools(path.Points);
    }

    private static float SurfaceNear(RiverPath path, float x, float z)
    {
        float best = float.MaxValue;
        float surface = 0f;
        var point = new Vector2(x, z);

        foreach (RiverPoint candidate in path.Points)
        {
            float distance = (candidate.Position - point).sqrMagnitude;

            if (distance >= best)
                continue;

            best = distance;
            surface = candidate.Surface;
        }

        return surface;
    }

    private RiverPath Level(WaterMap water, List<Vector2> points, List<float> areas, float[] widthScale, float[] depthScale, Dictionary<int, float> lowered, float maxWidth)
    {
        var path = new RiverPath();
        float level = float.PositiveInfinity;
        float outflow = float.NaN;
        float sinceOutlet = 0f;
        var bodies = new List<int>(points.Count);
        var channels = new List<float>(points.Count);
        var entered = new HashSet<int>();

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 position = points[i];
            float depth = ChannelDepth(areas[i], depthScale == null ? 1f : depthScale[i]);
            float width = Mathf.Clamp(widthScale == null ? Width(areas[i], maxWidth) : Width(areas[i], maxWidth) * widthScale[i], SOURCE_WIDTH, maxWidth);

            Vector2 before = points[Mathf.Max(0, i - 1)];
            Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)];
            Vector2 tangent = (after - before).normalized;
            var normal = new Vector2(-tangent.y, tangent.x);

            float ground = _map.SampleWorldSmooth(position.x, position.y);
            float banks = ground;

            foreach (float share in BANK_TAPS)
            {
                Vector2 left = position + normal * (width * share + 1f);
                Vector2 right = position - normal * (width * share + 1f);

                banks = Mathf.Min(banks, Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)));
            }

            channels.Add(ChannelLevel(position, normal, width, banks));

            bool sea = OceanAt(position);
            int body = LakeNear(water, position.x, position.y, 0.5f * width + RIVER_PAD);

            if (body >= 0 && _floors != null && body < _floors.Length && level < _floors[body])
                body = -1;

            if (body >= 0)
            {
                if (water.BodyIds[water.CellIndex(position.x, position.y)] == body)
                    entered.Add(body);
                else if (entered.Contains(body) && !HoldsLake(water, body, position, normal, width))
                    body = -1;
            }

            bool lake = body >= 0;
            bodies.Add(body);

            if (lake)
            {
                float surface = water.Bodies[body].Surface;

                if (level < surface)
                {
                    lowered[body] = lowered.TryGetValue(body, out float current) ? Mathf.Min(current, level) : level;
                    surface = level;
                }

                level = Mathf.Min(level, surface);
                outflow = level;
                sinceOutlet = 0f;
            }
            else if (sea)
            {
                level = Mathf.Min(level, _config.SeaLevel);
                outflow = float.NaN;
            }
            else
            {
                float natural = banks - Freeboard(depth);

                if (!float.IsNaN(outflow))
                {
                    sinceOutlet += Vector2.Distance(before, position);
                    float held = Mathf.Min(outflow - OUTFLOW_GRADE * sinceOutlet, banks - OUTFLOW_CLEARANCE);

                    if (held > natural)
                        natural = held;
                    else
                        outflow = float.NaN;
                }

                level = Mathf.Min(level, natural);

                if (_config.SeaLevel > 0f && level <= _config.SeaLevel)
                {
                    level = _config.SeaLevel;
                    sea = OceanAt(position);
                }
            }

            path.Points.Add(new RiverPoint
            {
                Position = position,
                Surface = level,
                Bed = level - depth,
                Width = width,
                Flow = areas[i],
                Submerged = lake || sea
            });
        }

        Spring(path.Points);
        Ramp(path.Points);

        if (widthScale != null)
            Taper(path.Points);

        Bridge(path.Points);
        Deepen(path.Points, channels);
        Ease(path.Points);
        Pools(path.Points);
        Estuary(path.Points);
        Reprofile(path.Points);
        Drain(water, path.Points, bodies, lowered);

        return path;
    }

    private bool HoldsLake(WaterMap water, int body, Vector2 position, Vector2 normal, float width)
    {
        float level = water.Bodies[body].Surface - LAKE_HOLD;

        foreach (float share in BANK_TAPS)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 tap = position + normal * (side * (width * share + 1f));

                if (water.BodyIds[water.CellIndex(tap.x, tap.y)] != body && _map.SampleWorldSmooth(tap.x, tap.y) < level)
                    return false;
            }
        }

        return true;
    }

    private float ChannelLevel(Vector2 position, Vector2 normal, float width, float banks)
    {
        float lowest = banks;

        foreach (float reach in CHANNEL_REACHES)
        {
            Vector2 left = position + normal * (0.5f * width + reach);
            Vector2 right = position - normal * (0.5f * width + reach);

            lowest = Mathf.Min(lowest, Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)));
        }

        return lowest - ChannelFreeboard(width);
    }

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

    private void Drain(WaterMap water, List<RiverPoint> points, List<int> bodies, Dictionary<int, float> lowered)
    {
        for (int i = 0; i < points.Count; i++)
        {
            int body = bodies[i];

            if (body < 0 || points[i].Surface >= water.Bodies[body].Surface - LOWER_MARGIN)
                continue;

            if (_floors != null && body < _floors.Length && points[i].Surface < _floors[body])
                continue;

            lowered[body] = lowered.TryGetValue(body, out float current) ? Mathf.Min(current, points[i].Surface) : points[i].Surface;
        }
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

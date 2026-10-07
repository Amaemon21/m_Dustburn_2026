using System.Collections.Generic;
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
            _levelling = c;
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

    private RiverPath Level(WaterMap water, List<Vector2> points, List<float> areas, float[] widthScale, float[] depthScale, Dictionary<int, float> lowered, float maxWidth)
    {
        var path = new RiverPath();
        float level = float.PositiveInfinity;
        float outflow = float.NaN;
        float sinceOutlet = 0f;
        var bodies = new List<int>(points.Count);
        var channels = new List<float>(points.Count);
        var rims = new List<float>(points.Count);
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

            float rim = ChannelRim(position, normal, width, banks);
            rims.Add(rim);
            channels.Add(rim - ChannelFreeboard(width));

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
                    level = HoldInflow(path.Points, rims, level, surface);

                if (level < surface)
                {
                    if (!lowered.TryGetValue(body, out float current) || level < current)
                    {
                        lowered[body] = level;
                        _lowerReasons[body] = $"course {_levelling} arrives at point {i} ({position.x:0}, {position.y:0}) already at {level:0.00} m";
                    }

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

    private static float HoldInflow(List<RiverPoint> points, List<float> rims, float level, float surface)
    {
        float held = surface;
        int first = points.Count;

        for (int k = points.Count - 1; k >= 0 && !points[k].Submerged && points[k].Surface < surface; k--)
        {
            held = Mathf.Min(held, rims[k] - INFLOW_FREEBOARD);
            first = k;
        }

        if (first == points.Count)
            return level;

        if (first > 0)
            held = Mathf.Min(held, points[first - 1].Surface);

        if (held <= level)
            return level;

        for (int k = first; k < points.Count; k++)
        {
            RiverPoint point = points[k];

            if (point.Surface >= held)
                continue;

            point.Bed += held - point.Surface;
            point.Surface = held;
            points[k] = point;
        }

        return held;
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

    private float ChannelRim(Vector2 position, Vector2 normal, float width, float banks)
    {
        float lowest = banks;

        foreach (float reach in CHANNEL_REACHES)
        {
            Vector2 left = position + normal * (0.5f * width + reach);
            Vector2 right = position - normal * (0.5f * width + reach);

            lowest = Mathf.Min(lowest, Mathf.Min(_map.SampleWorldSmooth(left.x, left.y), _map.SampleWorldSmooth(right.x, right.y)));
        }

        return lowest;
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

            if (lowered.TryGetValue(body, out float current) && current <= points[i].Surface)
                continue;

            lowered[body] = points[i].Surface;
            _lowerReasons[body] = $"course {_levelling} point {i} ({points[i].Position.x:0}, {points[i].Position.y:0}) inside it is shaped to {points[i].Surface:0.00} m";
        }
    }
}

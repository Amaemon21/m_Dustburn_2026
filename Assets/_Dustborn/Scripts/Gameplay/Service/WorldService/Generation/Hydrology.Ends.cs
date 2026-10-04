using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class Hydrology
{
    private void ResolveEnds(HydrologyGrid grid, WaterMap water, List<RiverCourse> courses)
    {
        for (int round = 0; round <= courses.Count; round++)
        {
            bool changed = false;

            for (int c = 0; c < courses.Count; c++)
            {
                RiverCourse course = courses[c];

                if (course.Points.Count < 2 || Terminates(courses, course, water))
                    continue;

                int keep = LastStanding(course, water);
                Vector2 end = course.Points[^1];
                int parent = course.JoinPath;
                Truncate(course, keep < MIN_COURSE_POINTS ? 0 : keep + 1);
                Log.Note(course.Points.Count == 0 ? HydrologyAction.Dropped : HydrologyAction.Shortened, c, end, parent >= 0 ? $"the end is not within reach of course {parent} it was traced into, nor in standing water; kept {course.Points.Count} points" : $"the end is in no standing water or river; kept {course.Points.Count} points up to the last standing water");
                course.JoinPath = -1;
                course.JoinCell = -1;
                changed = true;

                if (course.Points.Count == 0)
                    DroppedCourses++;
                else
                    ShortenedCourses++;
            }

            if (!changed)
                break;
        }
    }

    private void ExtendToWater(HydrologyGrid grid, WaterMap water, List<RiverCourse> courses)
    {
        var index = new CourseIndex(courses);

        for (int c = 0; c < courses.Count; c++)
            index.Add(c);

        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];

            if (course.Points.Count < 2 || Terminates(courses, course, water))
                continue;

            Vector2 end = course.Points[^1];
            int cell = Mathf.Clamp((int)(end.y / grid.CellSize), 0, grid.Resolution - 1) * grid.Resolution + Mathf.Clamp((int)(end.x / grid.CellSize), 0, grid.Resolution - 1);
            var trail = new List<Vector2> { end };
            bool reached = false;

            for (int step = 0; step < EXTEND_CELLS; step++)
            {
                cell = Downhill(grid, cell);

                if (cell < 0 || grid.Sink[cell])
                    break;

                trail.Add(new Vector2((cell % grid.Resolution + 0.5f) * grid.CellSize, (cell / grid.Resolution + 0.5f) * grid.CellSize));

                if (water.BodyIds[cell] < 0 && !grid.Outlet[cell])
                    continue;

                reached = true;

                int inner = Downhill(grid, cell);

                if (inner >= 0)
                    trail.Add(new Vector2((inner % grid.Resolution + 0.5f) * grid.CellSize, (inner / grid.Resolution + 0.5f) * grid.CellSize));

                break;
            }

            int touch = FirstTouch(courses, index, water, c, trail, out Contact contact);

            if (touch >= 0 && contact.Course > c)
                continue;

            if (touch >= 0)
            {
                trail.RemoveRange(touch + 1, trail.Count - touch - 1);
                reached = true;
            }

            if (!reached)
                continue;

            if (trail.Count > 1)
            {
                var flows = new List<float>(trail.Count);

                for (int i = 0; i < trail.Count; i++)
                    flows.Add(course.Areas[^1]);

                Resample(trail, flows, RESAMPLE_STEP, out List<Vector2> points, out List<float> areas);
                int before = course.Points.Count;
                course.Points.AddRange(points.GetRange(1, points.Count - 1));
                course.Areas.AddRange(areas.GetRange(1, areas.Count - 1));
                Extend(course.Stamp, before, course.Points.Count);
            }

            if (touch >= 0)
                Attach(courses, c, course.Points.Count - 1, contact);

            Log.Note(HydrologyAction.Extended, c, course.Points[^1], touch >= 0 ? $"walked downhill into course {contact.Course}" : "walked downhill into a lowered lake or the sea");
            ExtendedCourses++;
        }
    }

    private int FirstTouch(List<RiverCourse> courses, CourseIndex index, WaterMap water, int c, List<Vector2> trail, out Contact contact)
    {
        RiverCourse course = courses[c];
        float width = Width(course.Areas[^1], course.MaxWidth);

        for (int m = 0; m < trail.Count; m++)
        {
            bool ignored = false;

            if (!InStanding(water, trail[m]) && Touches(courses, index, c, trail[m], width, -1, ref ignored, out contact))
                return m;
        }

        contact = default;
        return -1;
    }

    private static int Downhill(HydrologyGrid grid, int cell)
    {
        int resolution = grid.Resolution;
        int column = cell % resolution, row = cell / resolution;
        int best = -1;
        float lowest = grid.Height[cell];

        for (int direction = 0; direction < 8; direction++)
        {
            int c = column + OffsetX[direction], w = row + OffsetZ[direction];

            if (c < 0 || w < 0 || c >= resolution || w >= resolution)
                continue;

            int next = w * resolution + c;

            if (grid.Height[next] >= lowest)
                continue;

            lowest = grid.Height[next];
            best = next;
        }

        return best;
    }

    private bool Terminates(List<RiverCourse> courses, RiverCourse course, WaterMap water)
    {
        Vector2 end = course.Points[^1];
        float reach = 0.5f * Width(course.Areas[^1], course.MaxWidth) + RIVER_PAD;

        if (course.JoinPath >= 0 && course.JoinPath < courses.Count && courses[course.JoinPath].Points.Count >= 2)
        {
            float join = reach + Width(course.Areas[^1]) + JOIN_SLACK * _settings.CellSize;

            if ((Nearest(courses[course.JoinPath].Points, end) - end).sqrMagnitude <= join * join)
                return true;
        }

        if (InStanding(water, end))
            return true;

        if (CoastShaper.Active(_config))
            return false;

        float margin = water.CellSize * 4f;

        return end.x <= margin || end.y <= margin || end.x >= _config.WorldSize - margin || end.y >= _config.WorldSize - margin;
    }

    private bool InStanding(WaterMap water, Vector2 point)
    {
        return SeaAt(point) || water.BodyIds[water.CellIndex(point.x, point.y)] >= 0;
    }

    private int LastStanding(RiverCourse course, WaterMap water)
    {
        for (int i = course.Points.Count - 1; i >= 0; i--)
        {
            if (InStanding(water, course.Points[i]))
                return i;
        }

        return -1;
    }

    private static void JoinTributaries(List<RiverCourse> courses)
    {
        foreach (RiverCourse course in courses)
        {
            List<Vector2> points = course.Points;
            int joinPath = course.JoinPath;

            if (joinPath < 0 || joinPath >= courses.Count || points.Count < 2)
                continue;

            Vector2 end = points[^1];
            Vector2 delta = Nearest(courses[joinPath].Points, end) - end;
            int blend = Mathf.Min(points.Count - 1, JOIN_BLEND_POINTS);

            for (int k = 0; k < blend; k++)
            {
                float t = 1f - k / (float)blend;
                points[points.Count - 1 - k] += delta * (t * t * (3f - 2f * t));
            }
        }
    }

    private static Vector2 Nearest(List<Vector2> line, Vector2 point)
    {
        Vector2 best = line[0];
        float nearest = float.MaxValue;

        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector2 axis = line[i + 1] - line[i];
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - line[i], axis) / length);
            Vector2 candidate = line[i] + axis * t;
            float distance = (candidate - point).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            best = candidate;
        }

        return best;
    }

    private static void Merge(WaterMap water)
    {
        for (int river = 1; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (point.Submerged || !water.InsideEarlierRiver(river, point.Position, -0.5f * point.Width, out float surface) || point.Surface > surface + MERGE_TOLERANCE)
                    continue;

                point.Submerged = true;
                points[i] = point;
            }
        }

        water.InvalidateIndex();
    }

    public static bool MeetTrunks(WaterMap water)
    {
        bool changed = false;

        foreach (RiverPath path in water.Rivers)
        {
            if (path.Parent < 0 || path.Parent >= water.Rivers.Count || path.Points.Count < 2)
                continue;

            RiverPath trunk = water.Rivers[path.Parent];
            float level = trunk.Points[NearestPoint(trunk, path.Points[^1].Position)].Surface;

            if (path.Points[^1].Surface <= level + CONFLUENCE_TOLERANCE)
                continue;

            Descend(path.Points, level);
            changed = true;
        }

        if (changed)
            water.InvalidateIndex();

        return changed;
    }

    private static void Descend(List<RiverPoint> points, float level)
    {
        float target = level;

        for (int i = points.Count - 1; i >= 0; i--)
        {
            RiverPoint point = points[i];

            if (point.Surface <= target)
                break;

            point.Bed -= point.Surface - target;
            point.Surface = target;
            points[i] = point;

            if (i > 0)
                target += MAX_JOIN_GRADE * Vector2.Distance(points[i - 1].Position, point.Position);
        }

        Pools(points);
    }

    private static void SettleMouths(WaterMap water)
    {
        for (int river = 1; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;
            bool merged = false;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (!point.Submerged || !water.InsideEarlierRiver(river, point.Position, -0.5f * point.Width, out float trunk))
                    continue;

                merged = true;

                if (point.Surface <= trunk)
                    continue;

                point.Bed -= point.Surface - trunk;
                point.Surface = trunk;
                points[i] = point;
            }

            if (merged)
                Reprofile(points);
        }

        water.InvalidateIndex();
    }
}

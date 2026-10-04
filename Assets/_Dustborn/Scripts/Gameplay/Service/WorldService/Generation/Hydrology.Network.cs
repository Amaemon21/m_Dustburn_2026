using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    public const float CONTACT_GAP = 30f;
    private const float CONTACT_CELL = 64f;
    private const float JOIN_ENTRY_COT = 0.84f;
    private const float MIN_JOIN_SHIFT = 8f;
    private const float STUB_LENGTH = 150f;
    private const float STUB_WIDTHS = 8f;
    private const float HEAD_MERGE_CELLS = 6f;

    public int CapturedCourses { get; private set; }
    public int StubCourses { get; private set; }
    public int MergedHeads { get; private set; }

    private readonly struct Contact
    {
        public readonly int Course;
        public readonly int Segment;
        public readonly float Along;

        public Contact(int course, int segment, float along)
        {
            Course = course;
            Segment = segment;
            Along = along;
        }
    }

    private sealed class CourseIndex
    {
        private readonly Dictionary<long, List<(int Course, int Segment)>> _cells = new();
        private readonly List<RiverCourse> _courses;
        private readonly HashSet<(int, int)> _seen = new();

        public CourseIndex(List<RiverCourse> courses)
        {
            _courses = courses;
        }

        public void Add(int course)
        {
            List<Vector2> points = _courses[course].Points;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                int minX = Cell(Mathf.Min(a.x, b.x)), maxX = Cell(Mathf.Max(a.x, b.x));
                int minZ = Cell(Mathf.Min(a.y, b.y)), maxZ = Cell(Mathf.Max(a.y, b.y));

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        long key = Key(x, z);

                        if (!_cells.TryGetValue(key, out List<(int, int)> list))
                            _cells[key] = list = new List<(int, int)>();

                        list.Add((course, i));
                    }
                }
            }
        }

        public IEnumerable<(int Course, int Segment)> Near(Vector2 point, float reach)
        {
            _seen.Clear();
            int minX = Cell(point.x - reach), maxX = Cell(point.x + reach);
            int minZ = Cell(point.y - reach), maxZ = Cell(point.y + reach);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!_cells.TryGetValue(Key(x, z), out List<(int Course, int Segment)> list))
                        continue;

                    foreach ((int Course, int Segment) entry in list)
                    {
                        if (_seen.Add(entry))
                            yield return entry;
                    }
                }
            }
        }

        private static int Cell(float value) => Mathf.FloorToInt(value / CONTACT_CELL);

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }

    private float Widest => Mathf.Max(MainWidth, MAX_WIDTH * _settings.RiverWidthScale);

    private void Capture(List<RiverCourse> courses, WaterMap water)
    {
        var index = new CourseIndex(courses);

        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];

            if (course.Points.Count >= 2 && FirstContact(courses, index, water, c, out int keep, out Contact contact))
            {
                CapturedCourses++;
                Log.Note(HydrologyAction.Captured, c, course.Points[keep], keep == 0 ? $"starts inside course {contact.Course} and is dropped" : $"meets course {contact.Course} after {keep} points and joins it there");

                if (keep == 0)
                    Truncate(course, 0);
                else
                    Attach(courses, c, keep, contact);
            }

            index.Add(c);
        }
    }

    private bool FirstContact(List<RiverCourse> courses, CourseIndex index, WaterMap water, int c, out int keep, out Contact contact)
    {
        RiverCourse course = courses[c];
        int parent = course.Stamp?.Parent ?? -1;
        bool besideParent = parent >= 0;

        for (int k = 0; k < course.Points.Count; k++)
        {
            Vector2 point = course.Points[k];

            if (InStanding(water, point))
                continue;

            bool nearParent = false;

            if (Touches(courses, index, c, point, Width(course.Areas[k], course.MaxWidth), besideParent ? parent : -1, ref nearParent, out contact))
            {
                keep = k;
                return true;
            }

            besideParent &= nearParent;
        }

        keep = -1;
        contact = default;
        return false;
    }

    private bool Touches(List<RiverCourse> courses, CourseIndex index, int self, Vector2 point, float width, int ignore, ref bool nearIgnored, out Contact contact)
    {
        float best = float.MaxValue;
        contact = default;

        foreach ((int other, int segment) in index.Near(point, 0.5f * (width + Widest) + CONTACT_GAP))
        {
            if (other == self)
                continue;

            RiverCourse trunk = courses[other];
            Vector2 a = trunk.Points[segment], axis = trunk.Points[segment + 1] - a;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, axis) / length);
            float distance = (a + axis * t - point).sqrMagnitude;
            float reach = 0.5f * (width + Width(trunk.Areas[segment], trunk.MaxWidth)) + CONTACT_GAP;

            if (distance > reach * reach || distance >= best)
                continue;

            if (other == ignore)
            {
                nearIgnored = true;
                continue;
            }

            best = distance;
            contact = new Contact(other, segment, t);
        }

        return best < float.MaxValue;
    }

    private void Attach(List<RiverCourse> courses, int c, int keep, Contact contact)
    {
        RiverCourse course = courses[c];
        RiverCourse trunk = courses[contact.Course];
        Vector2 from = course.Points[keep];
        Vector2 nearest = Vector2.Lerp(trunk.Points[contact.Segment], trunk.Points[contact.Segment + 1], contact.Along);
        float gap = Vector2.Distance(from, nearest);
        float along = Along(trunk.Points, contact.Segment, contact.Along) + Mathf.Max(MIN_JOIN_SHIFT, gap * JOIN_ENTRY_COT);
        Vector2 target = PointAlong(trunk.Points, along, out Vector2 flow);
        Vector2 heading = keep > 0 ? (from - course.Points[keep - 1]).normalized : (target - from).normalized;
        Vector2 entry = gap > 1e-3f ? (flow + (nearest - from) / gap).normalized : flow;
        float span = Vector2.Distance(from, target);
        float area = course.Areas[keep];

        Truncate(course, keep + 1);
        int before = course.Points.Count;
        int steps = Mathf.Max(1, Mathf.CeilToInt(span / RESAMPLE_STEP));

        for (int i = 1; i <= steps; i++)
        {
            course.Points.Add(Hermite(from, heading * span, target, entry * span, i / (float)steps));
            course.Areas.Add(area);
        }

        Extend(course.Stamp, before, course.Points.Count);
        course.JoinPath = contact.Course;
        course.JoinCell = -1;
    }

    private static Vector2 Hermite(Vector2 from, Vector2 start, Vector2 to, Vector2 end, float t)
    {
        float t2 = t * t, t3 = t2 * t;

        return (2f * t3 - 3f * t2 + 1f) * from + (t3 - 2f * t2 + t) * start + (-2f * t3 + 3f * t2) * to + (t3 - t2) * end;
    }

    private static float Along(List<Vector2> line, int segment, float t)
    {
        float along = 0f;

        for (int i = 0; i < segment; i++)
            along += Vector2.Distance(line[i], line[i + 1]);

        return along + t * Vector2.Distance(line[segment], line[segment + 1]);
    }

    private static Vector2 PointAlong(List<Vector2> line, float along, out Vector2 flow)
    {
        flow = (line[1] - line[0]).normalized;

        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector2 axis = line[i + 1] - line[i];
            float length = axis.magnitude;

            if (length < 1e-6f)
                continue;

            flow = axis / length;

            if (along <= length)
                return line[i] + flow * Mathf.Max(0f, along);

            along -= length;
        }

        return line[^1];
    }

    private void MergeHeads(List<RiverCourse> courses)
    {
        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];
            int p = course.JoinPath;

            if (p < 0 || p >= courses.Count || p == c || course.Points.Count < 2 || courses[p].Points.Count < 2)
                continue;

            RiverCourse trunk = courses[p];
            int k = NearestIndex(trunk.Points, course.Points[^1]);
            float head = Length(trunk.Points.GetRange(0, k + 1));
            float reach = HEAD_MERGE_CELLS * _settings.CellSize + Width(course.Areas[^1], course.MaxWidth);

            if (head > reach)
                continue;

            Log.Note(HydrologyAction.MergedHead, c, course.Points[^1], $"joins course {p} {head:0} m below its head, so course {p} continues it and its own {k + 1} head points are dropped");
            Prepend(course, trunk, k);

            foreach (RiverCourse other in courses)
            {
                if (other.JoinPath == c)
                    other.JoinPath = p;
            }

            Truncate(course, 0);
            course.JoinPath = -1;
            MergedHeads++;
        }
    }

    private static void Prepend(RiverCourse course, RiverCourse trunk, int k)
    {
        int keep = trunk.Points.Count - k - 1;
        var points = new List<Vector2>(course.Points);
        var areas = new List<float>(course.Areas);
        points.AddRange(trunk.Points.GetRange(k + 1, keep));

        for (int i = k + 1; i < trunk.Areas.Count; i++)
            areas.Add(Mathf.Max(trunk.Areas[i], areas[^1]));

        var cells = new List<int>(course.Cells);
        cells.AddRange(trunk.Cells);

        if (course.Stamp != null || trunk.Stamp != null)
        {
            WaterStampCourse stamp = trunk.Stamp ?? new WaterStampCourse();
            stamp.WidthScale = Join(course.Stamp?.WidthScale, course.Points.Count, 1f, trunk.Stamp?.WidthScale, k + 1, keep);
            stamp.DepthScale = Join(course.Stamp?.DepthScale, course.Points.Count, 1f, trunk.Stamp?.DepthScale, k + 1, keep);

            if (course.Stamp?.Track != null || trunk.Stamp?.Track != null)
            {
                stamp.Track = new WaterStampTrack(points.Count)
                {
                    Placement = Join(course.Stamp?.Track?.Placement, course.Points.Count, -1, trunk.Stamp?.Track?.Placement, k + 1, keep),
                    Stamp = Join(course.Stamp?.Track?.Stamp, course.Points.Count, Vector2.zero, trunk.Stamp?.Track?.Stamp, k + 1, keep)
                };
            }

            stamp.Points = points;
            stamp.Areas = areas;
            trunk.Stamp = stamp;
        }

        trunk.Points = points;
        trunk.Areas = areas;
        trunk.Cells = cells;
        trunk.Main |= course.Main;
        trunk.MaxWidth = Mathf.Max(trunk.MaxWidth, course.MaxWidth);
    }

    private static T[] Join<T>(T[] first, int firstCount, T filler, T[] second, int from, int count)
    {
        var result = new T[firstCount + count];

        for (int i = 0; i < firstCount; i++)
            result[i] = first != null && i < first.Length ? first[i] : filler;

        for (int i = 0; i < count; i++)
            result[firstCount + i] = second != null && from + i < second.Length ? second[from + i] : filler;

        return result;
    }

    private void DropStubs(List<RiverCourse> courses)
    {
        var fed = new bool[courses.Count];

        foreach (RiverCourse course in courses)
        {
            if (course.JoinPath >= 0 && course.JoinPath < courses.Count && course.Points.Count >= 2)
                fed[course.JoinPath] = true;
        }

        for (int c = 0; c < courses.Count; c++)
        {
            RiverCourse course = courses[c];

            if (fed[c] || course.JoinPath < 0 || course.Points.Count < 2 || course.Stamp is { Parent: >= 0 })
                continue;

            if (Length(course.Points) >= Mathf.Max(STUB_LENGTH, STUB_WIDTHS * Width(course.Areas[^1], course.MaxWidth)))
                continue;

            Log.Note(HydrologyAction.StubDropped, c, course.Points[0], $"a {Length(course.Points):0} m tributary of course {course.JoinPath} feeding nothing");
            Truncate(course, 0);
            StubCourses++;
        }
    }

    private static float Length(List<Vector2> points)
    {
        float length = 0f;

        for (int i = 1; i < points.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);

        return length;
    }

    private static void Widen(List<RiverCourse> courses, RiverPath[] shapedPaths)
    {
        for (int c = courses.Count - 1; c >= 0; c--)
        {
            RiverCourse course = courses[c];

            if (course.JoinPath < 0 || course.JoinPath >= c || shapedPaths[c] == null || shapedPaths[course.JoinPath] == null)
                continue;

            List<RiverPoint> tributary = shapedPaths[c].Points;
            List<RiverPoint> trunk = shapedPaths[course.JoinPath].Points;
            float width = tributary[^1].Width;

            for (int i = NearestPoint(shapedPaths[course.JoinPath], tributary[^1].Position); i < trunk.Count; i++)
            {
                RiverPoint point = trunk[i];

                if (point.Width >= width)
                    continue;

                point.Width = width;
                trunk[i] = point;
            }
        }
    }

    private static void CarryFlow(List<RiverCourse> courses)
    {
        for (int c = courses.Count - 1; c >= 0; c--)
        {
            RiverCourse course = courses[c];

            if (course.JoinPath < 0 || course.JoinPath >= c || course.Points.Count < 2)
                continue;

            RiverCourse trunk = courses[course.JoinPath];

            if (trunk.Points.Count < 2)
                continue;

            float flow = course.Areas[^1];

            for (int i = NearestIndex(trunk.Points, course.Points[^1]); i < trunk.Areas.Count; i++)
                trunk.Areas[i] = Mathf.Max(trunk.Areas[i], flow);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterMap
{
    public const float RIVER_UNDERLAP = 0.03f;

    public bool RiverOpen(RiverPoint point)
    {
        if (point.Submerged)
            return false;

        return !StandingAt(point.Position.x, point.Position.y, out float surface) || surface < point.Surface - RIVER_UNDERLAP;
    }

    public bool TryRiver(float x, float z, out WaterSample sample)
    {
        sample = default;

        if (Rivers.Count == 0)
            return false;

        RiverSegmentIndex lookup = Index();

        if (!lookup.TryGet(x, z, out List<int> near))
            return false;

        var point = new Vector2(x, z);
        float best = float.MaxValue;
        int owner = int.MaxValue;

        foreach (int segment in near)
        {
            (int river, int index) = lookup.Segment(segment);
            List<RiverPoint> points = Rivers[river].Points;

            RiverPoint a = points[index];
            RiverPoint b = points[index + 1];

            Vector2 axis = b.Position - a.Position;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);

            Vector2 closest = a.Position + axis * t;
            float half = 0.5f * Mathf.Lerp(a.Width, b.Width, t) + WaterMeshes.RIBBON_PAD;
            float distance = (point - closest).sqrMagnitude;

            if (distance > half * half)
                continue;

            bool openA = OpenNear(points, index);
            bool openB = OpenNear(points, index + 1);

            if (!openA && !openB || t <= 0f && (!openA || index == 0) || t >= 1f && (!openB || index + 2 == points.Count))
                continue;

            float share = distance / Mathf.Max(half * half, 1e-6f);

            if (river > owner || river == owner && share >= best)
                continue;

            owner = river;
            best = share;
            sample = new WaterSample
            {
                Kind = WaterKind.River,
                Surface = Mathf.Lerp(a.Surface, b.Surface, t),
                Width = Mathf.Lerp(a.Width, b.Width, t),
                Flow = length < 1e-8f ? Vector2.zero : axis / Mathf.Sqrt(length),
                Body = -1
            };
        }

        return best < float.MaxValue;
    }

    private bool OpenNear(List<RiverPoint> points, int i)
    {
        for (int k = Mathf.Max(0, i - WaterMeshes.MOUTH_REACH); k <= Mathf.Min(points.Count - 1, i + WaterMeshes.MOUTH_REACH); k++)
        {
            if (RiverOpen(points[k]))
                return true;
        }

        return false;
    }

    public bool InsideOtherRiver(int river, Vector2 point, float pad)
    {
        return InsideRiver(river, point, pad, false, false, out _);
    }

    public bool InsideDrawnEarlierRiver(int river, Vector2 point, float pad, out float surface)
    {
        return InsideRiver(river, point, pad, true, true, out surface);
    }

    public bool InsideEarlierRiver(int river, Vector2 point, float pad, out float surface)
    {
        return InsideRiver(river, point, pad, true, false, out surface);
    }

    private bool InsideRiver(int river, Vector2 point, float pad, bool earlierOnly, bool drawnOnly, out float surface)
    {
        surface = float.NegativeInfinity;

        if (Rivers.Count == 0)
            return false;

        RiverSegmentIndex lookup = Index();

        if (!lookup.TryGet(point.x, point.y, out List<int> near))
            return false;

        foreach (int segment in near)
        {
            (int other, int index) = lookup.Segment(segment);

            if (earlierOnly ? other >= river : other == river)
                continue;

            List<RiverPoint> points = Rivers[other].Points;
            if (drawnOnly && !OpenNear(points, index) && !OpenNear(points, index + 1))
                continue;

            RiverPoint a = points[index];
            RiverPoint b = points[index + 1];

            Vector2 axis = b.Position - a.Position;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);
            float half = 0.5f * Mathf.Lerp(a.Width, b.Width, t) + pad;

            if ((point - (a.Position + axis * t)).sqrMagnitude > half * half)
                continue;

            surface = Mathf.Max(surface, Mathf.Lerp(a.Surface, b.Surface, t));
        }

        return !float.IsNegativeInfinity(surface);
    }

    public bool CrossesRiver(Vector2 from, Vector2 to, out float width, out Vector2 flow)
    {
        return CrossesRiver(from, to, out width, out flow, out _, out _);
    }

    public bool CrossesRiver(Vector2 from, Vector2 to, out float width, out Vector2 flow, out Vector2 point, out float surface)
    {
        width = 0f;
        flow = Vector2.zero;
        point = Vector2.zero;
        surface = 0f;

        if (Rivers.Count == 0)
            return false;

        float span = Vector2.Distance(from, to);

        if (Remote(CellIndex(from.x, from.y), span) && Remote(CellIndex(to.x, to.y), span))
            return false;

        RiverSegmentIndex lookup = Index();

        Vector2 min = Vector2.Min(from, to);
        Vector2 max = Vector2.Max(from, to);

        int minX = RiverSegmentIndex.Cell(min.x), maxX = RiverSegmentIndex.Cell(max.x);
        int minZ = RiverSegmentIndex.Cell(min.y), maxZ = RiverSegmentIndex.Cell(max.y);

        for (int cz = minZ; cz <= maxZ; cz++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!lookup.TryGetCell(cx, cz, out List<int> near))
                    continue;

                foreach (int segment in near)
                {
                    (int river, int index) = lookup.Segment(segment);
                    RiverPoint a = Rivers[river].Points[index];
                    RiverPoint b = Rivers[river].Points[index + 1];

                    if (a.Submerged && b.Submerged)
                        continue;

                    if (!Intersects(from, to, a.Position, b.Position, out float t))
                        continue;

                    float w = Mathf.Lerp(a.Width, b.Width, t);

                    if (w <= width)
                        continue;

                    width = w;
                    flow = (b.Position - a.Position).normalized;
                    point = Vector2.Lerp(a.Position, b.Position, t);
                    surface = Mathf.Lerp(a.Surface, b.Surface, t);
                }
            }
        }

        return width > 0f;
    }

    public int FindCrossings(IReadOnlyList<Road> roads)
    {
        const float SEPARATION = 16f;

        Crossings.Clear();

        foreach (Road road in roads)
        {
            Vector2[] points = road.Points;
            Vector2 last = new(float.MaxValue, float.MaxValue);

            for (int i = 0; i + 1 < points.Length; i++)
            {
                if (!CrossesRiver(points[i], points[i + 1], out float width, out Vector2 flow, out Vector2 point, out float surface))
                    continue;

                if ((point - last).sqrMagnitude < SEPARATION * SEPARATION)
                    continue;

                last = point;

                Crossings.Add(new WaterCrossing
                {
                    Position = point,
                    Direction = (points[i + 1] - points[i]).normalized,
                    Flow = flow,
                    RiverWidth = width,
                    WaterHeight = surface,
                    RoadKind = road.Kind
                });
            }
        }

        return Crossings.Count;
    }

    public void RemoveRivers(ICollection<int> removed)
    {
        if (removed.Count == 0)
            return;

        var remap = new int[Rivers.Count];
        int kept = 0;

        for (int river = 0; river < Rivers.Count; river++)
            remap[river] = removed.Contains(river) ? -1 : kept++;

        for (int river = Rivers.Count - 1; river >= 0; river--)
        {
            if (remap[river] >= 0)
                continue;

            Rivers.RemoveAt(river);

            if (CarveReach != null && river < CarveReach.Count)
                CarveReach.RemoveAt(river);

            if (Stamps != null && river < Stamps.Tracks.Count)
                Stamps.Tracks.RemoveAt(river);
        }

        foreach (RiverPath river in Rivers)
            river.Parent = river.Parent >= 0 && river.Parent < remap.Length ? remap[river.Parent] : -1;

        InvalidateIndex();
    }

    public void InvalidateIndex()
    {
        System.Threading.Volatile.Write(ref _index, null);
    }

    private RiverSegmentIndex Index()
    {
        RiverSegmentIndex index = System.Threading.Volatile.Read(ref _index);

        if (index != null)
            return index;

        lock (_indexGate)
        {
            index = _index;

            if (index != null)
                return index;

            index = new RiverSegmentIndex(Rivers);
            System.Threading.Volatile.Write(ref _index, index);

            return index;
        }
    }

    private static bool Intersects(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float along)
    {
        along = 0f;

        Vector2 r = p2 - p;
        Vector2 s = q2 - q;

        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-9f)
            return false;

        Vector2 offset = q - p;

        float t = (offset.x * s.y - offset.y * s.x) / denominator;
        float u = (offset.x * r.y - offset.y * r.x) / denominator;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        along = u;
        return true;
    }
}

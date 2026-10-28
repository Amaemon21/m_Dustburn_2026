using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float MARKING_STEP = 4f;
    private const float MARKING_FADE_START = 2f;
    private const float MARKING_FADE_END = 10f;
    private const float EDGE_PROBE = 1f;
    private const float EDGE_FADE_START = -0.5f;
    private const float EDGE_FADE_END = 1f;
    private const float OWN_CHAIN_REACH = 40f;
    private const float MARKING_TOLERANCE = 0.05f;
    private const float ALONG_TOLERANCE = 0.02f;
    private const float MIN_MARKING_PIECE = 0.5f;
    private const int MAX_MARKING_SPLITS = 10;

    private void Emit(List<Vertex> polygon, Segment segment, Geometry geometry, Scratch scratch)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (Vertex vertex in polygon)
        {
            float along = Vector2.Dot(vertex.Point - segment.From, segment.Direction);
            min = Mathf.Min(min, along);
            max = Mathf.Max(max, along);
        }

        for (float start = Mathf.FloorToInt(min / MARKING_STEP) * MARKING_STEP; start < max; start += MARKING_STEP)
        {
            Vector2 low = segment.From + segment.Direction * start;
            Vector2 high = low + segment.Direction * MARKING_STEP;
            List<Vertex> ahead = scratch.Polygon();
            Clip(polygon, low + segment.Side, low, true, ahead);
            List<Vertex> piece = scratch.Polygon();
            Clip(ahead, high, high + segment.Side, true, piece);
            scratch.Release(ahead);
            EmitPiece(piece, segment, geometry, scratch);
            scratch.Release(piece);
        }
    }

    private void EmitPiece(List<Vertex> polygon, Segment segment, Geometry geometry, Scratch scratch)
    {
        if (Area(polygon) <= MIN_AREA)
            return;

        if (WaterCrossings.OverSpan(_water, Centroid(polygon)))
            return;

        EmitMarked(polygon, segment, geometry, 0, scratch);
    }

    private void EmitMarked(List<Vertex> polygon, Segment segment, Geometry geometry, int depth, Scratch scratch)
    {
        if (Area(polygon) <= MIN_AREA)
            return;

        List<Vector3> marks = scratch.Marks;
        marks.Clear();

        for (int i = 0; i < polygon.Count; i++)
            marks.Add(Marks(polygon[i].Point, segment));

        if (depth < MAX_MARKING_SPLITS && Split(polygon, marks, segment, out Vector2 from, out Vector2 to))
        {
            List<Vertex> half = scratch.Polygon();
            Clip(polygon, from, to, true, half);
            EmitMarked(half, segment, geometry, depth + 1, scratch);
            half.Clear();
            Clip(polygon, to, from, true, half);
            EmitMarked(half, segment, geometry, depth + 1, scratch);
            scratch.Release(half);
            return;
        }

        int start = geometry.Vertices.Count;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vertex vertex = polygon[i];
            Vector3 frame = Frame(vertex.Point, segment);
            geometry.Vertices.Add(vertex.Position);
            geometry.Normals.Add(vertex.Normal);
            Vector2 tangent = Tangent(vertex.Point, segment);
            geometry.Tangents.Add(new Vector3(tangent.x, 0f, tangent.y));
            geometry.Uv.Add(new Vector2(frame.x / frame.z, frame.y));
            geometry.Profile.Add(new Vector2(frame.z, segment.Style));
            geometry.Markings.Add(marks[i]);
        }

        for (int i = 1; i < polygon.Count - 1; i++)
        {
            if (Mathf.Abs(Cross(polygon[i].Point - polygon[0].Point, polygon[i + 1].Point - polygon[0].Point)) <= MIN_AREA)
                continue;

            geometry.Triangles.Add(start);
            geometry.Triangles.Add(start + i);
            geometry.Triangles.Add(start + i + 1);
        }
    }

    private bool Split(List<Vertex> polygon, List<Vector3> marks, Segment segment, out Vector2 from, out Vector2 to)
    {
        from = default;
        to = default;
        float minAlong = float.MaxValue, maxAlong = float.MinValue;
        float minSide = float.MaxValue, maxSide = float.MinValue;

        foreach (Vertex vertex in polygon)
        {
            Vector2 relative = vertex.Point - segment.From;
            float along = Vector2.Dot(relative, segment.Direction);
            float across = Vector2.Dot(relative, segment.Side);
            minAlong = Mathf.Min(minAlong, along);
            maxAlong = Mathf.Max(maxAlong, along);
            minSide = Mathf.Min(minSide, across);
            maxSide = Mathf.Max(maxSide, across);
        }

        if (Mathf.Max(maxAlong - minAlong, maxSide - minSide) <= MIN_MARKING_PIECE || !Deviates(polygon, marks, segment))
            return false;

        if (maxAlong - minAlong >= maxSide - minSide)
        {
            Vector2 middle = segment.From + segment.Direction * ((minAlong + maxAlong) * 0.5f);
            from = middle + segment.Side;
            to = middle;
            return true;
        }

        Vector2 centre = segment.From + segment.Side * ((minSide + maxSide) * 0.5f);
        from = centre;
        to = centre + segment.Direction;
        return true;
    }

    private bool Deviates(List<Vertex> polygon, List<Vector3> marks, Segment segment)
    {
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            if (Deviates(polygon, marks, segment, 0, i, i + 1))
                return true;
        }

        return false;
    }

    private bool Deviates(List<Vertex> polygon, List<Vector3> marks, Segment segment, int a, int b, int c)
    {
        Vector2 first = polygon[a].Point, second = polygon[b].Point, third = polygon[c].Point;
        float along = (Frame(first, segment).y + Frame(second, segment).y + Frame(third, segment).y) / 3f;

        if (Bends(first, second, segment) || Bends(second, third, segment) || Bends(third, first, segment)
            || Mathf.Abs(Frame((first + second + third) / 3f, segment).y - along) > ALONG_TOLERANCE)
            return true;

        if (segment.Fillet != null)
            return Widens(first, second, segment) || Widens(second, third, segment) || Widens(third, first, segment);

        if (segment.Neighbours.Length == 0 && segment.Fillets.Length == 0)
            return false;

        return Differs(Marks((first + second) * 0.5f, segment), (marks[a] + marks[b]) * 0.5f)
            || Differs(Marks((second + third) * 0.5f, segment), (marks[b] + marks[c]) * 0.5f)
            || Differs(Marks((third + first) * 0.5f, segment), (marks[c] + marks[a]) * 0.5f)
            || Differs(Marks((first + second + third) / 3f, segment), (marks[a] + marks[b] + marks[c]) / 3f);
    }

    private static bool Bends(Vector2 from, Vector2 to, Segment segment)
    {
        float middle = Frame((from + to) * 0.5f, segment).y;
        return Mathf.Abs(middle - (Frame(from, segment).y + Frame(to, segment).y) * 0.5f) > ALONG_TOLERANCE;
    }

    private static bool Widens(Vector2 from, Vector2 to, Segment segment)
    {
        float middle = Frame((from + to) * 0.5f, segment).x;
        return Mathf.Abs(middle - (Frame(from, segment).x + Frame(to, segment).x) * 0.5f) > ALONG_TOLERANCE;
    }

    private static bool Differs(Vector3 first, Vector3 second)
    {
        Vector3 delta = first - second;
        return Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z))) > MARKING_TOLERANCE;
    }

    private static Vector2 Centroid(List<Vertex> polygon)
    {
        Vector2 centroid = Vector2.zero;

        foreach (Vertex vertex in polygon)
            centroid += vertex.Point;

        return centroid / polygon.Count;
    }

    private static Vector2 Tangent(Vector2 point, Segment segment)
    {
        return segment.Fillet == null ? segment.Side : (segment.Fillet.Centre - point).normalized;
    }

    private static Vector3 Frame(Vector2 point, Segment segment)
    {
        if (segment.Fillet != null)
            return FilletFrame(point, segment.Fillet);

        Vector2 relative = point - segment.From;
        float start = Vector2.Dot(relative, segment.StartNormal);
        float span = start + Vector2.Dot(point - segment.To, segment.EndNormal);
        float t = span > segment.Length * 0.25f ? start / span : Vector2.Dot(relative, segment.Direction) / segment.Length;

        return new Vector3(Vector2.Dot(relative, segment.Side), segment.Along + t * segment.Length, Mathf.Lerp(segment.StartWidth, segment.EndWidth, t));
    }

    private Vector3 Marks(Vector2 point, Segment owner)
    {
        return Marks(point, owner, Frame(point, owner));
    }

    private Vector3 Marks(Vector2 point, Segment owner, Vector3 frame)
    {
        if (owner.Neighbours.Length == 0 && owner.Fillets.Length == 0)
            return Vector3.one;

        Vector2 axis = point - owner.Side * frame.x;
        Vector2 reach = owner.Side * (frame.z + EDGE_PROBE);

        return new Vector3(Fade(point, owner, MARKING_FADE_START, MARKING_FADE_END, false), Fade(axis + reach, owner, EDGE_FADE_START, EDGE_FADE_END, true),
            Fade(axis - reach, owner, EDGE_FADE_START, EDGE_FADE_END, true));
    }

    private float Fade(Vector2 point, Segment owner, float from, float to, bool fillets)
    {
        float gap = float.MaxValue;

        foreach (int index in owner.Neighbours)
            gap = Mathf.Min(gap, AxisGap(point, _segments[index]));

        if (fillets)
        {
            foreach (int index in owner.Fillets)
                gap = Mathf.Min(gap, FilletGap(point, _fillets[index]));
        }

        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, gap));
    }

    private static float AxisGap(Vector2 point, Segment other)
    {
        float along = Mathf.Clamp(Vector2.Dot(point - other.From, other.Direction), 0f, other.Length);
        float width = Mathf.Lerp(other.StartWidth, other.EndWidth, along / other.Length);

        return (point - (other.From + other.Direction * along)).magnitude - width;
    }
}

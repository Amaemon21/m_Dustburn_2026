using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float FILLET_RADIUS = 1.5f;
    private const float FILLET_DEPTH = 0.6f;
    private const float FILLET_MIN_TURN = 20f;
    private const float FILLET_MAX_TURN = 160f;
    private const float FILLET_STEP = 10f;
    private const float MIN_FILLET = 0.5f;

    private sealed class Fillet
    {
        public Vector2 Centre;
        public float Radius;
        public float Width;
        public Vector2 Start;
        public Vector2 End;
        public float Offset;
        public Vector2 Corner;
        public Vector2 FirstNormal;
        public Vector2 SecondNormal;
        public float FirstReach;
        public float SecondReach;
        public int FirstChain;
        public int SecondChain;
        public Vector2 Min;
        public Vector2 Max;
    }

    private void Round()
    {
        var junctions = new List<Junction>();
        Tees(junctions);
        Crossings(junctions);

        foreach (Junction junction in junctions)
        {
            List<Arm> arms = junction.Arms;
            arms.Sort((first, second) => Mathf.Atan2(first.Direction.y, first.Direction.x).CompareTo(Mathf.Atan2(second.Direction.y, second.Direction.x)));

            for (int i = 0; i < arms.Count && arms.Count > 1; i++)
                Fill(arms[i], arms[(i + 1) % arms.Count]);
        }

        for (int index = 0; index < _fillets.Count; index++)
            Attach(index);
    }

    private void Fill(Arm first, Arm second)
    {
        float turn = Mathf.Repeat(Mathf.Atan2(second.Direction.y, second.Direction.x) - Mathf.Atan2(first.Direction.y, first.Direction.x), Mathf.PI * 2f) * Mathf.Rad2Deg;

        if (turn < FILLET_MIN_TURN || turn > FILLET_MAX_TURN)
            return;

        Vector2 firstEdge = first.Origin + Perp(first.Direction) * first.Width;
        Vector2 secondEdge = second.Origin - Perp(second.Direction) * second.Width;
        float sine = Cross(first.Direction, second.Direction);
        float onFirst = Cross(secondEdge - firstEdge, second.Direction) / sine;
        float onSecond = Cross(secondEdge - firstEdge, first.Direction) / sine;

        if (onFirst < -first.Width || onSecond < -second.Width)
            return;

        float half = turn * 0.5f * Mathf.Deg2Rad;
        float tangent = Mathf.Tan(half);
        float width = (first.Width + second.Width) * 0.5f;
        float radius = FILLET_RADIUS * Mathf.Min(first.Width, second.Width);
        radius = Mathf.Min(radius, FILLET_DEPTH * width / (1f / Mathf.Sin(half) - 1f));
        radius = Mathf.Min(radius, (first.Reach - Mathf.Max(0f, onFirst) - ARM_MARGIN) * tangent);
        radius = Mathf.Min(radius, (second.Reach - Mathf.Max(0f, onSecond) - ARM_MARGIN) * tangent);

        if (radius < MIN_FILLET)
            return;

        Vector2 corner = firstEdge + first.Direction * onFirst;
        float length = radius / tangent;
        Vector2 centre = corner + (first.Direction + second.Direction).normalized * (radius / Mathf.Sin(half));
        Vector2 start = (corner + second.Direction * length - centre) / radius;
        Vector2 end = (corner + first.Direction * length - centre) / radius;

        if (Cross(start, end) < 0f)
            (start, end) = (end, start);

        var fillet = new Fillet
        {
            Centre = centre, Radius = radius, Width = width, Start = start.normalized, End = end.normalized, Offset = corner.x + corner.y, Corner = corner,
            FirstNormal = -Perp(first.Direction), SecondNormal = Perp(second.Direction), FirstReach = 2f * first.Width, SecondReach = 2f * second.Width,
            FirstChain = first.Chain, SecondChain = second.Chain
        };

        Fan(fillet, (180f - turn) * Mathf.Deg2Rad);
        _fillets.Add(fillet);
    }

    private void Fan(Fillet fillet, float sweep)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(sweep * Mathf.Rad2Deg / FILLET_STEP));
        var arc = new Vector2[steps + 1];

        for (int k = 0; k <= steps; k++)
        {
            float angle = sweep * k / steps;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            arc[k] = fillet.Centre + new Vector2(fillet.Start.x * cos - fillet.Start.y * sin, fillet.Start.x * sin + fillet.Start.y * cos) * fillet.Radius;
        }

        bool clockwise = Cross(arc[0] - fillet.Corner, arc[1] - fillet.Corner) < 0f;
        fillet.Min = fillet.Max = fillet.Corner;

        for (int k = 0; k < steps; k++)
        {
            Vector2 first = arc[clockwise ? k + 1 : k];
            Vector2 second = arc[clockwise ? k : k + 1];
            Vector2 direction = (arc[k + 1] - arc[k]).normalized;
            var segment = new Segment
            {
                From = fillet.Corner, To = (first + second) * 0.5f, Direction = direction, Side = Perp(direction), Length = (arc[k + 1] - arc[k]).magnitude,
                StartWidth = fillet.Width, EndWidth = fillet.Width, Chain = -1, Style = 0, Fillet = fillet, Neighbours = System.Array.Empty<int>(),
                Boundary = new[] { fillet.Corner, first, second }
            };
            segment.Min = Vector2.Min(fillet.Corner, Vector2.Min(first, second));
            segment.Max = Vector2.Max(fillet.Corner, Vector2.Max(first, second));
            fillet.Min = Vector2.Min(fillet.Min, segment.Min);
            fillet.Max = Vector2.Max(fillet.Max, segment.Max);
            Add(segment);
        }
    }

    private void Attach(int index)
    {
        Fillet fillet = _fillets[index];
        var slack = new Vector2(EDGE_PROBE + EDGE_FADE_END + MIN_FILLET, EDGE_PROBE + EDGE_FADE_END + MIN_FILLET);
        var candidates = new List<int>();
        Nearby(fillet.Min - slack, fillet.Max + slack, candidates);

        foreach (int candidate in candidates)
        {
            Segment segment = _segments[candidate];

            if (segment.Fillet != null || (segment.Chain != fillet.FirstChain && segment.Chain != fillet.SecondChain))
                continue;

            if (segment.Max.x < fillet.Min.x - slack.x || segment.Min.x > fillet.Max.x + slack.x || segment.Max.y < fillet.Min.y - slack.y || segment.Min.y > fillet.Max.y + slack.y)
                continue;

            int[] known = segment.Fillets;
            System.Array.Resize(ref known, known.Length + 1);
            known[^1] = index;
            segment.Fillets = known;
        }
    }

    private static Vector3 FilletFrame(Vector2 point, Fillet fillet)
    {
        Vector2 offset = point - fillet.Centre;
        float angle = Mathf.Atan2(Cross(fillet.Start, offset), Vector2.Dot(fillet.Start, offset));

        return new Vector3(fillet.Width - (offset.magnitude - fillet.Radius), fillet.Offset + angle * fillet.Radius, fillet.Width);
    }

    private static float FilletGap(Vector2 point, Fillet fillet)
    {
        Vector2 offset = point - fillet.Centre;

        if (Cross(fillet.Start, offset) < 0f || Cross(offset, fillet.End) < 0f)
            return Mathf.Min((offset - fillet.Start * fillet.Radius).magnitude, (offset - fillet.End * fillet.Radius).magnitude);

        float distance = offset.magnitude;

        if (distance > fillet.Radius && (Vector2.Dot(point - fillet.Corner, fillet.FirstNormal) >= fillet.FirstReach || Vector2.Dot(point - fillet.Corner, fillet.SecondNormal) >= fillet.SecondReach))
            return float.MaxValue;

        return fillet.Radius - distance;
    }
}

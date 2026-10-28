using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float ARM_SPAN = 8f;
    private const float ARM_MARGIN = 0.5f;
    private const float JUNCTION_MERGE = 2f;
    private const float SAME_ARM = 0.98f;

    private struct Arm
    {
        public Vector2 Origin;
        public Vector2 Direction;
        public float Width;
        public float Reach;
        public int Chain;
    }

    private sealed class Junction
    {
        public Vector2 Point;
        public readonly List<Arm> Arms = new();
    }

    private void Tees(List<Junction> junctions)
    {
        var candidates = new List<int>();

        for (int position = 0; position < _chains.Count; position++)
        {
            Chain chain = _chains[position];

            if (chain.Closed)
                continue;

            foreach (bool start in new[] { true, false })
            {
                Vector2 end = start ? chain.Points[0] : chain.Points[^1];
                var slack = new Vector2(TEE_SLACK + MIN_SEGMENT, TEE_SLACK + MIN_SEGMENT);
                Nearby(end - slack, end + slack, candidates);
                Junction junction = null;

                foreach (int other in Closest(end, position, candidates))
                {
                    Segment segment = _segments[other];
                    float along = segment.Along + Mathf.Clamp(Vector2.Dot(end - segment.From, segment.Direction), 0f, segment.Length);
                    junction ??= Join(junctions, segment.From + segment.Direction * (along - segment.Along));
                    AddArms(junction, segment.Chain, along);
                }

                if (junction != null)
                    AddArms(junction, position, start ? 0f : chain.Length);
            }
        }
    }

    private IEnumerable<int> Closest(Vector2 end, int chain, List<int> candidates)
    {
        var best = new Dictionary<int, (int Segment, float Distance)>();

        foreach (int index in candidates)
        {
            Segment segment = _segments[index];

            if (segment.Fillet != null || segment.Chain == chain || AxisGap(end, segment) > TEE_SLACK)
                continue;

            float distance = PointDistance(end, segment);

            if (!best.TryGetValue(segment.Chain, out var known) || distance < known.Distance)
                best[segment.Chain] = (index, distance);
        }

        var result = new List<int>();

        foreach (var entry in best.Values)
            result.Add(entry.Segment);

        result.Sort();
        return result;
    }

    private void Crossings(List<Junction> junctions)
    {
        var candidates = new List<int>();
        int count = _segments.Count;

        for (int first = 0; first < count; first++)
        {
            Segment one = _segments[first];
            Nearby(Vector2.Min(one.From, one.To), Vector2.Max(one.From, one.To), candidates);

            foreach (int second in candidates)
            {
                Segment two = _segments[second];

                if (second <= first || two.Chain == one.Chain || !Intersect(one, two, out float alongOne, out float alongTwo))
                    continue;

                if (NearEnd(one.Chain, alongOne, two.Chain) || NearEnd(two.Chain, alongTwo, one.Chain))
                    continue;

                Junction junction = Join(junctions, one.From + one.Direction * (alongOne - one.Along));
                AddArms(junction, one.Chain, alongOne);
                AddArms(junction, two.Chain, alongTwo);
            }
        }
    }

    private static bool Intersect(Segment one, Segment two, out float alongOne, out float alongTwo)
    {
        alongOne = alongTwo = 0f;
        float denominator = Cross(one.Direction, two.Direction);

        if (Mathf.Abs(denominator) < 0.001f)
            return false;

        Vector2 delta = two.From - one.From;
        float t = Cross(delta, two.Direction) / denominator;
        float u = Cross(delta, one.Direction) / denominator;

        if (t < -0.001f || t > one.Length + 0.001f || u < -0.001f || u > two.Length + 0.001f)
            return false;

        alongOne = one.Along + Mathf.Clamp(t, 0f, one.Length);
        alongTwo = two.Along + Mathf.Clamp(u, 0f, two.Length);
        return true;
    }

    private bool NearEnd(int chain, float along, int other)
    {
        Chain own = _chains[chain];

        if (own.Closed)
            return false;

        float reach = MaxWidth(_chains[other]) + MaxWidth(own) + TEE_SLACK;
        return along < reach || own.Length - along < reach;
    }

    private static float MaxWidth(Chain chain)
    {
        float width = 0f;

        foreach (float value in chain.Widths)
            width = Mathf.Max(width, value);

        return width;
    }

    private static Junction Join(List<Junction> junctions, Vector2 point)
    {
        foreach (Junction junction in junctions)
        {
            if ((junction.Point - point).sqrMagnitude <= JUNCTION_MERGE * JUNCTION_MERGE)
                return junction;
        }

        var created = new Junction { Point = point };
        junctions.Add(created);
        return created;
    }

    private void AddArms(Junction junction, int chain, float along)
    {
        Chain own = _chains[chain];
        AddArm(junction, own, chain, along, 1f);
        AddArm(junction, own, chain, along, -1f);
    }

    private void AddArm(Junction junction, Chain chain, int position, float along, float sign)
    {
        float reach = chain.Closed ? chain.Length * 0.5f : sign > 0f ? chain.Length - along : along;
        float width = WidthAt(chain, along);

        if (reach < width)
            return;

        Vector2 origin = PointAt(chain, along);
        Vector2 chord = PointAt(chain, along + sign * Mathf.Min(ARM_SPAN, reach)) - origin;

        if (chord.sqrMagnitude < MIN_SEGMENT * MIN_SEGMENT)
            return;

        var arm = new Arm { Origin = origin, Direction = chord.normalized, Width = width, Reach = reach, Chain = position };

        for (int i = 0; i < junction.Arms.Count; i++)
        {
            Arm known = junction.Arms[i];

            if (known.Chain != position || Vector2.Dot(known.Direction, arm.Direction) < SAME_ARM)
                continue;

            if (arm.Reach > known.Reach)
                junction.Arms[i] = arm;

            return;
        }

        junction.Arms.Add(arm);
    }

    private static Vector2 PointAt(Chain chain, float along)
    {
        if (chain.Closed)
            along = Mathf.Repeat(along, chain.Length);

        foreach (Segment segment in chain.Segments)
        {
            if (along <= segment.Along + segment.Length)
                return segment.From + segment.Direction * Mathf.Max(0f, along - segment.Along);
        }

        return chain.Points[^1];
    }

    private static float WidthAt(Chain chain, float along)
    {
        if (chain.Closed)
            along = Mathf.Repeat(along, chain.Length);

        foreach (Segment segment in chain.Segments)
        {
            if (along <= segment.Along + segment.Length)
                return Mathf.Lerp(segment.StartWidth, segment.EndWidth, Mathf.Clamp01((along - segment.Along) / segment.Length));
        }

        return chain.Widths[^1];
    }

    private void Nearby(Vector2 min, Vector2 max, List<int> output)
    {
        output.Clear();
        var seen = new HashSet<int>();

        for (int z = Cell(min.y); z <= Cell(max.y); z++)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                if (!_cells.TryGetValue((x, z), out List<int> items))
                    continue;

                foreach (int index in items)
                {
                    if (seen.Add(index))
                        output.Add(index);
                }
            }
        }

        output.Sort();
    }
}

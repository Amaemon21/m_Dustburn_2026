using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float TEE_SLACK = 0.5f;
    private const float TAIL_SLACK = 1f;
    private const float MITRE_REACH = 1.1f;

    private static void MarkTails(List<Chain> chains)
    {
        var grid = new Dictionary<(int X, int Z), List<int>>();
        var owners = new List<(int Chain, Segment Segment)>();

        for (int chain = 0; chain < chains.Count; chain++)
        {
            foreach (Segment segment in chains[chain].Segments)
            {
                Register(grid, segment.Min, segment.Max, owners.Count);
                owners.Add((chain, segment));
            }
        }

        for (int chain = 0; chain < chains.Count; chain++)
        {
            Chain own = chains[chain];

            if (own.Closed)
                continue;

            float start = Tail(own.Points[0], own.Widths[0], chain, grid, owners);
            float end = Tail(own.Points[^1], own.Widths[^1], chain, grid, owners);

            foreach (Segment segment in own.Segments)
                segment.Tail = segment.Along < start || segment.Along + segment.Length > own.Length - end;
        }
    }

    private static float Tail(Vector2 end, float width, int chain, Dictionary<(int X, int Z), List<int>> grid, List<(int Chain, Segment Segment)> owners)
    {
        if (!grid.TryGetValue((Cell(end.x), Cell(end.y)), out List<int> items))
            return 0f;

        float tail = 0f;

        foreach (int item in items)
        {
            (int other, Segment segment) = owners[item];

            if (other != chain && AxisGap(end, segment) <= TEE_SLACK)
                tail = Mathf.Max(tail, 2f * (Mathf.Max(segment.StartWidth, segment.EndWidth) + width) + TAIL_SLACK);
        }

        return tail;
    }

    private void Publish(List<Chain> chains)
    {
        var order = new int[chains.Count];

        for (int chain = 0; chain < order.Length; chain++)
            order[chain] = chain;

        System.Array.Sort(order, (first, second) => chains[first].Rank != chains[second].Rank ? chains[second].Rank.CompareTo(chains[first].Rank) : first.CompareTo(second));

        var global = new int[chains.Count][];

        foreach (bool tails in new[] { false, true })
        {
            for (int position = 0; position < order.Length; position++)
            {
                Chain chain = chains[order[position]];
                global[position] ??= new int[chain.Segments.Count];

                if (!tails)
                    _chains.Add(chain);

                for (int k = 0; k < chain.Segments.Count; k++)
                {
                    Segment segment = chain.Segments[k];

                    if (segment.Tail != tails)
                        continue;

                    segment.Chain = position;
                    global[position][k] = _segments.Count;
                    Add(segment);
                }
            }
        }

        for (int position = 0; position < order.Length; position++)
        {
            foreach (Segment segment in chains[order[position]].Segments)
            {
                segment.Previous = segment.Previous < 0 ? -1 : global[position][segment.Previous];
                segment.Next = segment.Next < 0 ? -1 : global[position][segment.Next];
            }
        }
    }

    private void Add(Segment segment)
    {
        int index = _segments.Count;
        segment.Hole = new Vector2[segment.Boundary.Length];

        for (int i = 0; i < segment.Boundary.Length; i++)
            segment.Hole[segment.Boundary.Length - 1 - i] = segment.Boundary[i];

        _segments.Add(segment);
        Register(_cells, segment.Min, segment.Max, index);
    }

    private Dictionary<(int X, int Z), List<int>> Influence()
    {
        var influence = new Dictionary<(int X, int Z), List<int>>();

        for (int index = 0; index < _segments.Count; index++)
        {
            Segment segment = _segments[index];
            float reach = Mathf.Max(segment.StartWidth, segment.EndWidth) + MARKING_FADE_END;
            var pad = new Vector2(reach, reach);
            Register(influence, Vector2.Min(segment.From, segment.To) - pad, Vector2.Max(segment.From, segment.To) + pad, index);
        }

        return influence;
    }

    private int[] Neighbours(Segment segment, Dictionary<(int X, int Z), List<int>> influence)
    {
        var slack = new Vector2(EDGE_PROBE, EDGE_PROBE);
        Vector2 min = segment.Min - slack;
        Vector2 max = segment.Max + slack;
        var neighbours = new HashSet<int>();

        for (int z = Cell(min.y); z <= Cell(max.y); z++)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                if (!influence.TryGetValue((x, z), out List<int> items))
                    continue;

                foreach (int index in items)
                {
                    if (Foreign(segment, _segments[index]) && Reaches(segment, _segments[index]))
                        neighbours.Add(index);
                }
            }
        }

        var sorted = new int[neighbours.Count];
        neighbours.CopyTo(sorted);
        System.Array.Sort(sorted);
        return sorted;
    }

    private static bool Reaches(Segment owner, Segment other)
    {
        float reach = MITRE_REACH * Mathf.Max(owner.StartWidth, owner.EndWidth) + EDGE_PROBE + Mathf.Max(other.StartWidth, other.EndWidth) + MARKING_FADE_END;
        return AxisDistance(owner, other) <= reach;
    }

    private static float AxisDistance(Segment first, Segment second)
    {
        if (Crosses(first.From, first.To, second.From, second.To))
            return 0f;

        return Mathf.Min(Mathf.Min(PointDistance(first.From, second), PointDistance(first.To, second)), Mathf.Min(PointDistance(second.From, first), PointDistance(second.To, first)));
    }

    private static float PointDistance(Vector2 point, Segment segment)
    {
        float along = Mathf.Clamp(Vector2.Dot(point - segment.From, segment.Direction), 0f, segment.Length);
        return (point - (segment.From + segment.Direction * along)).magnitude;
    }

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float first = Cross(b - a, c - a), second = Cross(b - a, d - a);
        float third = Cross(d - c, a - c), fourth = Cross(d - c, b - c);
        return first * second < 0f && third * fourth < 0f;
    }

    private bool Foreign(Segment owner, Segment other)
    {
        if (other.Chain != owner.Chain)
            return true;

        float gap = Mathf.Max(other.Along - owner.Along - owner.Length, owner.Along - other.Along - other.Length);
        Chain chain = _chains[owner.Chain];

        if (chain.Closed)
            gap = Mathf.Min(gap, chain.Length - Mathf.Max(owner.Along + owner.Length, other.Along + other.Length) + Mathf.Min(owner.Along, other.Along));

        return gap > OWN_CHAIN_REACH;
    }
}

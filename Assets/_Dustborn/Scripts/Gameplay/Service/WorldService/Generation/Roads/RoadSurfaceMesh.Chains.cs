using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float MIN_SEGMENT = 0.25f;
    private const float JOIN_DISTANCE = 0.5f;
    private const float MAX_JOIN_TURN = 60f;
    private const float TAPER_PER_METRE = 10f;

    private sealed class Strand
    {
        public List<Vector2> Points;
        public float HalfWidth;
        public int Style;
        public readonly int[] Links = { -1, -1 };
    }

    private sealed class Chain
    {
        public List<Vector2> Points = new();
        public List<float> BaseWidths = new();
        public List<int> Styles = new();
        public readonly List<float> Widths = new();
        public readonly List<Segment> Segments = new();
        public bool Closed;
        public float Length;
        public int Rank;
    }

    private void Plan(IReadOnlyList<Road> roads)
    {
        List<Strand> strands = Strands(roads);
        Link(strands);

        List<Chain> chains = Assemble(strands);

        foreach (Chain chain in chains)
        {
            Taper(chain);
            Shape(chain);
        }

        MarkTails(chains);
        Publish(chains);

        Dictionary<(int X, int Z), List<int>> influence = Influence();

        foreach (Segment segment in _segments)
            segment.Neighbours = Neighbours(segment, influence);

        Round();
    }

    private static List<Strand> Strands(IReadOnlyList<Road> roads)
    {
        var strands = new List<Strand>();

        if (roads == null)
            return strands;

        foreach (Road road in roads)
        {
            if (road?.Points == null || road.Points.Length < 2 || road.Width <= 0f || road.Surface != RoadSurface.Asphalt)
                continue;

            List<Vector2> points = Thin(road.Points);

            if (points.Count < 2)
                continue;

            int style = road.Kind == RoadKind.Highway ? 3 : road.Kind == RoadKind.Arterial ? 2 : 1;
            strands.Add(new Strand { Points = points, HalfWidth = road.HalfWidth, Style = style });
        }

        return strands;
    }

    private static List<Vector2> Thin(Vector2[] source)
    {
        var points = new List<Vector2> { source[0] };

        for (int i = 1; i < source.Length; i++)
        {
            if ((source[i] - points[^1]).sqrMagnitude >= MIN_SEGMENT * MIN_SEGMENT)
                points.Add(source[i]);
            else if (i == source.Length - 1 && points.Count > 1)
                points[^1] = source[i];
        }

        return points;
    }

    private static void Link(List<Strand> strands)
    {
        var partners = new int[strands.Count * 2];

        for (int end = 0; end < partners.Length; end++)
            partners[end] = Partner(strands, end);

        for (int end = 0; end < partners.Length; end++)
        {
            int partner = partners[end];

            if (partner < 0 || partners[partner] != end || Turn(Leaving(strands, end), -Leaving(strands, partner)) > MAX_JOIN_TURN)
                continue;

            strands[end / 2].Links[end % 2] = partner;
        }
    }

    private static int Partner(List<Strand> strands, int end)
    {
        Vector2 point = EndPoint(strands, end);
        int partner = -1;

        for (int other = 0; other < strands.Count * 2; other++)
        {
            if (other == end || (EndPoint(strands, other) - point).sqrMagnitude > JOIN_DISTANCE * JOIN_DISTANCE)
                continue;

            if (partner >= 0)
                return -1;

            partner = other;
        }

        return partner;
    }

    private static Vector2 EndPoint(List<Strand> strands, int end)
    {
        List<Vector2> points = strands[end / 2].Points;
        return end % 2 == 0 ? points[0] : points[^1];
    }

    private static Vector2 Leaving(List<Strand> strands, int end)
    {
        List<Vector2> points = strands[end / 2].Points;
        return end % 2 == 0 ? (points[0] - points[1]).normalized : (points[^1] - points[^2]).normalized;
    }

    private static List<Chain> Assemble(List<Strand> strands)
    {
        var chains = new List<Chain>();
        var used = new bool[strands.Count];

        for (int strand = 0; strand < strands.Count; strand++)
        {
            int[] links = strands[strand].Links;

            if (!used[strand] && (links[0] < 0 || links[1] < 0))
                chains.Add(Walk(strands, used, links[0] < 0 ? strand * 2 : strand * 2 + 1));
        }

        for (int strand = 0; strand < strands.Count; strand++)
        {
            if (!used[strand])
                chains.Add(Walk(strands, used, strand * 2));
        }

        return chains;
    }

    private static Chain Walk(List<Strand> strands, bool[] used, int entry)
    {
        var chain = new Chain();
        int end = entry;

        while (true)
        {
            Strand strand = strands[end / 2];
            used[end / 2] = true;
            int count = strand.Points.Count;

            for (int i = chain.Points.Count == 0 ? 0 : 1; i < count; i++)
            {
                chain.Points.Add(strand.Points[end % 2 == 0 ? i : count - 1 - i]);

                if (chain.Points.Count < 2)
                    continue;

                chain.BaseWidths.Add(strand.HalfWidth);
                chain.Styles.Add(strand.Style);
            }

            int next = strand.Links[1 - end % 2];

            if (next < 0)
                return chain;

            if (used[next / 2])
            {
                chain.Closed = chain.Points.Count >= 4;

                if (chain.Closed)
                    chain.Points[^1] = chain.Points[0];

                return chain;
            }

            end = next;
        }
    }

    private static void Taper(Chain chain)
    {
        float[] along = Alongs(chain.Points);
        var tapers = new List<Vector3>();

        for (int k = 1; k < chain.BaseWidths.Count; k++)
        {
            if (Mathf.Abs(chain.BaseWidths[k] - chain.BaseWidths[k - 1]) > 0.001f)
                tapers.Add(new Vector3(along[k], chain.BaseWidths[k - 1], chain.BaseWidths[k]));
        }

        if (tapers.Count > 0)
        {
            Insert(chain, tapers, along);
            along = Alongs(chain.Points);
        }

        chain.Length = along[^1];

        for (int i = 0; i < chain.Points.Count; i++)
            chain.Widths.Add(WidthAt(along[i], chain.BaseWidths[Mathf.Min(i, chain.BaseWidths.Count - 1)], tapers));
    }

    private static void Insert(Chain chain, List<Vector3> tapers, float[] along)
    {
        var cuts = new List<float>();

        foreach (Vector3 taper in tapers)
        {
            float half = TaperHalf(taper);
            cuts.Add(taper.x - half);
            cuts.Add(taper.x + half);
        }

        cuts.Sort();
        var points = new List<Vector2> { chain.Points[0] };
        var widths = new List<float>();
        var styles = new List<int>();
        int cut = 0;

        for (int k = 0; k < chain.Points.Count - 1; k++)
        {
            while (cut < cuts.Count && cuts[cut] <= along[k] + MIN_SEGMENT)
                cut++;

            while (cut < cuts.Count && cuts[cut] < along[k + 1] - MIN_SEGMENT)
            {
                points.Add(Vector2.Lerp(chain.Points[k], chain.Points[k + 1], (cuts[cut] - along[k]) / (along[k + 1] - along[k])));
                widths.Add(chain.BaseWidths[k]);
                styles.Add(chain.Styles[k]);
                cut++;
            }

            points.Add(chain.Points[k + 1]);
            widths.Add(chain.BaseWidths[k]);
            styles.Add(chain.Styles[k]);
        }

        chain.Points = points;
        chain.BaseWidths = widths;
        chain.Styles = styles;
    }

    private static float WidthAt(float along, float width, List<Vector3> tapers)
    {
        foreach (Vector3 taper in tapers)
        {
            float half = TaperHalf(taper);

            if (along >= taper.x - half && along <= taper.x + half)
                return Mathf.Lerp(taper.y, taper.z, (along - taper.x + half) / (2f * half));
        }

        return width;
    }

    private static float TaperHalf(Vector3 taper) => Mathf.Abs(taper.z - taper.y) * TAPER_PER_METRE * 0.5f;

    private static float[] Alongs(List<Vector2> points)
    {
        var along = new float[points.Count];

        for (int i = 1; i < points.Count; i++)
            along[i] = along[i - 1] + (points[i] - points[i - 1]).magnitude;

        return along;
    }
}

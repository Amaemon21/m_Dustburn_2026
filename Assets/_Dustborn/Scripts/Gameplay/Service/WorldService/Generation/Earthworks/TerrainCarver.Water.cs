using System.Collections.Generic;
using UnityEngine;

public partial class TerrainCarver
{
    private bool[] RaiseAboveWater(Vector2[] points, float[] distance, float[] profile, float[] original, float halfWidth, float shoulder, float grade)
    {
        if (_water == null)
            return null;

        bool raised = false;
        bool[] bridged = Bridged(_water, points, distance, halfWidth);

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 tangent = (points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
            var normal = new Vector2(-tangent.y, tangent.x);

            for (int side = -1; side <= 1; side++)
            {
                Vector2 at = points[i] + normal * (side * halfWidth);
                if (!_water.TrySurface(at.x, at.y, out float surface))
                    continue;

                bool bridge = bridged != null && bridged[i];

                float minimum = (surface + (bridge ? BRIDGE_CLEARANCE : CULVERT_CLEARANCE)) / _source.MaxHeight;

                if (minimum <= profile[i])
                    continue;

                profile[i] = minimum;
                raised = true;
            }

            foreach (float offset in new[] { halfWidth + shoulder, halfWidth + shoulder + SHORE_GUARD })
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 at = points[i] + normal * (side * offset);

                    if (!_water.StandingAt(at.x, at.y, out float level))
                        continue;

                    float minimum = (level + SHORE_CLEARANCE) / _source.MaxHeight;

                    if (minimum <= profile[i])
                        continue;

                    profile[i] = minimum;
                    raised = true;
                }
            }
        }

        if (bridged != null)
        {
            LevelDecks(bridged, distance, profile, original);
            raised = true;
        }

        if (!raised)
            return bridged;

        float slope = grade / _source.MaxHeight;

        for (int pass = 0; pass < DECK_PASSES; pass++)
        {
            for (int i = 1; i < profile.Length; i++)
                profile[i] = Mathf.Max(profile[i], profile[i - 1] - slope * (distance[i] - distance[i - 1]));

            for (int i = profile.Length - 2; i >= 0; i--)
                profile[i] = Mathf.Max(profile[i], profile[i + 1] - slope * (distance[i + 1] - distance[i]));

            if (bridged == null || pass + 1 == DECK_PASSES)
                break;

            LevelDecks(bridged, distance, profile, original);
        }

        return bridged;
    }

    private bool[] Bridged(WaterMap water, Vector2[] points, float[] distance, float halfWidth)
    {
        bool[] bridged = null;
        float bridgeWidth = _config.Water.BridgeMinWidth;
        float edge = halfWidth + BridgeMesh.KERB;

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 tangent = (points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
            var normal = new Vector2(-tangent.y, tangent.x);
            bool over = false;

            for (int k = -BRIDGE_TAPS; k <= BRIDGE_TAPS && !over; k++)
            {
                Vector2 at = points[i] + normal * (edge * k / BRIDGE_TAPS);
                over = water.TryRiver(at.x, at.y, out WaterSample sample) && sample.Width >= bridgeWidth
                    && (sample.Flow.sqrMagnitude < 1e-6f || Mathf.Abs(Vector2.Dot(tangent, sample.Flow.normalized)) <= MAX_BRIDGE_COSINE
                        || water.CrossesRiver(points[i] - tangent * (sample.Width + edge), points[i] + tangent * (sample.Width + edge), out _, out _));
            }

            if (!over)
                continue;

            bridged ??= new bool[points.Length];

            for (int j = Reach(distance, i, -BRIDGE_PAD); j <= Reach(distance, i, BRIDGE_PAD); j++)
                bridged[j] = true;
        }

        return bridged;
    }

    private static void LevelDecks(bool[] bridged, float[] distance, float[] profile, float[] original)
    {
        int count = bridged.Length;

        for (int start = 0; start < count; start++)
        {
            if (!bridged[start])
                continue;

            int end = start;

            while (end + 1 < count && bridged[end + 1])
                end++;

            int from = Reach(distance, start, -BRIDGE_APPROACH);
            int to = Reach(distance, end, BRIDGE_APPROACH);
            float deck = Mathf.Max(original[from], original[to]);

            for (int i = from; i <= to; i++)
                deck = Mathf.Max(deck, profile[i]);

            for (int i = from; i <= to; i++)
                profile[i] = deck;

            start = end;
        }
    }

    private static int Reach(float[] distance, int index, float metres)
    {
        float target = distance[index] + metres;
        int step = metres < 0f ? -1 : 1;

        while (index + step >= 0 && index + step < distance.Length && (step < 0 ? distance[index + step] >= target : distance[index + step] <= target))
            index += step;

        return index;
    }

    private void RecordBridges(Road road, Vector2[] points, float[] profile, bool[] bridged, float halfWidth)
    {
        if (bridged == null)
            return;

        for (int start = 0; start < bridged.Length; start++)
        {
            if (!bridged[start])
                continue;

            int end = start;

            while (end + 1 < bridged.Length && bridged[end + 1])
                end++;

            Vector2 from = points[start];
            Vector2 to = points[end];
            if (!_water.CrossesRiver(from, to, out float width, out Vector2 flow, out _, out float surface))
            {
                Vector2 middle = points[(start + end) / 2];

                if (_water.TryRiver(middle.x, middle.y, out WaterSample sample))
                {
                    width = sample.Width;
                    flow = sample.Flow;
                    surface = sample.Surface;
                }
            }

            Bridges.Add(new WaterCrossing
            {
                Position = 0.5f * (from + to),
                Center = 0.5f * (from + to),
                Direction = (to - from).normalized,
                Flow = flow,
                RiverWidth = width,
                WaterHeight = surface,
                RoadKind = road.Kind,
                RoadWidth = 2f * halfWidth,
                Bridge = true,
                Span = Vector2.Distance(from, to),
                DeckHeight = profile[start] * _source.MaxHeight
            });

            start = end;
        }
    }

    private static List<Vector2> Spans(bool[] bridged, float[] distance)
    {
        if (bridged == null)
            return null;

        var spans = new List<Vector2>();

        for (int start = 0; start < bridged.Length; start++)
        {
            if (!bridged[start])
                continue;

            int end = start;

            while (end + 1 < bridged.Length && bridged[end + 1])
                end++;

            spans.Add(new Vector2(distance[start], distance[end]));
            start = end;
        }

        return spans;
    }

    private static bool[] NearSpans(List<Vector2> spans, float[] distance, float halfWidth, float[] skirts)
    {
        if (spans == null || spans.Count == 0)
            return null;

        var near = new bool[distance.Length];

        for (int i = 0; i < distance.Length; i++)
        {
            float reach = halfWidth + skirts[i];

            foreach (Vector2 span in spans)
                near[i] |= distance[i] >= span.x - reach && distance[i] <= span.y + reach;
        }

        return near;
    }

    private static bool UnderDeck(List<Vector2> spans, float along)
    {
        foreach (Vector2 span in spans)
        {
            if (along >= span.x && along <= span.y)
                return true;
        }

        return false;
    }
}

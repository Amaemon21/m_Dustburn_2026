using System.Collections.Generic;
using UnityEngine;

public static class WaterCrossings
{
    private const float STEP = 0.5f;
    private const float PIPE_REACH = 60f;
    private const float PIPE_REACH_BEYOND_ROAD = 24f;
    private const float WATER_EDGE = 0.2f;
    private const float BRIDGE_MATCH = 16f;

    public static void Finish(WaterMap water, HeightMap heights, WorldGenerationConfig config, IReadOnlyList<WaterCrossing> bridges)
    {
        if (water == null)
            return;

        List<WaterCrossing> crossings = water.Crossings;
        crossings.RemoveAll(crossing => Bridged(crossing, bridges));

        for (int i = 0; i < crossings.Count; i++)
        {
            WaterCrossing crossing = crossings[i];
            crossing.RoadWidth = 2f * RoadKindProfile.For(config, crossing.RoadKind).HalfWidth;
            crossing.Bridge = false;
            Culvert(ref crossing, heights);
            crossings[i] = crossing;
        }

        crossings.AddRange(bridges);
    }

    private static bool Bridged(WaterCrossing crossing, IReadOnlyList<WaterCrossing> bridges)
    {
        foreach (WaterCrossing bridge in bridges)
        {
            float reach = bridge.Span * 0.5f + BRIDGE_MATCH;

            if ((crossing.Position - bridge.Center).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
    }

    public static bool OverSpan(WaterMap water, Vector2 point)
    {
        if (water == null)
            return false;

        foreach (WaterCrossing crossing in water.Crossings)
        {
            if (!crossing.Bridge || crossing.Span <= 0f)
                continue;

            Vector2 offset = point - crossing.Center;
            float along = Vector2.Dot(offset, crossing.Direction);
            float across = offset.x * crossing.Direction.y - offset.y * crossing.Direction.x;
            float length = crossing.Span * 0.5f;
            float width = crossing.RoadWidth * 0.5f + BridgeMesh.KERB;

            if (along * along <= length * length && across * across <= width * width)
                return true;
        }

        return false;
    }

    public static bool TryDeck(WaterMap water, Vector2 point, out float deck)
    {
        deck = 0f;

        if (water == null)
            return false;

        foreach (WaterCrossing crossing in water.Crossings)
        {
            if (!crossing.Bridge || crossing.Span <= 0f)
                continue;

            Vector2 offset = point - crossing.Center;
            float along = Vector2.Dot(offset, crossing.Direction);
            float across = offset.x * crossing.Direction.y - offset.y * crossing.Direction.x;
            float length = crossing.Span * 0.5f + BridgeMesh.DECK_OVERLAP;
            float width = crossing.RoadWidth * 0.5f + BridgeMesh.KERB;

            if (along * along > length * length || across * across > width * width)
                continue;

            deck = crossing.DeckHeight + BridgeMesh.DECK_LIFT;
            return true;
        }

        return false;
    }

    private static void Culvert(ref WaterCrossing crossing, HeightMap heights)
    {
        Vector2 flow = crossing.Flow.sqrMagnitude > 1e-6f ? crossing.Flow.normalized : new Vector2(-crossing.Direction.y, crossing.Direction.x);
        float wet = crossing.WaterHeight + WATER_EDGE;

        float back = Walk(heights, crossing.Position, -flow, 0f, PIPE_REACH, height => height < wet);
        float ahead = Walk(heights, crossing.Position, flow, 0f, PIPE_REACH, height => height < wet);
        crossing.Flow = flow;

        float limit = crossing.RoadWidth * 0.5f + PIPE_REACH_BEYOND_ROAD;

        if (back < 0f || ahead < 0f || back > limit || ahead > limit)
        {
            crossing.Span = 0f;
            return;
        }

        crossing.Span = back + ahead;
        crossing.Center = crossing.Position + flow * (0.5f * (ahead - back));
        crossing.DeckHeight = crossing.WaterHeight;
    }

    private static float Walk(HeightMap heights, Vector2 origin, Vector2 direction, float start, float limit, System.Func<float, bool> reached)
    {
        for (float travelled = start; travelled <= limit; travelled += STEP)
        {
            Vector2 at = origin + direction * travelled;

            if (reached(heights.SampleWorldSmooth(at.x, at.y)))
                return travelled;
        }

        return -1f;
    }
}

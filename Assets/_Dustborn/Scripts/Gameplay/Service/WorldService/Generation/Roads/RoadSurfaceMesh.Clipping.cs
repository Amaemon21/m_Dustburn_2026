using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private static void Clip(List<Vertex> polygon, Vector2 from, Vector2 to, bool inside, List<Vertex> output)
    {
        if (polygon.Count == 0)
            return;

        Vertex previous = polygon[polygon.Count - 1];
        float previousDistance = Cross(to - from, previous.Point - from);

        if (!inside)
            previousDistance = -previousDistance;

        foreach (Vertex current in polygon)
        {
            float distance = Cross(to - from, current.Point - from);

            if (!inside)
                distance = -distance;

            if ((distance >= 0f) != (previousDistance >= 0f))
                output.Add(Vertex.Lerp(previous, current, previousDistance / (previousDistance - distance)));

            if (distance >= 0f)
                output.Add(current);

            previous = current;
            previousDistance = distance;
        }
    }

    private static List<List<Vertex>> Subtract(List<List<Vertex>> pieces, Vector2[] hole, Vector2 holeMin, Vector2 holeMax, Scratch scratch)
    {
        List<List<Vertex>> remaining = scratch.Group();

        foreach (List<Vertex> piece in pieces)
        {
            if (Separated(piece, hole, holeMin, holeMax))
                remaining.Add(piece);
            else
                Subtract(piece, hole, remaining, scratch);
        }

        scratch.Release(pieces);

        return remaining;
    }

    private static void Subtract(List<Vertex> polygon, Vector2[] hole, List<List<Vertex>> output, Scratch scratch)
    {
        for (int edge = 0; edge < hole.Length && polygon.Count >= 3; edge++)
        {
            Vector2 from = hole[edge];
            Vector2 to = hole[(edge + 1) % hole.Length];

            if ((to - from).sqrMagnitude <= MIN_AREA * MIN_AREA)
                continue;

            List<Vertex> outside = scratch.Polygon();
            Clip(polygon, to, from, false, outside);

            if (Area(outside) > MIN_AREA)
                output.Add(outside);
            else
                scratch.Release(outside);

            List<Vertex> inside = scratch.Polygon();
            Clip(polygon, to, from, true, inside);
            scratch.Release(polygon);
            polygon = inside;
        }

        scratch.Release(polygon);
    }

    private static bool Separated(List<Vertex> piece, Vector2[] hole, Vector2 holeMin, Vector2 holeMax)
    {
        Vector2 min = piece[0].Point;
        Vector2 max = min;

        foreach (Vertex vertex in piece)
        {
            min = Vector2.Min(min, vertex.Point);
            max = Vector2.Max(max, vertex.Point);
        }

        if (max.x <= holeMin.x || min.x >= holeMax.x || max.y <= holeMin.y || min.y >= holeMax.y)
            return true;

        for (int edge = 0; edge < hole.Length; edge++)
        {
            if (SeparatedAlong(hole[(edge + 1) % hole.Length] - hole[edge], piece, hole))
                return true;
        }

        for (int edge = 0; edge < piece.Count; edge++)
        {
            if (SeparatedAlong(piece[(edge + 1) % piece.Count].Point - piece[edge].Point, piece, hole))
                return true;
        }

        return false;
    }

    private static bool SeparatedAlong(Vector2 edge, List<Vertex> piece, Vector2[] hole)
    {
        if (edge.sqrMagnitude <= MIN_AREA * MIN_AREA)
            return false;

        var axis = new Vector2(-edge.y, edge.x);
        float pieceMin = float.MaxValue, pieceMax = float.MinValue;
        float holeMin = float.MaxValue, holeMax = float.MinValue;

        foreach (Vertex vertex in piece)
        {
            float projection = Vector2.Dot(vertex.Point, axis);
            pieceMin = Mathf.Min(pieceMin, projection);
            pieceMax = Mathf.Max(pieceMax, projection);
        }

        foreach (Vector2 point in hole)
        {
            float projection = Vector2.Dot(point, axis);
            holeMin = Mathf.Min(holeMin, projection);
            holeMax = Mathf.Max(holeMax, projection);
        }

        return pieceMax <= holeMin || holeMax <= pieceMin;
    }

    private static float Area(List<Vertex> polygon)
    {
        float area = 0f;

        for (int i = 1; i < polygon.Count - 1; i++)
            area += Cross(polygon[i].Point - polygon[0].Point, polygon[i + 1].Point - polygon[0].Point);

        return Mathf.Abs(area) * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}

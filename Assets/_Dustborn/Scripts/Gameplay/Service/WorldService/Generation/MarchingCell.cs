using System;
using UnityEngine;

public static class MarchingCell
{
    private const float EDGE_TOLERANCE = 1e-6f;

    public static float Crossing(float from, float to)
    {
        return Mathf.Clamp01(from / (from - to));
    }

    public static bool Split(float v0, float v1, float v2, float v3)
    {
        bool in0 = v0 > 0f, in1 = v1 > 0f, in2 = v2 > 0f, in3 = v3 > 0f;

        return in0 == in2 && in1 == in3 && in0 != in1 && v0 + v1 + v2 + v3 <= 0f;
    }

    public static bool Contains(float v0, float v1, float v2, float v3, float x, float z)
    {
        bool in0 = v0 > 0f, in1 = v1 > 0f, in2 = v2 > 0f, in3 = v3 > 0f;
        int inside = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0) + (in3 ? 1 : 0);

        if (inside == 0)
            return false;

        if (inside == 4)
            return true;

        var south = new Vector2(Crossing(v0, v1), 0f);
        var east = new Vector2(1f, Crossing(v1, v2));
        var north = new Vector2(Crossing(v3, v2), 1f);
        var west = new Vector2(0f, Crossing(v0, v3));
        var point = new Vector2(x, z);

        if (Split(v0, v1, v2, v3))
        {
            if (in0)
                return InTriangle(point, west, new Vector2(0f, 0f), south) || InTriangle(point, east, new Vector2(1f, 1f), north);

            return InTriangle(point, south, new Vector2(1f, 0f), east) || InTriangle(point, north, new Vector2(0f, 1f), west);
        }

        Span<Vector2> polygon = stackalloc Vector2[6];
        int count = 0;

        if (in0) polygon[count++] = new Vector2(0f, 0f);
        if (in0 != in1) polygon[count++] = south;
        if (in1) polygon[count++] = new Vector2(1f, 0f);
        if (in1 != in2) polygon[count++] = east;
        if (in2) polygon[count++] = new Vector2(1f, 1f);
        if (in2 != in3) polygon[count++] = north;
        if (in3) polygon[count++] = new Vector2(0f, 1f);
        if (in3 != in0) polygon[count++] = west;

        for (int i = 0; i < count; i++)
        {
            if (Cross(polygon[i], polygon[(i + 1) % count], point) < -EDGE_TOLERANCE)
                return false;
        }

        return true;
    }

    private static bool InTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(a, b, point) >= -EDGE_TOLERANCE && Cross(b, c, point) >= -EDGE_TOLERANCE && Cross(c, a, point) >= -EDGE_TOLERANCE;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 point)
    {
        return (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
    }
}

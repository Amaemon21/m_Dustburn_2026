using UnityEngine;

public partial class RoadPlanner
{
    private float TurnCost(int heading, int step)
    {
        if (heading == FREE_HEADING || _config.RoadTurnPenalty <= 0f)
            return 0f;

        float dot = Mathf.Clamp(Vector2.Dot(_headings[heading], _headings[step]), -1f, 1f);

        return Mathf.Acos(dot) * _config.RoadTurnPenalty;
    }

    private float TurnRadius(int heading, int step)
    {
        if (heading == FREE_HEADING)
            return float.MaxValue;

        float dot = Vector2.Dot(_headings[heading], _headings[step]);

        if (dot > 0.9999f)
            return float.MaxValue;

        if (dot <= 0f)
            return 0f;

        Vector2 before = -new Vector2(STEP_X[heading], STEP_Y[heading]) * _cellSize;
        Vector2 after = new Vector2(STEP_X[step], STEP_Y[step]) * _cellSize;

        return RoadSmoother.Circumradius(before, Vector2.zero, after);
    }

    private float CrossSlope(int fromX, int fromY, int toX, int toY, float deltaX, float deltaY, float distance)
    {
        float perpendicularX = -deltaY / distance;
        float perpendicularY = deltaX / distance;
        float probe = _config.RoadHalfWidth + _config.RoadShoulder;
        float steepest = 0f;

        for (int k = 1; k <= CROSS_SAMPLES; k++)
        {
            float t = k / (CROSS_SAMPLES + 1f);
            float x = (fromX + 0.5f + (toX - fromX) * t) * _cellSize;
            float y = (fromY + 0.5f + (toY - fromY) * t) * _cellSize;
            float left = _map.SampleWorldSmooth(x - perpendicularX * probe, y - perpendicularY * probe);
            float right = _map.SampleWorldSmooth(x + perpendicularX * probe, y + perpendicularY * probe);

            steepest = Mathf.Max(steepest, Mathf.Abs(right - left) / (2f * probe));
        }

        return steepest;
    }

    private int HeadingOf(Vector2 direction)
    {
        int best = 0;
        float bestDot = float.MinValue;

        for (int i = 0; i < HEADINGS; i++)
        {
            float dot = Vector2.Dot(_headings[i], direction);

            if (dot <= bestDot)
                continue;

            bestDot = dot;
            best = i;
        }

        return best;
    }

    private float SampleMeters(int cellX, int cellY)
    {
        return _map.SampleWorld(new Vector3((cellX + 0.5f) * _cellSize, 0f, (cellY + 0.5f) * _cellSize));
    }

    private int CellOf(Vector2 position)
    {
        int x = Mathf.Clamp((int)(position.x / _cellSize), 0, _resolution - 1);
        int y = Mathf.Clamp((int)(position.y / _cellSize), 0, _resolution - 1);

        return y * _resolution + x;
    }

    private Vector2 CellCenter(int cell)
    {
        return new Vector2((cell % _resolution + 0.5f) * _cellSize, (cell / _resolution + 0.5f) * _cellSize);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 1; i < points.Length; i++)
            total += Vector2.Distance(points[i - 1], points[i]);

        return total;
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
    }

    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
    {
        t = 0f;
        u = 0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-8f)
            return false;

        Vector2 delta = c - a;

        t = (delta.x * s.y - delta.y * s.x) / denominator;
        u = (delta.x * r.y - delta.y * r.x) / denominator;

        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }
}

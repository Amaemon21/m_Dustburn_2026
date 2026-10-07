using System.Collections.Generic;
using UnityEngine;

public partial class TerrainCarver
{
    private Vector2[] Densify(Vector2[] points)
    {
        var result = new List<Vector2>();

        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (_cellSize * 0.5f)));

            for (int step = 0; step < steps; step++)
                result.Add(Vector2.Lerp(points[i], points[i + 1], step / (float)steps));
        }

        result.Add(points[^1]);

        return result.ToArray();
    }

    private float[] SampleGround(Vector2[] points)
    {
        var ground = new float[points.Length];

        for (int i = 0; i < ground.Length; i++)
            ground[i] = SampleNormalized(points[i].x, points[i].y);

        return ground;
    }

    private float[] SampleSurface(Vector2[] points)
    {
        var surface = new float[points.Length];
        int resolution = _source.Resolution;

        for (int i = 0; i < surface.Length; i++)
        {
            int cellX = Mathf.Clamp(Mathf.RoundToInt(points[i].x / _cellSize), 0, resolution - 1);
            int cellY = Mathf.Clamp(Mathf.RoundToInt(points[i].y / _cellSize), 0, resolution - 1);
            int cell = cellY * resolution + cellX;
            float original = _source.Heights[cell];

            surface[i] = original + (_carveTarget[cell] - original) * Mathf.Clamp01(_carveWeight[cell]);
        }

        return surface;
    }

    private float[] BuildProfile(float[] ground, float maxFill, float maxCut, int smoothing)
    {
        return RoadProfile.Build(ground, maxFill / _source.MaxHeight, maxCut / _source.MaxHeight, smoothing);
    }

    private void ClampEarthworks(float[] profile, float[] ground, float maxFillMeters, float maxCutMeters)
    {
        float maxFill = maxFillMeters / _source.MaxHeight;
        float maxCut = maxCutMeters / _source.MaxHeight;

        for (int i = 0; i < profile.Length; i++)
            profile[i] = Mathf.Clamp(profile[i], ground[i] - maxCut, ground[i] + maxFill);
    }

    private static float[] MovingAverage(float[] values, int window)
    {
        return RoadProfile.MovingAverage(values, window);
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
    }

    private static Vector2 Direction(Vector2[] points, int index)
    {
        int from = Mathf.Max(0, index - 1);
        int to = Mathf.Min(points.Length - 1, index + 1);

        Vector2 delta = points[to] - points[from];

        return delta.sqrMagnitude > 1e-6f ? delta.normalized : new Vector2(0f, 1f);
    }

    private float SampleMask(Vector2 point)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(point.x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(point.y / _cellSize), 0, resolution - 1);

        return _roadMask[cellY * resolution + cellX];
    }

    private float LateralEarthworks(Vector2[] points, int index, float profile, float reach)
    {
        Vector2 direction = Direction(points, index);
        Vector2 right = new(direction.y, -direction.x);
        Vector2 point = points[index];

        float moved = Mathf.Abs(profile - SampleNormalized(point.x, point.y));

        moved = Mathf.Max(moved, Mathf.Abs(profile - SampleNormalized(point.x + right.x * reach, point.y + right.y * reach)));
        moved = Mathf.Max(moved, Mathf.Abs(profile - SampleNormalized(point.x - right.x * reach, point.y - right.y * reach)));

        return moved * _source.MaxHeight;
    }
}

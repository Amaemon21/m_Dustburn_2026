using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private float[] BuildProfileField()
    {
        int size = _profileResolution;
        var ground = new float[size * size];

        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
                ground[y * size + x] = _map.SampleWorldSmooth(x * PROFILE_CELL, y * PROFILE_CELL);
        });

        float reach = Mathf.Clamp((_config.MaxRoadFill + _config.MaxRoadCut) / (2f * Mathf.Max(_config.RoadMaxGrade, 1e-3f)), MIN_CARVE_REACH, MAX_CARVE_REACH);
        int radius = Mathf.Max(1, Mathf.RoundToInt(reach * 0.5f / PROFILE_CELL));
        float[] field = ground;

        for (int pass = 0; pass < PROFILE_BLUR_PASSES; pass++)
            field = BoxBlur(field, size, radius);

        for (int i = 0; i < field.Length; i++)
            field[i] = Mathf.Clamp(field[i], ground[i] - _config.MaxRoadCut, ground[i] + _config.MaxRoadFill);

        return field;
    }

    private float Smoothed(float x, float y)
    {
        int last = _profileResolution - 1;
        float u = Mathf.Clamp(x / PROFILE_CELL, 0f, last);
        float v = Mathf.Clamp(y / PROFILE_CELL, 0f, last);
        int x0 = Mathf.Min((int)u, last - 1);
        int y0 = Mathf.Min((int)v, last - 1);
        int row = y0 * _profileResolution;
        int next = row + _profileResolution;
        float bottom = Mathf.Lerp(_profile[row + x0], _profile[row + x0 + 1], u - x0);
        float top = Mathf.Lerp(_profile[next + x0], _profile[next + x0 + 1], u - x0);

        return Mathf.Lerp(bottom, top, v - y0);
    }

    private float StepGrade(int fromX, int fromY, int toX, int toY, float distance)
    {
        int pieces = Mathf.Max(2, Mathf.CeilToInt(distance / GRADE_PROBE));
        float startX = (fromX + 0.5f) * _cellSize;
        float startY = (fromY + 0.5f) * _cellSize;
        float endX = (toX + 0.5f) * _cellSize;
        float endY = (toY + 0.5f) * _cellSize;
        float previous = Smoothed(startX, startY);
        float steepest = 0f;

        for (int k = 1; k <= pieces; k++)
        {
            float t = k / (float)pieces;
            float height = Smoothed(startX + (endX - startX) * t, startY + (endY - startY) * t);

            steepest = Mathf.Max(steepest, Mathf.Abs(height - previous) * pieces / distance);
            previous = height;
        }

        return steepest;
    }
}

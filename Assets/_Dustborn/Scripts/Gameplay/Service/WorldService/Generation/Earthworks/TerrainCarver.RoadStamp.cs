using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class TerrainCarver
{
    private void StampRoad(Vector2[] points, float[] distance, float[] profile, float[] skirts, float halfWidth, float formation, float[] maxFill, float[] maxCut,
        bool[] bridged = null)
    {
        List<Vector2> spans = Spans(bridged, distance);
        bool[] nearSpan = NearSpans(spans, distance, halfWidth, skirts);

        int resolution = _source.Resolution;
        float cellSize = _cellSize;
        float paintReach = PaintReach;

        var lowRow = new int[points.Length];
        var highRow = new int[points.Length];

        int minY = resolution - 1;
        int maxY = 0;

        for (int i = 0; i < points.Length; i++)
        {
            float radius = halfWidth + skirts[i];

            lowRow[i] = Mathf.Max(0, Mathf.FloorToInt((points[i].y - radius) / cellSize));
            highRow[i] = Mathf.Min(resolution - 1, Mathf.CeilToInt((points[i].y + radius) / cellSize));

            if (lowRow[i] < minY)
                minY = lowRow[i];

            if (highRow[i] > maxY)
                maxY = highRow[i];
        }

        if (maxY < minY)
            return;

        int bandCount = (maxY - minY) / BAND_ROWS + 1;
        var bands = new List<int>[bandCount];

        for (int i = 0; i < points.Length; i++)
        {
            if (highRow[i] < lowRow[i])
                continue;

            int first = (lowRow[i] - minY) / BAND_ROWS;
            int last = (highRow[i] - minY) / BAND_ROWS;

            for (int band = first; band <= last; band++)
                (bands[band] ??= new List<int>()).Add(i);
        }

        Parallel.For(0, bandCount, band =>
        {
            List<int> bucket = bands[band];

            if (bucket == null)
                return;

            int from = minY + band * BAND_ROWS;
            int to = Mathf.Min(maxY, from + BAND_ROWS - 1);

            foreach (int i in bucket)
            {
                if (bridged != null && bridged[i])
                    continue;

                Stamp(points, distance, profile, i, halfWidth, formation, skirts[i], paintReach, maxFill[i], maxCut[i], from, to, nearSpan != null && nearSpan[i] ? spans : null);
            }
        });
    }

    private void Stamp(Vector2[] points, float[] distance, float[] profile, int index, float inner, float formation, float outer, float paintReach,
        float maxFillMeters, float maxCutMeters, int clipMinY, int clipMaxY, List<Vector2> spans = null)
    {
        Vector2 point = points[index];
        float radius = inner + outer;
        int resolution = _source.Resolution;
        int last = points.Length - 1;
        int back = Mathf.Max(0, index - 1);
        int ahead = Mathf.Min(last, index + 1);
        Vector2 chord = points[ahead] - points[back];
        float span = distance[ahead] - distance[back];
        Vector2 tangent = chord.sqrMagnitude > 1e-8f ? chord.normalized : Vector2.zero;
        float slope = span > 1e-4f ? (profile[ahead] - profile[back]) / span : 0f;
        float lowAlong = index == 0 ? 0f : -radius;
        float highAlong = index == last ? 0f : radius;
        float height = profile[index];

        float maxFill = maxFillMeters / _source.MaxHeight;
        float maxCut = maxCutMeters / _source.MaxHeight;

        int minX = Mathf.Max(0, Mathf.FloorToInt((point.x - radius) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((point.x + radius) / _cellSize));
        int minY = Mathf.Max(clipMinY, Mathf.FloorToInt((point.y - radius) / _cellSize));
        int maxY = Mathf.Min(clipMaxY, Mathf.CeilToInt((point.y + radius) / _cellSize));

        bool paintsSurface = paintReach > 0f && _roadMask != null;
        float rejectSq = radius * radius * SQUARE_SLACK;

        for (int y = minY; y <= maxY; y++)
        {
            float deltaY = y * _cellSize - point.y;
            float deltaYSq = deltaY * deltaY;
            int row = y * resolution;

            for (int x = minX; x <= maxX; x++)
            {
                int cell = row + x;

                if (_carveWeight[cell] >= 1f && (!paintsSurface || _roadMask[cell] >= 1f))
                    continue;

                float deltaX = x * _cellSize - point.x;
                float distanceSq = deltaX * deltaX + deltaYSq;

                if (distanceSq > rejectSq)
                    continue;

                if (spans != null && UnderDeck(spans, distance[index] + deltaX * tangent.x + deltaY * tangent.y))
                    continue;

                float offset = Mathf.Sqrt(distanceSq);

                if (offset > radius)
                    continue;

                float weight = offset <= inner ? 1f : 1f - (offset - inner) / outer;
                weight = Mathf.SmoothStep(0f, 1f, weight);

                if (weight > _carveWeight[cell])
                {
                    float ground = _source.Heights[cell];
                    float along = Mathf.Clamp(deltaX * tangent.x + deltaY * tangent.y, lowAlong, highAlong);

                    _carveWeight[cell] = weight;
                    _carveTarget[cell] = offset <= formation
                        ? ProfileAt(distance, profile, index, distance[index] + along)
                        : KeepShore(x * _cellSize, y * _cellSize, ground, Mathf.Clamp(height + slope * Mathf.Clamp(along, -formation, formation), ground - maxCut, ground + maxFill));
                }

                if (!paintsSurface)
                    continue;

                float surface = offset <= inner ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (offset - inner) / paintReach);

                if (surface > _roadMask[cell])
                    _roadMask[cell] = Mathf.Clamp01(surface);
            }
        }
    }

    private float KeepShore(float x, float z, float ground, float target)
    {
        if (_water == null || target >= ground)
            return target;

        int cell = _water.CellIndex(x, z);
        float level = _water.ShoreSurface[cell];

        if (float.IsNegativeInfinity(level))
            return target;

        float reach = ShoreDistance(x, z);
        float floor = (level + SHORE_CLEARANCE - SHORE_RIM_SLOPE * Mathf.Max(0f, reach - SHORE_RIM)) / _source.MaxHeight;

        return Mathf.Max(target, Mathf.Min(ground, floor));
    }

    private float ShoreDistance(float x, float z)
    {
        float cell = _water.CellSize;
        int last = _water.Resolution - 1;
        float u = Mathf.Clamp(x / cell - 0.5f, 0f, last);
        float v = Mathf.Clamp(z / cell - 0.5f, 0f, last);
        int column = Mathf.Min((int)u, last - 1);
        int row = Mathf.Min((int)v, last - 1);
        int origin = row * _water.Resolution + column;
        float[] distance = _water.ShoreDistance;

        float a = distance[origin], b = distance[origin + 1];
        float c = distance[origin + _water.Resolution], d = distance[origin + _water.Resolution + 1];

        if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsInfinity(c) || float.IsInfinity(d))
            return Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) + cell;

        return Mathf.Lerp(Mathf.Lerp(a, b, u - column), Mathf.Lerp(c, d, u - column), v - row);
    }

    private static float ProfileAt(float[] distance, float[] profile, int index, float target)
    {
        int last = distance.Length - 1;

        if (target <= distance[0])
            return profile[0];

        if (target >= distance[last])
            return profile[last];

        int low = Mathf.Min(index, last - 1);

        while (low > 0 && distance[low] > target)
            low--;

        while (low < last - 1 && distance[low + 1] < target)
            low++;

        float span = distance[low + 1] - distance[low];
        float t = span > 1e-6f ? Mathf.Clamp01((target - distance[low]) / span) : 0f;

        return Mathf.Lerp(profile[low], profile[low + 1], t);
    }
}

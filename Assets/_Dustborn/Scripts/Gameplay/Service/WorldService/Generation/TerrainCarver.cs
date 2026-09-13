using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TerrainCarver
{
    private const int BAND_ROWS = 16;
    private const int APPLY_ROWS = 65536;
    private const int BLUR_PASSES = 3;
    private const float SQUARE_SLACK = 1.0001f;
    private const float FAR = 1e9f;
    private const float DIAGONAL = 1.41421356f;

    private readonly WorldGenerationConfig _config;

    private float[] _carveWeight;
    private float[] _carveTarget;
    private float[] _roadMask;
    private float[] _padDistance;

    private HeightMap _source;
    private float _cellSize;

    public TerrainCarver(WorldGenerationConfig config)
    {
        _config = config;
    }

    private float PaintReach => _config.RoadVergeWidth + _config.RoadEdgeSoftness * 0.5f;

    public HeightMap CarveSettlements(HeightMap source, IReadOnlyList<SettlementLayout> layouts)
    {
        Begin(source);

        if (layouts != null)
        {
            foreach (SettlementLayout layout in layouts)
                CarveSettlement(layout);
        }

        return Apply();
    }

    public HeightMap CarveStreets(HeightMap source, IReadOnlyList<SettlementLayout> layouts, out float[] roadMask)
    {
        Begin(source);

        _roadMask = new float[source.Heights.Length];

        if (layouts != null)
        {
            foreach (SettlementLayout layout in layouts)
            {
                foreach (Road street in layout.Streets)
                    CarveRoad(street, _config.StreetHalfWidth, _config.StreetShoulder, _config.MaxStreetFill, _config.MaxStreetCut, _config.StreetProfileSmoothing);
            }
        }

        roadMask = _roadMask;

        return Apply();
    }

    public HeightMap CarveHighways(HeightMap source, IReadOnlyList<Road> roads, float[] roadMask)
    {
        Begin(source);

        _roadMask = roadMask ?? new float[source.Heights.Length];

        foreach (Road road in roads)
            CarveRoad(road, _config.RoadHalfWidth, _config.RoadShoulder, _config.MaxRoadFill, _config.MaxRoadCut, _config.RoadProfileSmoothing);

        return Apply();
    }

    public HeightMap CarvePads(HeightMap source, IReadOnlyList<PoiPlacement> placements)
    {
        Begin(source);

        _padDistance = new float[source.Heights.Length];
        System.Array.Fill(_padDistance, float.MaxValue);

        foreach (PoiPlacement placement in placements)
            CarvePad(placement);

        return Apply();
    }

    private void Begin(HeightMap source)
    {
        _source = source;
        _cellSize = (float)source.WorldSize / (source.Resolution - 1);

        int cellCount = source.Heights.Length;

        _carveWeight = new float[cellCount];
        _carveTarget = new float[cellCount];
        _roadMask = null;
    }

    private HeightMap Apply()
    {
        var result = new HeightMap(_source.Resolution, _source.WorldSize, _source.MaxHeight);

        float[] source = _source.Heights;
        float[] target = _carveTarget;
        float[] weight = _carveWeight;
        float[] heights = result.Heights;
        int length = heights.Length;
        int rows = (length + APPLY_ROWS - 1) / APPLY_ROWS;

        Parallel.For(0, rows, band =>
        {
            int end = Mathf.Min(length, (band + 1) * APPLY_ROWS);

            for (int i = band * APPLY_ROWS; i < end; i++)
                heights[i] = source[i] + (target[i] - source[i]) * Mathf.Clamp01(weight[i]);
        });

        return result;
    }

    private void CarveSettlement(SettlementLayout layout)
    {
        if (layout == null || layout.IsEmpty)
            return;

        int resolution = _source.Resolution;
        float skirt = Mathf.Max(_cellSize, _config.SettlementPadSkirt);
        int radius = Mathf.Max(0, Mathf.RoundToInt(_config.SettlementSmoothing / _cellSize));

        int minX = Mathf.Max(0, Mathf.FloorToInt((layout.Min.x - skirt) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((layout.Min.y - skirt) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((layout.Max.x + skirt) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((layout.Max.y + skirt) / _cellSize));

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        if (width <= 0 || height <= 0)
            return;

        float[] distance = DistanceToBlocks(layout, minX, minY, width, height);
        float[] level = SmoothGround(minX, minY, width, height, radius);

        float maxFill = _config.MaxHubFill / _source.MaxHeight;
        float maxCut = _config.MaxHubCut / _source.MaxHeight;

        Parallel.For(0, height, row =>
        {
            int line = (minY + row) * resolution + minX;

            for (int column = 0; column < width; column++)
            {
                float metres = distance[row * width + column];

                if (metres >= skirt)
                    continue;

                float weight = Mathf.SmoothStep(0f, 1f, 1f - metres / skirt);
                int index = line + column;

                if (weight <= _carveWeight[index])
                    continue;

                float ground = _source.Heights[index];

                _carveWeight[index] = weight;
                _carveTarget[index] = Mathf.Clamp(level[row * width + column], ground - maxCut, ground + maxFill);
            }
        });
    }

    private float[] DistanceToBlocks(SettlementLayout layout, int minX, int minY, int width, int height)
    {
        var distance = new float[width * height];
        System.Array.Fill(distance, FAR);

        foreach (Block block in layout.Blocks)
        {
            Vector2 low = block.Corners[0];
            Vector2 high = low;

            foreach (Vector2 corner in block.Corners)
            {
                low = Vector2.Min(low, corner);
                high = Vector2.Max(high, corner);
            }

            int fromX = Mathf.Max(0, Mathf.FloorToInt(low.x / _cellSize) - minX);
            int toX = Mathf.Min(width - 1, Mathf.CeilToInt(high.x / _cellSize) - minX);
            int fromY = Mathf.Max(0, Mathf.FloorToInt(low.y / _cellSize) - minY);
            int toY = Mathf.Min(height - 1, Mathf.CeilToInt(high.y / _cellSize) - minY);

            for (int row = fromY; row <= toY; row++)
            {
                for (int column = fromX; column <= toX; column++)
                {
                    var point = new Vector2((minX + column) * _cellSize, (minY + row) * _cellSize);

                    if (block.Contains(point))
                        distance[row * width + column] = 0f;
                }
            }
        }

        Chamfer(distance, width, height, _cellSize);

        return distance;
    }

    private static void Chamfer(float[] distance, int width, int height, float step)
    {
        float diagonal = step * DIAGONAL;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float value = distance[i];

                if (x > 0)
                    value = Mathf.Min(value, distance[i - 1] + step);

                if (y > 0)
                {
                    value = Mathf.Min(value, distance[i - width] + step);

                    if (x > 0)
                        value = Mathf.Min(value, distance[i - width - 1] + diagonal);

                    if (x < width - 1)
                        value = Mathf.Min(value, distance[i - width + 1] + diagonal);
                }

                distance[i] = value;
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int i = y * width + x;
                float value = distance[i];

                if (x < width - 1)
                    value = Mathf.Min(value, distance[i + 1] + step);

                if (y < height - 1)
                {
                    value = Mathf.Min(value, distance[i + width] + step);

                    if (x < width - 1)
                        value = Mathf.Min(value, distance[i + width + 1] + diagonal);

                    if (x > 0)
                        value = Mathf.Min(value, distance[i + width - 1] + diagonal);
                }

                distance[i] = value;
            }
        }
    }

    private float[] SmoothGround(int minX, int minY, int width, int height, int radius)
    {
        int resolution = _source.Resolution;
        int reach = radius * BLUR_PASSES;

        int fromX = Mathf.Max(0, minX - reach);
        int fromY = Mathf.Max(0, minY - reach);
        int toX = Mathf.Min(resolution - 1, minX + width - 1 + reach);
        int toY = Mathf.Min(resolution - 1, minY + height - 1 + reach);

        int outerWidth = toX - fromX + 1;
        int outerHeight = toY - fromY + 1;

        var values = new float[outerWidth * outerHeight];
        float[] heights = _source.Heights;

        Parallel.For(0, outerHeight, row =>
            System.Array.Copy(heights, (fromY + row) * resolution + fromX, values, row * outerWidth, outerWidth));

        if (radius > 0)
        {
            var scratch = new float[values.Length];

            for (int pass = 0; pass < BLUR_PASSES; pass++)
            {
                BoxRows(values, scratch, outerWidth, outerHeight, radius);
                BoxColumns(scratch, values, outerWidth, outerHeight, radius);
            }
        }

        var level = new float[width * height];
        int offsetX = minX - fromX;
        int offsetY = minY - fromY;

        for (int row = 0; row < height; row++)
            System.Array.Copy(values, (offsetY + row) * outerWidth + offsetX, level, row * width, width);

        return level;
    }

    private static void BoxRows(float[] source, float[] target, int width, int height, int radius)
    {
        float scale = 1f / (2 * radius + 1);

        Parallel.For(0, height, y =>
        {
            int row = y * width;
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += source[row + Mathf.Clamp(k, 0, width - 1)];

            for (int x = 0; x < width; x++)
            {
                target[row + x] = sum * scale;
                sum += source[row + Mathf.Min(x + radius + 1, width - 1)] - source[row + Mathf.Max(x - radius, 0)];
            }
        });
    }

    private static void BoxColumns(float[] source, float[] target, int width, int height, int radius)
    {
        float scale = 1f / (2 * radius + 1);

        Parallel.For(0, width, x =>
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += source[Mathf.Clamp(k, 0, height - 1) * width + x];

            for (int y = 0; y < height; y++)
            {
                target[y * width + x] = sum * scale;
                sum += source[Mathf.Min(y + radius + 1, height - 1) * width + x] - source[Mathf.Max(y - radius, 0) * width + x];
            }
        });
    }

    private void CarveRoad(Road road, float halfWidth, float shoulder, float maxFill, float maxCut, int smoothing)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        Vector2[] points = Densify(road.Points);

        float[] ground = SampleGround(points);
        float[] profile = BuildProfile(ground, maxFill, maxCut, smoothing);

        LevelToExistingRoads(points, ground, profile);

        float reach = halfWidth + shoulder * 0.5f;
        var skirts = new float[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            float moved = LateralEarthworks(points, i, profile[i], reach);

            skirts[i] = shoulder + moved * _config.RoadEmbankmentSlope;
        }

        StampRoad(points, profile, skirts, halfWidth, maxFill, maxCut);
    }

    private void StampRoad(Vector2[] points, float[] profile, float[] skirts, float halfWidth, float maxFill, float maxCut)
    {
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
                Stamp(points[i], profile[i], halfWidth, skirts[i], paintReach, maxFill, maxCut, from, to);
        });
    }

    private void LevelToExistingRoads(Vector2[] points, float[] ground, float[] profile)
    {
        if (_roadMask == null)
            return;

        for (int i = 0; i < points.Length; i++)
        {
            float mask = SampleMask(points[i]);

            if (mask <= 0f)
                continue;

            profile[i] = Mathf.Lerp(profile[i], ground[i], mask);
        }
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

    private void CarvePad(PoiPlacement placement)
    {
        Vector2 forward = placement.Forward;
        Vector2 right = new(forward.y, -forward.x);
        Vector2 center = placement.Ground;

        float street = SampleNormalized(center.x + forward.x * placement.Footprint.y * 0.5f, center.y + forward.y * placement.Footprint.y * 0.5f);

        float halfWidth = placement.Footprint.x * 0.5f;
        float halfDepth = placement.Footprint.y * 0.5f;
        float margin = Mathf.Max(_config.PoiPadMargin, _cellSize);
        float skirt = _config.PoiPadSkirt;
        float reach = Mathf.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth) + margin + skirt;

        float maxFill = _config.MaxPoiFill / _source.MaxHeight;
        float maxCut = _config.MaxPoiCut / _source.MaxHeight;

        int resolution = _source.Resolution;

        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.x + reach) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - reach) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.y + reach) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float deltaX = x * _cellSize - center.x;
                float deltaY = y * _cellSize - center.y;

                float alongRight = Mathf.Abs(deltaX * right.x + deltaY * right.y) - halfWidth;
                float alongForward = Mathf.Abs(deltaX * forward.x + deltaY * forward.y) - halfDepth;

                float outsideX = Mathf.Max(alongRight, 0f);
                float outsideY = Mathf.Max(alongForward, 0f);

                float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                    + Mathf.Min(Mathf.Max(alongRight, alongForward), 0f);

                if (distance > margin + skirt)
                    continue;

                int index = y * resolution + x;

                if (distance >= _padDistance[index])
                    continue;

                float ground = _source.Heights[index];

                _padDistance[index] = distance;
                _carveWeight[index] = distance <= margin ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (distance - margin) / skirt);
                _carveTarget[index] = Mathf.Clamp(street, ground - maxCut, ground + maxFill);
            }
        }
    }

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

    private float[] BuildProfile(float[] ground, float maxFill, float maxCut, int smoothing)
    {
        float[] profile = MovingAverage(ground, smoothing);

        ClampEarthworks(profile, ground, maxFill, maxCut);

        profile = MovingAverage(profile, Mathf.Max(1, smoothing / 3));

        ClampEarthworks(profile, ground, maxFill, maxCut);

        return profile;
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
        var result = new float[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int samples = 0;

            for (int offset = -window; offset <= window; offset++)
            {
                sum += values[Mathf.Clamp(i + offset, 0, values.Length - 1)];
                samples++;
            }

            result[i] = sum / samples;
        }

        return result;
    }

    private void Stamp(Vector2 point, float normalizedHeight, float inner, float outer, float paintReach, float maxFillMeters, float maxCutMeters, int clipMinY, int clipMaxY)
    {
        float radius = inner + outer;
        int resolution = _source.Resolution;

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
                int index = row + x;

                if (_carveWeight[index] >= 1f && (!paintsSurface || _roadMask[index] >= 1f))
                    continue;

                float deltaX = x * _cellSize - point.x;
                float distanceSq = deltaX * deltaX + deltaYSq;

                if (distanceSq > rejectSq)
                    continue;

                float distance = Mathf.Sqrt(distanceSq);

                if (distance > radius)
                    continue;

                float weight = distance <= inner ? 1f : 1f - (distance - inner) / outer;
                weight = Mathf.SmoothStep(0f, 1f, weight);

                if (weight > _carveWeight[index])
                {
                    float ground = _source.Heights[index];

                    _carveWeight[index] = weight;
                    _carveTarget[index] = Mathf.Clamp(normalizedHeight, ground - maxCut, ground + maxFill);
                }

                if (!paintsSurface)
                    continue;

                float surface = distance <= inner ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (distance - inner) / paintReach);

                if (surface > _roadMask[index])
                    _roadMask[index] = Mathf.Clamp01(surface);
            }
        }
    }

    private float SampleNormalized(float x, float y)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(y / _cellSize), 0, resolution - 1);

        return _source.Get(cellX, cellY);
    }
}

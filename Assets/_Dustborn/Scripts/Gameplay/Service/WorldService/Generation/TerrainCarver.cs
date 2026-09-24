using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TerrainCarver
{
    private const int BAND_ROWS = 16;
    private const int APPLY_ROWS = 65536;
    private const int BLUR_PASSES = 3;
    private const float SQUARE_SLACK = 1.0001f;
    private const int RELAX_PASSES = 16;
    private const float GRADE_MARGIN = 0.9f;
    private const float ANCHOR_MASK = 0.99f;
    private const float RAMP_PROBE = 1f;
    private const float OVERRUN_MARGIN = 1f;
    private const float RAMP_EPSILON = 0.05f;

    private readonly WorldGenerationConfig _config;

    private float[] _carveWeight;
    private float[] _carveTarget;
    private float[] _roadMask;
    private float[] _padDistance;

    private HeightMap _source;
    private float _cellSize;

    public List<(Road Road, Vector2[] Points, float[] Profile, bool[] Anchored)> Profiles { get; set; }

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
            var streets = new List<Road>();

            foreach (SettlementLayout layout in layouts)
                streets.AddRange(layout.Streets);

            foreach (Road street in Ordered(streets))
                CarveRoad(street);
        }

        roadMask = _roadMask;

        return Apply();
    }

    public HeightMap CarveHighways(HeightMap source, IReadOnlyList<Road> roads, float[] roadMask)
    {
        Begin(source);

        _roadMask = roadMask ?? new float[source.Heights.Length];

        foreach (Road road in Ordered(roads))
            CarveRoad(road);

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
        var result = new HeightMap(_source.Resolution, _source.WorldSize, _source.MaxHeight) { Water = _source.Water };

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

        _carveWeight = null;
        _carveTarget = null;
        _padDistance = null;
        _roadMask = null;
        _source = null;

        return result;
    }

    private List<Road> Ordered(IReadOnlyList<Road> roads)
    {
        var ordered = new List<Road>(roads.Count);

        foreach (Road road in roads)
        {
            if (road?.Points != null && road.Points.Length >= 2)
                ordered.Add(road);
        }

        var rank = new Dictionary<Road, int>(ordered.Count);

        for (int i = 0; i < ordered.Count; i++)
            rank[ordered[i]] = i;

        ordered.Sort((left, right) =>
        {
            int compare = RoadKindProfile.For(_config, left.Kind).CarveOrder.CompareTo(RoadKindProfile.For(_config, right.Kind).CarveOrder);
            return compare != 0 ? compare : rank[left].CompareTo(rank[right]);
        });

        return ordered;
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

        float[] level = SmoothGround(minX, minY, width, height, radius);
        Dictionary<long, float> corners = CornerHeights(layout, level, minX, minY, width, height);

        float maxFill = _config.MaxHubFill / _source.MaxHeight;
        float maxCut = _config.MaxHubCut / _source.MaxHeight;
        float tileSize = layout.TileSize;

        Parallel.For(0, height, row =>
        {
            int y = minY + row;
            int line = y * resolution + minX;

            for (int column = 0; column < width; column++)
            {
                var point = new Vector2((minX + column) * _cellSize, y * _cellSize);

                layout.ToLocal(point, out float u, out float v);

                if (!Surface(layout, corners, u, v, tileSize, skirt, out float target, out float weight))
                    continue;

                int index = line + column;

                if (weight <= _carveWeight[index])
                    continue;

                float ground = _source.Heights[index];

                _carveWeight[index] = weight;
                _carveTarget[index] = Mathf.Clamp(target, ground - maxCut, ground + maxFill);
            }
        });

        foreach (SettlementGateway gateway in layout.Gateways)
            CarveGatewayRamp(layout, corners, gateway, skirt);
    }

    private void CarveGatewayRamp(SettlementLayout layout, Dictionary<long, float> corners, SettlementGateway gateway, float skirt)
    {
        layout.ToLocal(gateway.Port - gateway.Tangent * RAMP_PROBE, out float portU, out float portV);

        if (!Surface(layout, corners, portU, portV, layout.TileSize, skirt, out float pad, out _))
            return;

        float grade = _config.RoadMaxGrade * GRADE_MARGIN;
        float length = (_config.MaxHubCut + _config.MaxHubFill) / Mathf.Max(grade, 1e-3f);
        float halfWidth = _config.RoadHalfWidth + _config.RoadShoulder;
        float reach = halfWidth + _config.RoadShoulder + _config.RoadEmbankmentSlope * Mathf.Max(_config.MaxHubCut, _config.MaxHubFill);
        float maxFill = _config.MaxHubFill / _source.MaxHeight;
        float maxCut = _config.MaxHubCut / _source.MaxHeight;
        Vector2 tangent = gateway.Tangent;
        Vector2 far = gateway.Port + tangent * length;
        int resolution = _source.Resolution;

        int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(gateway.Port.x, far.x) - reach) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(gateway.Port.x, far.x) + reach) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(gateway.Port.y, far.y) - reach) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(gateway.Port.y, far.y) + reach) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 offset = new Vector2(x * _cellSize, y * _cellSize) - gateway.Port;
                float along = Vector2.Dot(offset, tangent);
                float across = Mathf.Abs(offset.x * tangent.y - offset.y * tangent.x);

                if (along < 0f || along > length || across > reach)
                    continue;

                int index = y * resolution + x;
                float ground = _source.Heights[index];
                float allowed = grade * along / _source.MaxHeight;
                float target = Mathf.Clamp(Mathf.Clamp(ground, pad - allowed, pad + allowed), ground - maxCut, ground + maxFill);
                float moved = Mathf.Abs(target - ground) * _source.MaxHeight;

                if (moved < RAMP_EPSILON)
                    continue;

                float outer = _config.RoadShoulder + moved * _config.RoadEmbankmentSlope;
                float weight = across <= halfWidth ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (across - halfWidth) / outer);

                if (weight <= _carveWeight[index])
                    continue;

                _carveWeight[index] = weight;
                _carveTarget[index] = target;
            }
        }
    }

    private Dictionary<long, float> CornerHeights(SettlementLayout layout, float[] level, int minX, int minY, int width, int height)
    {
        var corners = new Dictionary<long, float>();
        float half = layout.TileSize * 0.5f;

        foreach (SettlementTile tile in layout.Tiles)
        {
            for (int b = 0; b <= 1; b++)
            {
                for (int a = 0; a <= 1; a++)
                {
                    long key = SettlementLayout.Key(tile.I + a, tile.J + b);

                    if (corners.ContainsKey(key))
                        continue;

                    Vector2 corner = layout.LocalToWorld((tile.I + a) * layout.TileSize - half, (tile.J + b) * layout.TileSize - half);
                    int column = Mathf.Clamp(Mathf.RoundToInt(corner.x / _cellSize) - minX, 0, width - 1);
                    int row = Mathf.Clamp(Mathf.RoundToInt(corner.y / _cellSize) - minY, 0, height - 1);

                    corners[key] = level[row * width + column];
                }
            }
        }

        float limit = _config.MaxTileGrade * layout.TileSize / _source.MaxHeight;
        var keys = new List<long>(corners.Keys);

        keys.Sort();

        for (int pass = 0; pass < RELAX_PASSES; pass++)
        {
            bool changed = false;

            foreach (long key in keys)
            {
                int i = (int)(key >> 32) - 32768;
                int j = (int)(key & 0xffffffffL) - 32768;

                changed |= Limit(corners, key, SettlementLayout.Key(i + 1, j), limit);
                changed |= Limit(corners, key, SettlementLayout.Key(i, j + 1), limit);
            }

            if (!changed)
                break;
        }

        return corners;
    }

    private static bool Limit(Dictionary<long, float> corners, long key, long neighbour, float limit)
    {
        if (!corners.TryGetValue(neighbour, out float other))
            return false;

        float own = corners[key];
        float difference = own - other;

        if (Mathf.Abs(difference) <= limit)
            return false;

        float excess = (Mathf.Abs(difference) - limit) * 0.5f * Mathf.Sign(difference);

        corners[key] = own - excess;
        corners[neighbour] = other + excess;

        return true;
    }

    private static bool Surface(SettlementLayout layout, Dictionary<long, float> corners, float u, float v, float tileSize, float skirt,
        out float target, out float weight)
    {
        target = 0f;
        weight = 0f;

        float half = tileSize * 0.5f;
        int i = Mathf.FloorToInt(u / tileSize + 0.5f);
        int j = Mathf.FloorToInt(v / tileSize + 0.5f);

        if (layout.TileAt(i, j) != null)
        {
            target = Bilinear(corners, i, j, (u - i * tileSize + half) / tileSize, (v - j * tileSize + half) / tileSize);
            weight = 1f;
            return true;
        }

        float bestSqr = skirt * skirt;
        int bestI = 0;
        int bestJ = 0;
        bool found = false;

        for (int dj = -1; dj <= 1; dj++)
        {
            for (int di = -1; di <= 1; di++)
            {
                if (layout.TileAt(i + di, j + dj) == null)
                    continue;

                float outsideU = Mathf.Max(0f, Mathf.Abs(u - (i + di) * tileSize) - half);
                float outsideV = Mathf.Max(0f, Mathf.Abs(v - (j + dj) * tileSize) - half);
                float distanceSqr = outsideU * outsideU + outsideV * outsideV;

                if (distanceSqr >= bestSqr)
                    continue;

                bestSqr = distanceSqr;
                bestI = i + di;
                bestJ = j + dj;
                found = true;
            }
        }

        if (!found)
            return false;

        float s = Mathf.Clamp01((u - bestI * tileSize + half) / tileSize);
        float t = Mathf.Clamp01((v - bestJ * tileSize + half) / tileSize);

        target = Bilinear(corners, bestI, bestJ, s, t);
        weight = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Sqrt(bestSqr) / skirt);

        return weight > 0f;
    }

    private static float Bilinear(Dictionary<long, float> corners, int i, int j, float s, float t)
    {
        float bottom = Mathf.Lerp(corners[SettlementLayout.Key(i, j)], corners[SettlementLayout.Key(i + 1, j)], s);
        float top = Mathf.Lerp(corners[SettlementLayout.Key(i, j + 1)], corners[SettlementLayout.Key(i + 1, j + 1)], s);

        return Mathf.Lerp(bottom, top, t);
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

    private void CarveRoad(Road road)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        RoadKindProfile kind = RoadKindProfile.For(_config, road.Kind);
        float halfWidth = kind.HalfWidth;
        float shoulder = kind.Shoulder;
        float maxFill = kind.MaxFill;
        float maxCut = kind.MaxCut;
        int smoothing = kind.ProfileSmoothing;

        Vector2[] points = Densify(road.Points);
        float[] distance = Cumulative(points);

        float[] original = SampleGround(points);
        float[] surface = SampleSurface(points);
        float[] profile = BuildProfile(original, maxFill, maxCut, smoothing);
        bool[] anchored = LevelToExistingRoads(points, surface, profile);

        RoadProfile.Fit(profile, original, distance, kind.MaxGrade * GRADE_MARGIN / _source.MaxHeight,
            maxFill / _source.MaxHeight, maxCut / _source.MaxHeight, anchored, RoadProfile.EARTHWORK_OVERRUN);

        Profiles?.Add((road, points, profile, anchored));

        float reach = halfWidth + shoulder * 0.5f;
        var skirts = new float[points.Length];
        var fills = new float[points.Length];
        var cuts = new float[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            float moved = LateralEarthworks(points, i, profile[i], reach);

            skirts[i] = shoulder + moved * _config.RoadEmbankmentSlope;
            fills[i] = Mathf.Max(maxFill, (profile[i] - original[i]) * _source.MaxHeight + OVERRUN_MARGIN);
            cuts[i] = Mathf.Max(maxCut, (original[i] - profile[i]) * _source.MaxHeight + OVERRUN_MARGIN);
        }

        StampRoad(points, distance, profile, skirts, halfWidth, reach, fills, cuts);
    }

    private void StampRoad(Vector2[] points, float[] distance, float[] profile, float[] skirts, float halfWidth, float formation, float[] maxFill, float[] maxCut)
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
                Stamp(points, distance, profile, i, halfWidth, formation, skirts[i], paintReach, maxFill[i], maxCut[i], from, to);
        });
    }

    private bool[] LevelToExistingRoads(Vector2[] points, float[] ground, float[] profile)
    {
        var anchored = new bool[points.Length];

        if (_roadMask == null)
            return anchored;

        for (int i = 0; i < points.Length; i++)
        {
            float mask = SampleMask(points[i]);

            if (mask <= 0f)
                continue;

            profile[i] = Mathf.Lerp(profile[i], ground[i], mask);
            anchored[i] = mask >= ANCHOR_MASK;
        }

        return anchored;
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
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

    private void Stamp(Vector2[] points, float[] distance, float[] profile, int index, float inner, float formation, float outer, float paintReach,
        float maxFillMeters, float maxCutMeters, int clipMinY, int clipMaxY)
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
                        : Mathf.Clamp(height + slope * Mathf.Clamp(along, -formation, formation), ground - maxCut, ground + maxFill);
                }

                if (!paintsSurface)
                    continue;

                float surface = offset <= inner ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (offset - inner) / paintReach);

                if (surface > _roadMask[cell])
                    _roadMask[cell] = Mathf.Clamp01(surface);
            }
        }
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

    private float SampleNormalized(float x, float y)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(y / _cellSize), 0, resolution - 1);

        return _source.Get(cellX, cellY);
    }
}

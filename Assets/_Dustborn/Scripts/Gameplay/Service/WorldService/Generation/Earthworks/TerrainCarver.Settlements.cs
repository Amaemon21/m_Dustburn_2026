using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class TerrainCarver
{
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
        float bench = Mathf.Min(length, _config.GatewayApproachLength);
        float dry = DryRampLength(gateway.Port, tangent, length, reach);
        length = Mathf.Min(length, dry);

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
                float level = along <= bench ? _source.SampleWorldSmooth(gateway.Port.x + tangent.x * along, gateway.Port.y + tangent.y * along) / _source.MaxHeight : ground;
                float formation = Mathf.Clamp(level, pad - allowed, pad + allowed);
                float target = along <= bench && across <= halfWidth ? formation : Mathf.Clamp(formation, ground - maxCut, ground + maxFill);
                float moved = Mathf.Abs(target - ground) * _source.MaxHeight;

                if (moved < RAMP_EPSILON)
                    continue;

                float outer = _config.RoadShoulder + moved * _config.RoadEmbankmentSlope;
                float weight = across <= halfWidth ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (across - halfWidth) / outer);
                weight *= Mathf.SmoothStep(0f, 1f, (dry - along) / RAMP_WATER_FADE);

                if (weight <= _carveWeight[index])
                    continue;

                _carveWeight[index] = weight;
                _carveTarget[index] = target;
            }
        }
    }

    private float DryRampLength(Vector2 port, Vector2 tangent, float length, float reach)
    {
        if (_water == null)
            return float.PositiveInfinity;

        float step = _water.CellSize * 0.5f;
        var normal = new Vector2(-tangent.y, tangent.x);

        for (float along = 0f; along <= length + step; along += step)
        {
            for (float across = -reach; across <= reach + step; across += step)
            {
                Vector2 point = port + tangent * along + normal * Mathf.Min(across, reach);

                if (point.x < 0f || point.y < 0f || point.x >= _water.WorldSize || point.y >= _water.WorldSize)
                    continue;

                if (_water.ShoreDistance[_water.CellIndex(point.x, point.y)] <= RAMP_WATER_GAP)
                    return along;
            }
        }

        return float.PositiveInfinity;
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
}

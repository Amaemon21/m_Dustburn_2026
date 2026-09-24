using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class RiverCarver
{
    public const float EDGE_LIFT = 0.1f;
    public const float GORGE = 3.5f;
    public const float MAX_RISE = 24f;
    public const float BANK_PER_HALF_WIDTH = 0.6f;
    public const float BANK_TOE = 0.6f;

    private const float TILE = 256f;

    private readonly struct Segment
    {
        public readonly RiverPoint A;
        public readonly RiverPoint B;
        public readonly float Reach;

        public Segment(RiverPoint a, RiverPoint b, float reach)
        {
            A = a;
            B = b;
            Reach = reach;
        }
    }

    public static HeightMap Carve(HeightMap source, WaterMap water, float bankWidth)
    {
        var result = new HeightMap(source.Resolution, source.WorldSize, source.MaxHeight) { Water = water };

        float[] original = source.Heights;
        float[] heights = result.Heights;

        System.Array.Copy(original, heights, original.Length);

        if (water.Rivers.Count == 0)
            return result;

        var segments = new List<Segment>();

        var reaches = new List<float[]>(water.Rivers.Count);

        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;
            var reach = new float[Mathf.Max(0, points.Count - 1)];

            for (int i = 0; i + 1 < points.Count; i++)
            {
                reach[i] = Reach(source, points[i], points[i + 1], bankWidth);
                segments.Add(new Segment(points[i], points[i + 1], reach[i]));
            }

            reaches.Add(reach);
        }

        water.CarveReach = reaches;
        water.Uncarved = source;

        int tiles = Mathf.CeilToInt(source.WorldSize / TILE);
        var buckets = new List<int>[tiles * tiles];

        for (int s = 0; s < segments.Count; s++)
        {
            Segment segment = segments[s];

            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(segment.A.Position.x, segment.B.Position.x) - segment.Reach) / TILE), 0, tiles - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(segment.A.Position.x, segment.B.Position.x) + segment.Reach) / TILE), 0, tiles - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(segment.A.Position.y, segment.B.Position.y) - segment.Reach) / TILE), 0, tiles - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(segment.A.Position.y, segment.B.Position.y) + segment.Reach) / TILE), 0, tiles - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                    (buckets[z * tiles + x] ??= new List<int>()).Add(s);
            }
        }

        float cell = (float)source.WorldSize / (source.Resolution - 1);
        float maxHeight = source.MaxHeight;
        int resolution = source.Resolution;

        Parallel.For(0, buckets.Length, tile =>
        {
            List<int> bucket = buckets[tile];

            if (bucket == null)
                return;

            int tileMinX = Mathf.CeilToInt(tile % tiles * TILE / cell);
            int tileMaxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((tile % tiles + 1) * TILE / cell) - 1);
            int tileMinZ = Mathf.CeilToInt(tile / tiles * TILE / cell);
            int tileMaxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((tile / tiles + 1) * TILE / cell) - 1);

            if (tile % tiles == tiles - 1)
                tileMaxX = resolution - 1;

            if (tile / tiles == tiles - 1)
                tileMaxZ = resolution - 1;

            int tileWidth = tileMaxX - tileMinX + 1;
            int tileCount = tileWidth * (tileMaxZ - tileMinZ + 1);
            var nearest = new float[tileCount];
            var bank = new float[tileCount];
            var channel = new float[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                nearest[i] = float.PositiveInfinity;
                channel[i] = float.PositiveInfinity;
            }

            foreach (int s in bucket)
            {
                Segment segment = segments[s];
                RiverPoint a = segment.A;
                RiverPoint b = segment.B;

                int minX = Mathf.Max(tileMinX, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - segment.Reach) / cell));
                int maxX = Mathf.Min(tileMaxX, Mathf.CeilToInt((Mathf.Max(a.Position.x, b.Position.x) + segment.Reach) / cell));
                int minZ = Mathf.Max(tileMinZ, Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - segment.Reach) / cell));
                int maxZ = Mathf.Min(tileMaxZ, Mathf.CeilToInt((Mathf.Max(a.Position.y, b.Position.y) + segment.Reach) / cell));

                Vector2 axis = b.Position - a.Position;
                float length = axis.sqrMagnitude;

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var point = new Vector2(x * cell, z * cell);
                        float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);
                        float distance = Vector2.Distance(point, a.Position + axis * t);

                        if (distance > segment.Reach)
                            continue;

                        int local = (z - tileMinZ) * tileWidth + x - tileMinX;
                        float ground = original[z * resolution + x] * maxHeight;
                        float half = 0.5f * Mathf.Lerp(a.Width, b.Width, t);

                        float target = Profile(distance, half, bankWidth, Mathf.Lerp(a.Bed, b.Bed, t), Mathf.Lerp(a.Surface, b.Surface, t), ground);

                        if (distance <= half)
                            channel[local] = Mathf.Min(channel[local], target);

                        if (distance >= nearest[local])
                            continue;

                        nearest[local] = distance;
                        bank[local] = target;
                    }
                }
            }

            for (int z = tileMinZ; z <= tileMaxZ; z++)
            {
                for (int x = tileMinX; x <= tileMaxX; x++)
                {
                    int local = (z - tileMinZ) * tileWidth + x - tileMinX;

                    if (float.IsPositiveInfinity(nearest[local]))
                        continue;

                    int index = z * resolution + x;
                    float carved = Mathf.Min(bank[local], channel[local]) / maxHeight;

                    if (carved < heights[index])
                        heights[index] = carved;
                }
            }
        });

        return result;
    }

    private static float Reach(HeightMap map, RiverPoint a, RiverPoint b, float bankWidth)
    {
        float half = 0.5f * Mathf.Max(a.Width, b.Width);
        float edge = Mathf.Min(a.Surface, b.Surface) + EDGE_LIFT;

        Vector2 axis = (b.Position - a.Position).normalized;
        var normal = new Vector2(-axis.y, axis.x);
        float rise = 0f;

        foreach (float distance in new[] { half + bankWidth, half + 2f * bankWidth, half + 4f * bankWidth })
        {
            foreach (RiverPoint end in new[] { a, b })
            {
                Vector2 left = end.Position + normal * distance;
                Vector2 right = end.Position - normal * distance;

                rise = Mathf.Max(rise, map.SampleWorldSmooth(left.x, left.y) - edge);
                rise = Mathf.Max(rise, map.SampleWorldSmooth(right.x, right.y) - edge);
            }
        }

        return half + BankWidth(half, bankWidth, rise) + 1f;
    }

    public static float BankWidth(float half, float bankWidth, float rise)
    {
        return Mathf.Max(bankWidth + BANK_PER_HALF_WIDTH * half, GORGE * Mathf.Min(rise, MAX_RISE));
    }

    public static float Profile(float distance, float half, float bankWidth, float bed, float surface, float ground)
    {
        float edge = surface + EDGE_LIFT;

        if (distance <= half)
        {
            float across = half <= 0f ? 1f : distance / half;
            float target = bed + (edge - bed) * across * across;

            return Mathf.Min(target, ground);
        }

        float rise = ground - edge;

        if (rise <= 0f)
            return ground;

        float width = BankWidth(half, bankWidth, rise);

        if (width <= 0f)
            return ground;

        float t = Mathf.Clamp01((distance - half) / width);
        float easeOut = 1f - (1f - t) * (1f - t);
        float toe = t * t * (3f - 2f * t);

        return Mathf.Min(ground, edge + rise * Mathf.Lerp(easeOut, toe, BANK_TOE));
    }
}

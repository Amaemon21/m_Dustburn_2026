using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

public static class RiverCarver
{
    public const float EDGE_LIFT = 0.1f;
    public const float FLAT_BED = 0.25f;
    public const float MIN_DEPTH = 0.05f;
    public const float WATERLINE_GRADE = 0.45f;
    private const float MAX_WATERLINE_TANGENT = 3f;
    private const float FLOOR_DROP = 2f;
    private const float FLOOR_REACH = (EDGE_LIFT + FLOOR_DROP) / WATERLINE_GRADE;
    private const int LAKE_GUARD_CELLS = StandingWaterRegions.OWNERSHIP_REACH + 2;
    private const float CROSSING_GUARD = 12f;
    public const float GORGE = 3.5f;
    public const float MAX_RISE = 24f;
    public const float BANK_PER_HALF_WIDTH = 0.6f;
    public const float BANK_TOE = 0.6f;
    public const float LIP_BASE = 0.4f;
    public const float LIP_PER_WIDTH = 0.04f;
    public const float MAX_LIP = 1f;
    private const float ROAD_KEEP = 0.5f;
    public const float WANDER_SHARE = 0.25f;
    public const float MIN_WANDER = 0.4f;
    public const float MAX_WANDER = 1.5f;
    public const float WANDER_LIMIT = 0.4f;
    public const float WANDER_PERIOD = 28f;
    public const float WANDER_WIDTHS = 3f;
    public const float TIP_WIDTHS = 1.5f;
    private const int WANDER_SEED = 7919;
    private static readonly float2 LeftWander = FractalNoise.Offset(WANDER_SEED, 1);
    private static readonly float2 RightWander = FractalNoise.Offset(WANDER_SEED, 2);

    private const float TILE = 256f;

    private readonly struct Segment
    {
        public readonly RiverPoint A;
        public readonly RiverPoint B;
        public readonly float Reach;
        public readonly bool OpenStart;
        public readonly bool OpenEnd;
        public readonly float AlongA;
        public readonly float AlongB;
        public readonly ChannelCourse Course;

        public Segment(RiverPoint a, RiverPoint b, float reach, bool openStart, bool openEnd, ChannelCourse course, int index)
        {
            A = a;
            B = b;
            Reach = reach;
            OpenStart = openStart;
            OpenEnd = openEnd;
            Course = course;
            AlongA = course.Along[index];
            AlongB = course.Along[index + 1];
        }
    }

    public static HeightMap Carve(HeightMap source, WaterMap water, float bankWidth, (bool Start, bool End)[] loose = null)
    {
        loose ??= RiverEnds.Loose(water);

        var result = new HeightMap(source.Resolution, source.WorldSize, source.MaxHeight);

        float[] original = source.Heights;
        float[] heights = result.Heights;

        System.Array.Copy(original, heights, original.Length);

        if (water.Rivers.Count == 0)
            return result;

        var segments = new List<Segment>();

        var reaches = new List<float[]>(water.Rivers.Count);

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            var reach = new float[Mathf.Max(0, points.Count - 1)];
            bool looseStart = points.Count >= 2 && loose[r].Start;
            bool looseEnd = points.Count >= 2 && loose[r].End;
            ChannelCourse course = new ChannelCourse(points, looseStart, looseEnd);

            for (int i = 0; i + 1 < points.Count; i++)
            {
                reach[i] = Reach(source, points[i], points[i + 1], bankWidth);
                segments.Add(new Segment(points[i], points[i + 1], reach[i], looseStart && i == 0, looseEnd && i + 2 == points.Count, course, i));
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
        short[] nearLake = NearLakes(water);
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
            var bank = new float[tileCount];
            var channel = new float[tileCount];
            var edge = new float[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                bank[i] = float.PositiveInfinity;
                channel[i] = float.PositiveInfinity;
                edge[i] = float.NegativeInfinity;
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
                        float along = length < 1e-8f ? 0f : Vector2.Dot(point - a.Position, axis) / length;

                        if (segment.OpenStart && along < 0f || segment.OpenEnd && along > 1f)
                            continue;

                        float t = Mathf.Clamp01(along);
                        float distance = Vector2.Distance(point, a.Position + axis * t);

                        if (distance > segment.Reach)
                            continue;

                        int local = (z - tileMinZ) * tileWidth + x - tileMinX;
                        float ground = original[z * resolution + x] * maxHeight;
                        float ribbon = 0.5f * Mathf.Lerp(a.Width, b.Width, t);
                        Vector2 foot = a.Position + axis * t;
                        float half = segment.Course.Half(ribbon, foot, Side(axis, point - a.Position), Mathf.Lerp(segment.AlongA, segment.AlongB, t));

                        float target = Profile(distance, half, bankWidth, Mathf.Lerp(a.Bed, b.Bed, t), Mathf.Lerp(a.Surface, b.Surface, t), ground);

                        if (distance > half)
                            target = AboveLake(water, nearLake, x * cell, z * cell, ground, target);

                        if (distance <= half)
                            channel[local] = Mathf.Min(channel[local], target);
                        else if (distance <= ribbon + WaterMeshes.RIBBON_PAD + FLOOR_REACH)
                            edge[local] = Mathf.Max(edge[local], Mathf.Min(target, Floor(distance - ribbon - WaterMeshes.RIBBON_PAD, Mathf.Lerp(a.Surface, b.Surface, t))));

                        bank[local] = Mathf.Min(bank[local], target);
                    }
                }
            }

            for (int z = tileMinZ; z <= tileMaxZ; z++)
            {
                for (int x = tileMinX; x <= tileMaxX; x++)
                {
                    int local = (z - tileMinZ) * tileWidth + x - tileMinX;

                    if (float.IsPositiveInfinity(bank[local]))
                        continue;

                    int index = z * resolution + x;
                    float carved = Mathf.Min(Mathf.Max(bank[local], edge[local]), channel[local]) / maxHeight;

                    if (carved < heights[index])
                        heights[index] = carved;
                }
            }
        });

        return result;
    }

    public static void Restore(HeightMap map, WaterMap water, float bridgeWidth, float[] roadMask = null)
    {
        if (water == null || water.Rivers.Count == 0)
            return;

        float cell = (float)map.WorldSize / (map.Resolution - 1);
        float maxHeight = map.MaxHeight;
        int resolution = map.Resolution;
        float[] heights = map.Heights;

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            ChannelCourse course = ChannelCourse.Of(water, r);
            bool looseStart = points.Count >= 2 && RiverEnds.Start(water, r) == RiverEnd.Loose;
            bool looseEnd = points.Count >= 2 && RiverEnds.End(water, r) == RiverEnd.Loose;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i], b = points[i + 1];

                if (a.Submerged && b.Submerged || NearCulvert(water, a.Position, bridgeWidth))
                    continue;

                float half = 0.5f * Mathf.Max(a.Width, b.Width);
                int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - half) / cell));
                int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.x, b.Position.x) + half) / cell));
                int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - half) / cell));
                int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(a.Position.y, b.Position.y) + half) / cell));
                Vector2 axis = b.Position - a.Position;
                float length = axis.sqrMagnitude;

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var point = new Vector2(x * cell, z * cell);
                        float along = length < 1e-8f ? 0f : Vector2.Dot(point - a.Position, axis) / length;

                        if (looseStart && i == 0 && along < 0f || looseEnd && i + 2 == points.Count && along > 1f)
                            continue;

                        float t = Mathf.Clamp01(along);
                        float distance = Vector2.Distance(point, a.Position + axis * t);
                        float width = course.Half(0.5f * Mathf.Lerp(a.Width, b.Width, t), a.Position + axis * t, Side(axis, point - a.Position), Mathf.Lerp(course.Along[i], course.Along[i + 1], t));

                        if (distance > width)
                            continue;

                        int index = z * resolution + x;

                        if (roadMask != null && roadMask[index] > ROAD_KEEP && !WaterCrossings.OverSpan(water, point))
                            continue;

                        float ground = heights[index] * maxHeight;
                        float target = Profile(distance, width, 0f, Mathf.Lerp(a.Bed, b.Bed, t), Mathf.Lerp(a.Surface, b.Surface, t), ground);

                        if (target < ground)
                            heights[index] = target / maxHeight;
                    }
                }
            }
        }
    }

    private static bool NearCulvert(WaterMap water, Vector2 point, float bridgeWidth)
    {
        foreach (WaterCrossing crossing in water.Crossings)
        {
            if (crossing.Bridge || crossing.RiverWidth >= bridgeWidth)
                continue;

            float reach = crossing.RoadWidth + CROSSING_GUARD;

            if ((crossing.Position - point).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
    }

    private static short[] NearLakes(WaterMap water)
    {
        int resolution = water.Resolution;
        var near = new short[water.BodyIds.Length];
        var distance = new int[near.Length];
        var queue = new Queue<int>();
        int reach = LAKE_GUARD_CELLS;

        for (int cell = 0; cell < near.Length; cell++)
        {
            near[cell] = water.BodyIds[cell];
            distance[cell] = near[cell] >= 0 ? 0 : int.MaxValue;

            if (near[cell] >= 0)
                queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();

            if (distance[cell] >= reach)
                continue;

            int cx = cell % resolution, cz = cell / resolution;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, nz = cz + dz;

                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution)
                        continue;

                    int next = nz * resolution + nx;

                    if (distance[next] <= distance[cell] + 1)
                        continue;

                    distance[next] = distance[cell] + 1;
                    near[next] = near[cell];
                    queue.Enqueue(next);
                }
            }
        }

        return near;
    }

    private static float AboveLake(WaterMap water, short[] nearLake, float x, float z, float ground, float target)
    {
        int lake = nearLake[water.CellIndex(x, z)];

        if (lake < 0)
            return target;

        float floor = water.Bodies[lake].Surface + EDGE_LIFT;

        return ground >= floor ? Mathf.Max(target, floor) : ground;
    }

    public static float Reach(HeightMap map, RiverPoint a, RiverPoint b, float bankWidth)
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

        return half + LipWidth(half) + BankWidth(half, bankWidth, rise) + 1f;
    }

    public static float BankWidth(float half, float bankWidth, float rise)
    {
        return Mathf.Max(bankWidth + BANK_PER_HALF_WIDTH * half, GORGE * Mathf.Min(rise, MAX_RISE));
    }

    public static float Profile(float distance, float half, float bankWidth, float bed, float surface, float ground)
    {
        if (distance <= half)
        {
            float depth = Mathf.Max(surface - bed, MIN_DEPTH);
            float start = FLAT_BED * half;
            float run = half - start;
            float s = run <= 0f ? 1f : Mathf.Clamp01((distance - start) / run);
            float waterline = Mathf.Min(MAX_WATERLINE_TANGENT, WATERLINE_GRADE * run / depth);
            float wall = s * s * (3f - 2f * s) + waterline * s * s * (s - 1f);

            return Mathf.Min(bed + depth * wall, ground);
        }

        float rise = ground - surface;

        if (rise <= 0f)
            return ground;

        float lip = LipHeight(half);
        float lipWidth = LipWidth(half);
        float x = Mathf.Clamp01((distance - half) / lipWidth);
        float bank = surface + lip * (1f - (1f - x) * (1f - x));
        float outer = rise - lip;

        if (outer <= 0f)
            return Mathf.Min(ground, bank);

        float width = BankWidth(half, bankWidth, outer);
        float across = Mathf.Clamp01((distance - half - lipWidth) / width);
        float easeOut = 1f - (1f - across) * (1f - across);
        float toe = across * across * (3f - 2f * across);

        return Mathf.Min(ground, bank + outer * Mathf.Lerp(easeOut, toe, BANK_TOE));
    }

    private static float Floor(float beyond, float surface)
    {
        return surface + EDGE_LIFT - WATERLINE_GRADE * Mathf.Max(0f, beyond);
    }

    public static float LipWidth(float half)
    {
        return 2f * LipHeight(half) / WATERLINE_GRADE;
    }

    public static float LipHeight(float half)
    {
        return Mathf.Min(MAX_LIP, LIP_BASE + LIP_PER_WIDTH * 2f * half);
    }

    private static float Side(Vector2 axis, Vector2 offset)
    {
        return axis.x * offset.y - axis.y * offset.x >= 0f ? 1f : -1f;
    }

    public static float Wander(float half, Vector2 foot, float side)
    {
        float amplitude = Mathf.Min(WANDER_LIMIT * half, Mathf.Clamp(WANDER_SHARE * half, MIN_WANDER, MAX_WANDER));
        float period = Mathf.Max(WANDER_PERIOD, WANDER_WIDTHS * 2f * half);
        float noise = FractalNoise.Sample01(new float2(foot.x, foot.y) / period, side > 0f ? LeftWander : RightWander, 2, 2f, 0.5f);

        return half - amplitude * noise;
    }

    public static float Tip(float reach, float half)
    {
        float span = TIP_WIDTHS * half;

        if (span <= 0f || reach >= span)
            return 1f;

        float u = 1f - Mathf.Max(0f, reach) / span;

        return Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
    }

    private sealed class ChannelCourse
    {
        public readonly float[] Along;
        private readonly bool _taperStart;
        private readonly bool _taperEnd;

        public ChannelCourse(List<RiverPoint> points, bool taperStart, bool taperEnd)
        {
            Along = new float[points.Count];

            for (int i = 1; i < points.Count; i++)
                Along[i] = Along[i - 1] + Vector2.Distance(points[i - 1].Position, points[i].Position);

            _taperStart = taperStart;
            _taperEnd = taperEnd;
        }

        public static ChannelCourse Of(WaterMap water, int river)
        {
            List<RiverPoint> points = water.Rivers[river].Points;
            bool ends = points.Count >= 2;

            return new ChannelCourse(points, ends && RiverEnds.Start(water, river) == RiverEnd.Loose, ends && RiverEnds.End(water, river) == RiverEnd.Loose);
        }

        public float Half(float half, Vector2 foot, float side, float along)
        {
            float shaped = Wander(half, foot, side);

            if (_taperStart)
                shaped *= Tip(along, half);

            if (_taperEnd && Along.Length > 0)
                shaped *= Tip(Along[^1] - along, half);

            return shaped;
        }
    }
}

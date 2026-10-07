using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static partial class RiverCarver
{
    public const float EDGE_LIFT = 0.1f;
    public const float FLAT_BED = 0.25f;
    public const float MIN_DEPTH = 0.05f;
    public const float WATERLINE_GRADE = 0.45f;
    private const float MAX_WATERLINE_TANGENT = 3f;
    private const float FLOOR_DROP = 2f;
    private const float FLOOR_REACH = (EDGE_LIFT + FLOOR_DROP) / WATERLINE_GRADE;
    private const int LAKE_GUARD_CELLS = StandingWaterRegions.OWNERSHIP_REACH + 2;
    private const float GUARD_FADE_CELLS = 2f;
    private const float CROSSING_GUARD = 12f;
    private const float LAKE_BANK_GRADE = 0.8f;
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

    internal readonly struct Segment
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

    public static HeightMap Carve(HeightMap source, WaterMap water, float bankWidth, (bool Start, bool End)[] loose = null, CarveCache cache = null)
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
        short[] nearLake = NearLakes(water, out byte[] lakeDistance);
        float maxHeight = source.MaxHeight;
        int resolution = source.Resolution;

        cache?.Prepare(source, bankWidth, buckets.Length);

        var work = new List<int>();
        var inputs = new CarveTile[buckets.Length];

        for (int tile = 0; tile < buckets.Length; tile++)
        {
            List<int> bucket = buckets[tile];

            if (bucket == null)
                continue;

            TileBounds(tile, tiles, cell, resolution, out int minX, out int minZ, out int maxX, out int maxZ);

            if (cache != null)
            {
                inputs[tile] = CarveTile.Describe(bucket, segments, water, nearLake, lakeDistance, minX * cell, minZ * cell, maxX * cell, maxZ * cell);

                if (cache.Reuse(tile, inputs[tile], heights, resolution, minX, minZ, maxX - minX + 1, maxZ))
                    continue;
            }

            work.Add(tile);
        }

        if (work.Count > 0)
            CarveTiles(work, buckets, segments, tiles, cell, resolution, maxHeight, bankWidth, water, nearLake, lakeDistance, heights);

        if (cache != null)
        {
            foreach (int tile in work)
            {
                TileBounds(tile, tiles, cell, resolution, out int minX, out int minZ, out int maxX, out int maxZ);
                cache.Store(tile, inputs[tile], heights, resolution, minX, minZ, maxX - minX + 1, maxZ);
            }
        }

        return result;
    }

    private static void TileBounds(int tile, int tiles, float cell, int resolution, out int minX, out int minZ, out int maxX, out int maxZ)
    {
        minX = Mathf.CeilToInt(tile % tiles * TILE / cell);
        maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((tile % tiles + 1) * TILE / cell) - 1);
        minZ = Mathf.CeilToInt(tile / tiles * TILE / cell);
        maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((tile / tiles + 1) * TILE / cell) - 1);

        if (tile % tiles == tiles - 1)
            maxX = resolution - 1;

        if (tile / tiles == tiles - 1)
            maxZ = resolution - 1;
    }

    private static void CarveTiles(List<int> work, List<int>[] buckets, List<Segment> segments, int tiles, float cell, int resolution, float maxHeight,
        float bankWidth, WaterMap water, short[] nearLake, byte[] lakeDistance, float[] heights)
    {
        int members = 0;
        int texels = 0;
        var bounds = new int4[work.Count];
        var offsets = new int[work.Count];
        var ranges = new int2[work.Count];

        for (int job = 0; job < work.Count; job++)
        {
            int tile = work[job];
            TileBounds(tile, tiles, cell, resolution, out int minX, out int minZ, out int maxX, out int maxZ);
            bounds[job] = new int4(minX, minZ, maxX, maxZ);
            offsets[job] = texels;
            ranges[job] = new int2(members, buckets[tile].Count);
            texels += (maxX - minX + 1) * (maxZ - minZ + 1);
            members += buckets[tile].Count;
        }

        var flat = new int[members];
        var compact = new float[texels];

        for (int job = 0; job < work.Count; job++)
        {
            buckets[work[job]].CopyTo(flat, ranges[job].x);
            int4 b = bounds[job];
            int width = b.z - b.x + 1;

            for (int z = b.y; z <= b.w; z++)
                System.Array.Copy(heights, z * resolution + b.x, compact, offsets[job] + (z - b.y) * width, width);
        }

        var carveSegments = new CarveSegment[segments.Count];

        for (int s = 0; s < segments.Count; s++)
            carveSegments[s] = CarveSegment.Of(segments[s]);

        var surfaces = new float[Mathf.Max(1, water.Bodies.Count)];

        for (int body = 0; body < water.Bodies.Count; body++)
            surfaces[body] = water.Bodies[body].Surface;

        var segmentArray = new NativeArray<CarveSegment>(carveSegments, Allocator.TempJob);
        var memberArray = new NativeArray<int>(flat.Length > 0 ? flat : new int[1], Allocator.TempJob);
        var rangeArray = new NativeArray<int2>(ranges, Allocator.TempJob);
        var boundArray = new NativeArray<int4>(bounds, Allocator.TempJob);
        var offsetArray = new NativeArray<int>(offsets, Allocator.TempJob);
        var lakeArray = new NativeArray<short>(nearLake, Allocator.TempJob);
        var distanceArray = new NativeArray<byte>(lakeDistance, Allocator.TempJob);
        var surfaceArray = new NativeArray<float>(surfaces, Allocator.TempJob);
        var heightArray = new NativeArray<float>(compact, Allocator.TempJob);

        try
        {
            new CarveJob
            {
                Segments = segmentArray,
                Members = memberArray,
                Ranges = rangeArray,
                Bounds = boundArray,
                Offsets = offsetArray,
                NearLake = lakeArray,
                NearDistance = distanceArray,
                GuardReach = LAKE_GUARD_CELLS,
                Surfaces = surfaceArray,
                Heights = heightArray,
                Cell = cell,
                MaxHeight = maxHeight,
                BankWidth = bankWidth,
                WaterCell = water.CellSize,
                WaterResolution = water.Resolution,
                Left = LeftWander,
                Right = RightWander
            }.Schedule(work.Count, 1).Complete();

            heightArray.CopyTo(compact);
        }
        finally
        {
            segmentArray.Dispose();
            memberArray.Dispose();
            rangeArray.Dispose();
            boundArray.Dispose();
            offsetArray.Dispose();
            lakeArray.Dispose();
            distanceArray.Dispose();
            surfaceArray.Dispose();
            heightArray.Dispose();
        }

        for (int job = 0; job < work.Count; job++)
        {
            int4 b = bounds[job];
            int width = b.z - b.x + 1;

            for (int z = b.y; z <= b.w; z++)
                System.Array.Copy(compact, offsets[job] + (z - b.y) * width, heights, z * resolution + b.x, width);
        }
    }
}

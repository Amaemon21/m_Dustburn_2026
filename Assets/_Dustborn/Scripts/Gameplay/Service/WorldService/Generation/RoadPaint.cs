using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct RoadPaintSegment
{
    public float2 From;
    public float2 To;
    public float HalfWidth;
}

public struct RoadPaint
{
    public const int SUPERSAMPLES = 2;

    public bool Enabled;
    public bool SplitVerge;
    public int GridResolution;
    public float CellSize;
    public float VergeWidth;
    public float Softness;
    public float Texel;

    public void Cover(NativeArray<RoadPaintSegment> segments, NativeArray<int> cellStart, NativeArray<int> cellItems,
        float worldX, float worldZ, out float carriage, out float verge)
    {
        carriage = 0f;
        verge = 0f;

        if (!Enabled)
            return;

        int cell = CellOf(worldX, worldZ);
        int first = cellStart[cell];
        int last = cellStart[cell + 1];

        if (first == last)
            return;

        float outer = VergeWidth + Softness * 0.5f;

        if (EdgeDistance(segments, cellItems, first, last, new float2(worldX, worldZ), outer + Texel) >= outer + Texel)
            return;

        float step = Texel / SUPERSAMPLES;
        float origin = (step - Texel) * 0.5f;

        for (int v = 0; v < SUPERSAMPLES; v++)
        {
            for (int u = 0; u < SUPERSAMPLES; u++)
            {
                var point = new float2(worldX + origin + u * step, worldZ + origin + v * step);

                Profile(EdgeDistance(segments, cellItems, first, last, point, outer), out float surface, out float shoulder);

                carriage += surface;
                verge += shoulder;
            }
        }

        float samples = SUPERSAMPLES * SUPERSAMPLES;

        carriage /= samples;
        verge /= samples;
    }

    public void Profile(float distance, out float carriage, out float verge)
    {
        float half = Softness * 0.5f;
        float road = 1f - math.saturate((distance - VergeWidth + half) / Softness);

        if (!SplitVerge)
        {
            carriage = road;
            verge = 0f;
            return;
        }

        carriage = math.min(road, 1f - math.saturate((distance + half) / Softness));
        verge = road - carriage;
    }

    private static float EdgeDistance(NativeArray<RoadPaintSegment> segments, NativeArray<int> cellItems, int first, int last,
        float2 point, float limit)
    {
        float best = limit;

        for (int i = first; i < last; i++)
        {
            RoadPaintSegment segment = segments[cellItems[i]];
            float reach = best + segment.HalfWidth;

            if (reach <= 0f)
                continue;

            float2 line = segment.To - segment.From;
            float lengthSqr = math.dot(line, line);
            float along = lengthSqr > 1e-6f ? math.saturate(math.dot(point - segment.From, line) / lengthSqr) : 0f;
            float2 delta = point - (segment.From + line * along);
            float distanceSqr = math.dot(delta, delta);

            if (distanceSqr >= reach * reach)
                continue;

            best = math.sqrt(distanceSqr) - segment.HalfWidth;
        }

        return best;
    }

    private int CellOf(float worldX, float worldZ)
    {
        int x = math.clamp((int)math.floor(worldX / CellSize), 0, GridResolution - 1);
        int z = math.clamp((int)math.floor(worldZ / CellSize), 0, GridResolution - 1);

        return z * GridResolution + x;
    }
}

public sealed class RoadPaintIndex : IDisposable
{
    private const float CELL_SIZE = 16f;
    private const float MIN_SOFTNESS_TEXELS = 2f;

    private NativeArray<RoadPaintSegment> _segments;
    private NativeArray<int> _cellStart;
    private NativeArray<int> _cellItems;

    public NativeArray<RoadPaintSegment> Segments => _segments;
    public NativeArray<int> CellStart => _cellStart;
    public NativeArray<int> CellItems => _cellItems;

    public RoadPaint Paint { get; }
    public int SegmentCount { get; }

    public RoadPaintIndex(IReadOnlyList<Road> roads, WorldGenerationConfig config, float texel, bool splitVerge)
    {
        float softness = Mathf.Max(config.RoadEdgeSoftness, texel * MIN_SOFTNESS_TEXELS);
        int resolution = Mathf.Max(1, Mathf.CeilToInt(config.WorldSize / CELL_SIZE));

        List<RoadPaintSegment> segments = Collect(roads);
        float margin = config.RoadVergeWidth + softness * 0.5f + texel;

        var starts = new int[resolution * resolution + 1];

        foreach (RoadPaintSegment segment in segments)
            Visit(segment, margin, resolution, cell => starts[cell + 1]++);

        for (int cell = 0; cell < resolution * resolution; cell++)
            starts[cell + 1] += starts[cell];

        var items = new int[Mathf.Max(1, starts[^1])];
        var cursor = new int[resolution * resolution];

        System.Array.Copy(starts, cursor, cursor.Length);

        for (int index = 0; index < segments.Count; index++)
        {
            int current = index;

            Visit(segments[index], margin, resolution, cell => items[cursor[cell]++] = current);
        }

        SegmentCount = segments.Count;

        if (segments.Count == 0)
            segments.Add(default);

        _segments = new NativeArray<RoadPaintSegment>(segments.ToArray(), Allocator.Persistent);
        _cellStart = new NativeArray<int>(starts, Allocator.Persistent);
        _cellItems = new NativeArray<int>(items, Allocator.Persistent);

        Paint = new RoadPaint
        {
            Enabled = SegmentCount > 0,
            SplitVerge = splitVerge,
            GridResolution = resolution,
            CellSize = CELL_SIZE,
            VergeWidth = config.RoadVergeWidth,
            Softness = softness,
            Texel = texel
        };
    }

    public static bool HasRoads(IReadOnlyList<Road> roads)
    {
        if (roads == null)
            return false;

        foreach (Road road in roads)
        {
            if (road?.Points != null && road.Points.Length >= 2 && road.Width > 0f)
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref _segments);
        NativeBuffer.Release(ref _cellStart);
        NativeBuffer.Release(ref _cellItems);
    }

    private static List<RoadPaintSegment> Collect(IReadOnlyList<Road> roads)
    {
        var segments = new List<RoadPaintSegment>();

        if (roads == null)
            return segments;

        foreach (Road road in roads)
        {
            if (road?.Points == null || road.Points.Length < 2 || road.Width <= 0f)
                continue;

            for (int i = 0; i < road.Points.Length - 1; i++)
            {
                segments.Add(new RoadPaintSegment
                {
                    From = new float2(road.Points[i].x, road.Points[i].y),
                    To = new float2(road.Points[i + 1].x, road.Points[i + 1].y),
                    HalfWidth = road.Width * 0.5f
                });
            }
        }

        return segments;
    }

    private static void Visit(RoadPaintSegment segment, float margin, int resolution, Action<int> visit)
    {
        float reach = segment.HalfWidth + margin;

        int minX = Cell(math.min(segment.From.x, segment.To.x) - reach, resolution);
        int maxX = Cell(math.max(segment.From.x, segment.To.x) + reach, resolution);
        int minZ = Cell(math.min(segment.From.y, segment.To.y) - reach, resolution);
        int maxZ = Cell(math.max(segment.From.y, segment.To.y) + reach, resolution);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
                visit(z * resolution + x);
        }
    }

    private static int Cell(float coordinate, int resolution)
    {
        return Mathf.Clamp(Mathf.FloorToInt(coordinate / CELL_SIZE), 0, resolution - 1);
    }
}

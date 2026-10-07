using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class WaterStampCarver
{
    public const float FLOOD_LIFT = 0.6f;

    private const float MAX_CUT = 8f;
    private const float MAX_BANK_GRADE = 0.3f;
    private const float REACH_FADE = 0.85f;
    private const float GUARD_PAD = 2f;
    private const float GUARD_SHARE = 0.5f;
    private const float TILE = 256f;
    private const int SAMPLES = 16;
    private const int STRIDE = SAMPLES + 1;

    private sealed class Sections
    {
        public float[] Reach;
        public float[] Cap;
        public float[] Shoulder;
        public float[] Left;
        public float[] Right;
    }

    private readonly struct Segment
    {
        public readonly RiverPoint A;
        public readonly RiverPoint B;
        public readonly int River;
        public readonly int Index;
        public readonly float Reach;
        public readonly float Guard;

        public float Span => Mathf.Max(Reach, Guard);

        public Segment(RiverPoint a, RiverPoint b, int river, int index, float reach)
        {
            A = a;
            B = b;
            River = river;
            Index = index;
            Reach = reach;
            Guard = 0.5f * Mathf.Max(a.Width, b.Width) * (1f + GUARD_SHARE) + GUARD_PAD + WaterMeshes.RIBBON_PAD;
        }
    }

    public static long Carve(HeightMap carved, HeightMap source, WaterMap water, WaterStampLibrary library)
    {
        WaterStampLayout layout = water.Stamps;

        if (layout == null || library == null || library.Settings.RiverInfluence <= 0f || layout.Tracks.Count != water.Rivers.Count)
            return 0;

        var sections = new Sections[water.Rivers.Count];
        bool any = false;

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            sections[river] = Measure(water.Rivers[river].Points, layout.Tracks[river], layout, library);
            any |= sections[river] != null;
        }

        if (!any)
            return 0;

        var segments = new List<Segment>();

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;
            Sections section = sections[river];

            for (int i = 0; i + 1 < points.Count; i++)
                segments.Add(new Segment(points[i], points[i + 1], river, i, section == null ? 0f : Mathf.Max(section.Reach[i], section.Reach[i + 1])));
        }

        int tiles = Mathf.CeilToInt(source.WorldSize / TILE);
        var buckets = new List<int>[tiles * tiles];

        for (int s = 0; s < segments.Count; s++)
        {
            Segment segment = segments[s];

            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(segment.A.Position.x, segment.B.Position.x) - segment.Span) / TILE), 0, tiles - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(segment.A.Position.x, segment.B.Position.x) + segment.Span) / TILE), 0, tiles - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(segment.A.Position.y, segment.B.Position.y) - segment.Span) / TILE), 0, tiles - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(segment.A.Position.y, segment.B.Position.y) + segment.Span) / TILE), 0, tiles - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                    (buckets[z * tiles + x] ??= new List<int>()).Add(s);
            }
        }

        int resolution = source.Resolution;
        float cell = (float)source.WorldSize / (resolution - 1);
        float maxHeight = source.MaxHeight;
        float[] original = source.Heights;
        float[] heights = carved.Heights;
        float influence = library.Settings.RiverInfluence;
        long changed = 0;

        Parallel.For(0, buckets.Length, () => 0L, (tile, _, count) =>
        {
            List<int> bucket = buckets[tile];

            if (bucket == null)
                return count;

            bool carving = false;

            foreach (int s in bucket)
                carving |= segments[s].Reach > 0f;

            if (!carving)
                return count;

            int tileMinX = Mathf.CeilToInt(tile % tiles * TILE / cell);
            int tileMaxX = tile % tiles == tiles - 1 ? resolution - 1 : Mathf.Min(resolution - 1, Mathf.CeilToInt((tile % tiles + 1) * TILE / cell) - 1);
            int tileMinZ = Mathf.CeilToInt(tile / tiles * TILE / cell);
            int tileMaxZ = tile / tiles == tiles - 1 ? resolution - 1 : Mathf.Min(resolution - 1, Mathf.CeilToInt((tile / tiles + 1) * TILE / cell) - 1);
            int tileWidth = tileMaxX - tileMinX + 1;
            var target = new float[tileWidth * (tileMaxZ - tileMinZ + 1)];
            var nearest = new float[target.Length];
            var guard = new float[target.Length];

            for (int i = 0; i < target.Length; i++)
            {
                target[i] = float.PositiveInfinity;
                nearest[i] = float.PositiveInfinity;
                guard[i] = float.NegativeInfinity;
            }

            foreach (int s in bucket)
                Stamp(segments[s], sections[segments[s].River], original, target, nearest, guard, resolution, cell, maxHeight, influence, tileMinX, tileMaxX, tileMinZ, tileMaxZ, tileWidth);

            for (int z = tileMinZ; z <= tileMaxZ; z++)
            {
                for (int x = tileMinX; x <= tileMaxX; x++)
                {
                    int local = (z - tileMinZ) * tileWidth + x - tileMinX;
                    float value = target[local];

                    if (float.IsPositiveInfinity(value))
                        continue;

                    int index = z * resolution + x;
                    float lowered = Mathf.Max(value, guard[local]) / maxHeight;

                    if (lowered >= heights[index])
                        continue;

                    heights[index] = lowered;
                    count++;
                }
            }

            return count;
        }, count => System.Threading.Interlocked.Add(ref changed, count));

        layout.CorridorCells += changed;
        return changed;
    }

    private static Sections Measure(List<RiverPoint> points, WaterStampTrack track, WaterStampLayout layout, WaterStampLibrary library)
    {
        if (track == null)
            return null;

        int count = points.Count;
        var sections = new Sections
        {
            Reach = new float[count],
            Cap = new float[count],
            Shoulder = new float[count],
            Left = new float[count * STRIDE],
            Right = new float[count * STRIDE]
        };

        bool stamped = false;

        for (int i = 0; i < count; i++)
        {
            sections.Shoulder[i] = 0.25f;

            if (!track.Stamped(i) || points[i].Submerged)
                continue;

            WaterStampPlacement placement = layout.Placements[track.Placement[i]];
            WaterStampDefinition definition = library.Get(placement.StampIndex);
            WaterStampShape shape = library.Shape(placement.StampIndex);

            Vector2 tangent = points[Mathf.Min(count - 1, i + 1)].Position - points[Mathf.Max(0, i - 1)].Position;

            if (tangent.sqrMagnitude < 1e-8f)
                continue;

            tangent = tangent.normalized;
            Vector2 across = placement.StampDirection(new Vector2(-tangent.y, tangent.x));
            float width = Mathf.Max(0.5f, points[i].Width);
            float ratio = definition.NominalChannelWidth / width;

            sections.Reach[i] = definition.CorridorHalfWidth / ratio;
            sections.Cap[i] = Mathf.Min(MAX_CUT, Mathf.Max(1f, sections.Reach[i] - definition.MedianCoreHalfWidth / ratio) * MAX_BANK_GRADE);
            sections.Shoulder[i] = definition.BankShoulder;

            for (int k = 0; k <= SAMPLES; k++)
            {
                float offset = definition.CorridorHalfWidth * k / SAMPLES;
                sections.Left[i * STRIDE + k] = Sample(shape, placement, track.Stamp[i] + across * offset);
                sections.Right[i * STRIDE + k] = Sample(shape, placement, track.Stamp[i] - across * offset);
            }

            stamped = true;
        }

        if (!stamped)
            return null;

        int radius = Mathf.Max(1, Mathf.RoundToInt(library.Settings.BlendDistance / Hydrology.RESAMPLE_STEP));

        Smooth(sections.Reach, 1, radius);
        Smooth(sections.Cap, 1, radius);
        Smooth(sections.Shoulder, 1, radius);
        Smooth(sections.Left, STRIDE, radius);
        Smooth(sections.Right, STRIDE, radius);

        return sections;
    }

    private static float Sample(WaterStampShape shape, WaterStampPlacement placement, Vector2 stamp)
    {
        Vector2 uv = placement.Uv(stamp);

        return shape.Sample(Mathf.Clamp01(uv.x), uv.y);
    }

    private static void Smooth(float[] values, int stride, int radius)
    {
        int count = values.Length / stride;
        var copy = (float[])values.Clone();
        float share = 1f / (2 * radius + 1);

        for (int i = 0; i < count; i++)
        {
            int from = Mathf.Max(0, i - radius), to = Mathf.Min(count - 1, i + radius);

            for (int k = 0; k < stride; k++)
            {
                float sum = 0f;

                for (int j = from; j <= to; j++)
                    sum += copy[j * stride + k];

                values[i * stride + k] = sum * share;
            }
        }
    }

    private static void Stamp(Segment segment, Sections sections, float[] original, float[] target, float[] nearest, float[] guard,
        int resolution, float cell, float maxHeight, float influence, int tileMinX, int tileMaxX, int tileMinZ, int tileMaxZ, int tileWidth)
    {
        RiverPoint a = segment.A, b = segment.B;
        Vector2 axis = b.Position - a.Position;
        float length = axis.sqrMagnitude;

        if (length < 1e-8f)
            return;

        Vector2 left = new Vector2(-axis.y, axis.x) / Mathf.Sqrt(length);

        int minX = Mathf.Max(tileMinX, Mathf.FloorToInt((Mathf.Min(a.Position.x, b.Position.x) - segment.Span) / cell));
        int maxX = Mathf.Min(tileMaxX, Mathf.CeilToInt((Mathf.Max(a.Position.x, b.Position.x) + segment.Span) / cell));
        int minZ = Mathf.Max(tileMinZ, Mathf.FloorToInt((Mathf.Min(a.Position.y, b.Position.y) - segment.Span) / cell));
        int maxZ = Mathf.Min(tileMaxZ, Mathf.CeilToInt((Mathf.Max(a.Position.y, b.Position.y) + segment.Span) / cell));

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x * cell, z * cell);
                float along = Mathf.Clamp01(Vector2.Dot(point - a.Position, axis) / length);
                Vector2 offset = point - (a.Position + axis * along);
                float distance = offset.magnitude;
                int local = (z - tileMinZ) * tileWidth + x - tileMinX;

                if (distance <= segment.Guard)
                    guard[local] = Mathf.Max(guard[local], Mathf.Lerp(a.Surface, b.Surface, along) + RiverCarver.EDGE_LIFT);

                if (distance > segment.Span || distance >= nearest[local])
                    continue;

                nearest[local] = distance;
                target[local] = float.PositiveInfinity;

                if (sections == null || distance > segment.Reach || distance <= 0.5f * Mathf.Lerp(a.Width, b.Width, along))
                    continue;

                int i = segment.Index;
                float reach = Mathf.Lerp(sections.Reach[i], sections.Reach[i + 1], along);

                if (reach <= 1e-3f || distance >= reach)
                    continue;

                float[] profile = Vector2.Dot(offset, left) >= 0f ? sections.Left : sections.Right;
                float position = distance / reach * SAMPLES;
                int k = Mathf.Min((int)position, SAMPLES - 1);
                float t = position - k;
                float first = Mathf.Lerp(profile[i * STRIDE + k], profile[i * STRIDE + k + 1], t);
                float second = Mathf.Lerp(profile[(i + 1) * STRIDE + k], profile[(i + 1) * STRIDE + k + 1], t);
                float mask = Mathf.Lerp(first, second, along);
                float shoulder = Mathf.Max(1e-3f, Mathf.Lerp(sections.Shoulder[i], sections.Shoulder[i + 1], along));
                float weight = WaterStampLibrary.SmoothStep(0f, shoulder, mask) * (1f - WaterStampLibrary.SmoothStep(REACH_FADE, 1f, distance / reach));

                if (weight <= 0f)
                    continue;

                float ground = original[z * resolution + x] * maxHeight;
                float excess = ground - (Mathf.Lerp(a.Surface, b.Surface, along) + FLOOD_LIFT);

                if (excess <= 0f)
                    continue;

                float cap = Mathf.Lerp(sections.Cap[i], sections.Cap[i + 1], along);
                target[local] = ground - Mathf.Min(excess, cap) * weight * influence;
            }
        }
    }
}

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public static partial class RiverCarver
{
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
        return Wander(half, foot, side, LeftWander, RightWander);
    }

    private static float Wander(float half, Vector2 foot, float side, float2 left, float2 right)
    {
        float amplitude = Mathf.Min(WANDER_LIMIT * half, Mathf.Clamp(WANDER_SHARE * half, MIN_WANDER, MAX_WANDER));
        float period = Mathf.Max(WANDER_PERIOD, WANDER_WIDTHS * 2f * half);
        float noise = FractalNoise.Sample01(new float2(foot.x, foot.y) / period, side > 0f ? left : right, 2, 2f, 0.5f);

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

    internal sealed class ChannelCourse
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

        public bool TaperStart => _taperStart;

        public bool TaperEnd => _taperEnd;

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

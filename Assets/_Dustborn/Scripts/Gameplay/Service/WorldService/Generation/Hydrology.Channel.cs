using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class Hydrology
{
    private float MainWidth => _settings.MainRiverWidth * _settings.RiverWidthScale;

    public float Width(float area)
    {
        return Width(area, MAX_WIDTH * _settings.RiverWidthScale);
    }

    public float Width(float area, float cap)
    {
        return Mathf.Min(cap, _settings.RiverWidthScale * NaturalWidth(Doublings(area)));
    }

    public float Depth(float area)
    {
        return _settings.RiverDepthScale * DepthAtWidth(NaturalWidth(Doublings(area)));
    }

    public static float DepthAtWidth(float width)
    {
        if (width < STREAM_WIDTH)
            return Mathf.Lerp(SOURCE_DEPTH, STREAM_DEPTH, Mathf.InverseLerp(SOURCE_WIDTH, STREAM_WIDTH, width));

        return Mathf.Min(MAX_DEPTH, STREAM_DEPTH + DEPTH_PER_DOUBLING * (width - STREAM_WIDTH) / WIDTH_PER_DOUBLING);
    }

    private float Doublings(float area)
    {
        return Mathf.Log(Mathf.Max(1f, area / (_settings.RiverStartArea * 1e6f)), 2f);
    }

    private float[] WidthScale(RiverCourse course)
    {
        float[] stamped = course.Stamp?.WidthScale;

        if (!course.Main)
            return stamped;

        float[] boost = MainBoost(course.Areas, course.MaxWidth);

        if (stamped == null)
            return boost;

        for (int i = 0; i < boost.Length && i < stamped.Length; i++)
            boost[i] *= stamped[i];

        return boost;
    }

    private float[] MainBoost(List<float> areas, float maxWidth)
    {
        float mouth = 0f;

        foreach (float area in areas)
            mouth = Mathf.Max(mouth, area);

        float natural = Width(mouth, maxWidth);
        float target = Mathf.Max(1f, maxWidth / Mathf.Max(SOURCE_WIDTH, natural));
        var boost = new float[areas.Count];

        for (int i = 0; i < areas.Count; i++)
            boost[i] = Mathf.Lerp(1f, target, Mathf.Sqrt(Mathf.Clamp01(areas[i] / Mathf.Max(1f, mouth))));

        return boost;
    }

    private static float NaturalWidth(float doublings)
    {
        if (doublings >= HEADWATER_DOUBLINGS)
            return HEADWATER_WIDTH + WIDTH_PER_DOUBLING * (doublings - HEADWATER_DOUBLINGS);

        return Mathf.Lerp(SOURCE_WIDTH, HEADWATER_WIDTH, doublings / HEADWATER_DOUBLINGS);
    }

    private float ChannelDepth(float area, float scale)
    {
        float natural = Depth(area);

        return Mathf.Clamp(natural * scale, Mathf.Min(STREAM_DEPTH, natural), MAX_DEPTH);
    }

    private static float ChannelFreeboard(float width)
    {
        return Mathf.Min(MAX_CHANNEL_FREEBOARD, CHANNEL_FREEBOARD + CHANNEL_FREEBOARD_PER_WIDTH * width);
    }

    private static float PoolDrop(float width)
    {
        return Mathf.Min(MAX_POOL_DROP, POOL_DROP + POOL_DROP_PER_WIDTH * width);
    }

    private static float Freeboard(float depth)
    {
        return 0.15f + 0.2f * Mathf.Min(depth, FREEBOARD_DEPTH);
    }
}

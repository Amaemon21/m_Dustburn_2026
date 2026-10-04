using UnityEngine;

public static class RoadProfile
{
    public const float EARTHWORK_OVERRUN = 2f;

    public static float[] Build(float[] ground, float maxFill, float maxCut, int smoothing)
    {
        float[] profile = MovingAverage(ground, smoothing);

        ClampEarthworks(profile, ground, maxFill, maxCut);

        profile = MovingAverage(profile, Mathf.Max(1, smoothing / 3));

        ClampEarthworks(profile, ground, maxFill, maxCut);

        return profile;
    }

    public static void ClampEarthworks(float[] profile, float[] ground, float maxFill, float maxCut)
    {
        for (int i = 0; i < profile.Length; i++)
            profile[i] = Mathf.Clamp(profile[i], ground[i] - maxCut, ground[i] + maxFill);
    }

    public static void Fit(float[] profile, float[] ground, float[] distance, float grade, float maxFill, float maxCut, bool[] anchored, float overrun)
    {
        int count = profile.Length;

        if (count < 2 || grade <= 0f || float.IsInfinity(grade))
            return;

        float[] lower = Bounds(profile, ground, anchored, -maxCut);
        float[] upper = Bounds(profile, ground, anchored, maxFill);
        float[] capLower = Bounds(profile, ground, anchored, -maxCut * overrun);
        float[] capUpper = Bounds(profile, ground, anchored, maxFill * overrun);

        Close(lower, distance, grade, true);
        Close(upper, distance, grade, false);
        Close(capLower, distance, grade, true);
        Close(capUpper, distance, grade, false);

        for (int i = 0; i < count; i++)
        {
            float low = Mathf.Max(Mathf.Min(lower[i], upper[i]), capLower[i]);
            float high = Mathf.Min(Mathf.Max(lower[i], upper[i]), capUpper[i]);

            if (low > high)
            {
                float middle = (low + high) * 0.5f;
                low = middle;
                high = middle;
            }

            lower[i] = low;
            upper[i] = high;
        }

        var forward = new float[count];
        var backward = new float[count];

        forward[0] = Mathf.Clamp(profile[0], lower[0], upper[0]);

        for (int i = 1; i < count; i++)
        {
            float reach = grade * (distance[i] - distance[i - 1]);
            forward[i] = Mathf.Clamp(Mathf.Clamp(profile[i], forward[i - 1] - reach, forward[i - 1] + reach), lower[i], upper[i]);
        }

        backward[count - 1] = Mathf.Clamp(profile[count - 1], lower[count - 1], upper[count - 1]);

        for (int i = count - 2; i >= 0; i--)
        {
            float reach = grade * (distance[i + 1] - distance[i]);
            backward[i] = Mathf.Clamp(Mathf.Clamp(profile[i], backward[i + 1] - reach, backward[i + 1] + reach), lower[i], upper[i]);
        }

        for (int i = 0; i < count; i++)
            profile[i] = (forward[i] + backward[i]) * 0.5f;
    }

    private static float[] Bounds(float[] profile, float[] ground, bool[] anchored, float offset)
    {
        var bounds = new float[profile.Length];

        for (int i = 0; i < bounds.Length; i++)
            bounds[i] = anchored != null && anchored[i] ? profile[i] : ground[i] + offset;

        return bounds;
    }

    private static void Close(float[] bounds, float[] distance, float grade, bool lower)
    {
        for (int i = 1; i < bounds.Length; i++)
        {
            float reach = grade * (distance[i] - distance[i - 1]);
            bounds[i] = lower ? Mathf.Max(bounds[i], bounds[i - 1] - reach) : Mathf.Min(bounds[i], bounds[i - 1] + reach);
        }

        for (int i = bounds.Length - 2; i >= 0; i--)
        {
            float reach = grade * (distance[i + 1] - distance[i]);
            bounds[i] = lower ? Mathf.Max(bounds[i], bounds[i + 1] - reach) : Mathf.Min(bounds[i], bounds[i + 1] + reach);
        }
    }

    public static void LimitGrade(float[] profile, float[] distance, float grade, bool[] anchored)
    {
        if (grade <= 0f || float.IsInfinity(grade))
            return;

        for (int i = 1; i < profile.Length; i++)
        {
            if (anchored != null && anchored[i])
                continue;

            float reach = grade * (distance[i] - distance[i - 1]);
            profile[i] = Mathf.Clamp(profile[i], profile[i - 1] - reach, profile[i - 1] + reach);
        }

        for (int i = profile.Length - 2; i >= 0; i--)
        {
            if (anchored != null && anchored[i])
                continue;

            float reach = grade * (distance[i + 1] - distance[i]);
            profile[i] = Mathf.Clamp(profile[i], profile[i + 1] - reach, profile[i + 1] + reach);
        }
    }

    public static float[] MovingAverage(float[] values, int window)
    {
        int count = values.Length;
        var result = new float[count];

        if (count == 0)
            return result;

        var prefix = new double[count + 1];

        for (int i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + values[i];

        int samples = 2 * window + 1;

        for (int i = 0; i < count; i++)
        {
            int low = i - window;
            int high = i + window;
            double sum = prefix[Mathf.Min(high, count - 1) + 1] - prefix[Mathf.Max(low, 0)];

            if (low < 0)
                sum += -low * (double)values[0];

            if (high > count - 1)
                sum += (high - count + 1) * (double)values[count - 1];

            result[i] = (float)(sum / samples);
        }

        return result;
    }
}

using UnityEngine;

public readonly struct DecorTint
{
    public const int LEVELS = 3;

    public Color Color { get; }
    public float Variance { get; }
    public float SnowMin { get; }
    public float SnowMax { get; }
    public float SelectionVariance => Mathf.Max(Variance, SnowMax > SnowMin ? 0.01f : 0f);

    public DecorTint(Color color, float variance, float snowMin = 0f, float snowMax = 0f)
    {
        Color = color;
        Variance = Mathf.Max(0f, variance);
        SnowMin = Mathf.Clamp01(snowMin);
        SnowMax = Mathf.Clamp(snowMax, SnowMin, 1f);
    }

    public static DecorTint Of(VoxelDecorLayer layer)
    {
        return new DecorTint(layer.Tint, layer.TintVariance, layer.SnowMin, layer.SnowMax);
    }

    public bool IsColorNeutral => Variance <= 0f && Color.r == 1f && Color.g == 1f && Color.b == 1f;
    public bool IsNeutral => IsColorNeutral && SnowMax <= 0f;

    public Color Exact(float shade)
    {
        float selection = SelectionVariance;
        return Scaled(selection == Variance ? shade : 1f + (shade - 1f) * Variance / selection);
    }

    public int Level(float shade)
    {
        float selection = SelectionVariance;
        if (selection <= 0f)
            return LEVELS / 2;

        float position = (shade - (1f - selection)) / (2f * selection);

        return Mathf.Clamp((int)(position * LEVELS), 0, LEVELS - 1);
    }

    public float LevelShade(int level)
    {
        return 1f + Variance * ((2f * level + 1f) / LEVELS - 1f);
    }

    public Color Stepped(int level)
    {
        return Scaled(LevelShade(level));
    }

    public float Snow(int level) => Mathf.Lerp(SnowMin, SnowMax, (level + 0.5f) / LEVELS);

    public bool SameAppearance(DecorTint other)
    {
        return Color.r == other.Color.r && Color.g == other.Color.g && Color.b == other.Color.b && Color.a == other.Color.a
            && Variance == other.Variance && SnowMin == other.SnowMin && SnowMax == other.SnowMax;
    }

    private Color Scaled(float shade)
    {
        return new Color(Color.r * shade, Color.g * shade, Color.b * shade, Color.a);
    }
}

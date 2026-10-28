using UnityEngine;

public readonly struct DecorTint
{
    public const int LEVELS = 3;

    public Color Color { get; }
    public float Variance { get; }

    public DecorTint(Color color, float variance)
    {
        Color = color;
        Variance = Mathf.Max(0f, variance);
    }

    public static DecorTint Of(VoxelDecorLayer layer)
    {
        return new DecorTint(layer.Tint, layer.TintVariance);
    }

    public bool IsNeutral => Variance <= 0f && Color.r == 1f && Color.g == 1f && Color.b == 1f;

    public Color Exact(float shade)
    {
        return Scaled(shade);
    }

    public int Level(float shade)
    {
        if (Variance <= 0f)
            return LEVELS / 2;

        float position = (shade - (1f - Variance)) / (2f * Variance);

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

    private Color Scaled(float shade)
    {
        return new Color(Color.r * shade, Color.g * shade, Color.b * shade, Color.a);
    }
}

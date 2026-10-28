using System;
using UnityEngine;

public enum WorldAmount
{
    None,
    Few,
    Normal,
    Many
}

public enum WorldRelief
{
    Plains,
    Rolling,
    Hills
}

[Serializable]
public sealed class WorldVariation
{
    public const int REFERENCE_SIZE = 8192;
    public const int SIZE_STEP = 1024;
    public const int MIN_SIZE = 2048;
    public const int MAX_SIZE = 10240;

    private const uint FNV_OFFSET = 2166136261;
    private const uint FNV_PRIME = 16777619;

    public int Seed;
    public int WorldSize = REFERENCE_SIZE;
    public WorldAmount Towns = WorldAmount.Normal;
    public WorldAmount Farmsteads = WorldAmount.Normal;
    public WorldAmount Rivers = WorldAmount.Normal;
    public WorldAmount Lakes = WorldAmount.Normal;
    public WorldAmount Mountains = WorldAmount.Normal;
    public WorldRelief Relief = WorldRelief.Hills;

    public float SizeScale => WorldSize / (float)REFERENCE_SIZE;
    public float AreaScale => SizeScale * SizeScale;

    public static int SeedOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        string trimmed = text.Trim();
        if (int.TryParse(trimmed, out int number))
            return number;

        uint hash = FNV_OFFSET;
        foreach (char symbol in trimmed)
        {
            hash ^= symbol;
            hash *= FNV_PRIME;
        }

        return (int)(hash & int.MaxValue);
    }

    public static int ClampSize(int size)
    {
        int stepped = Mathf.RoundToInt(size / (float)SIZE_STEP) * SIZE_STEP;
        return Mathf.Clamp(stepped, MIN_SIZE, MAX_SIZE);
    }

    public void Validate()
    {
        if (WorldSize != ClampSize(WorldSize))
            throw new ArgumentOutOfRangeException(nameof(WorldSize), WorldSize, $"World size must be a multiple of {SIZE_STEP} between {MIN_SIZE} and {MAX_SIZE}");
    }

    public static float Factor(WorldAmount amount, float few, float many)
    {
        return amount switch
        {
            WorldAmount.None => 0f,
            WorldAmount.Few => few,
            WorldAmount.Many => many,
            _ => 1f
        };
    }
}

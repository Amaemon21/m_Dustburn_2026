using System;
using UnityEngine;

public enum WorldAmount
{
    None,
    Few,
    Normal,
    Many
}

public enum BiomeLayout
{
    Random,
    ForestCenter,
    WastelandCenter,
    Rings,
    Stripes
}

[Serializable]
public sealed class WorldVariation
{
    public const int VERSION = 2;
    public const int REFERENCE_SIZE = 8192;
    public const int SIZE_STEP = 1024;
    public const int MIN_SIZE = 2048;
    public const int MAX_SIZE = 10240;
    public const int SHARE_STEP = 5;
    public const int MAX_MOUNTAINS = 60;

    private const uint FNV_OFFSET = 2166136261;
    private const uint FNV_PRIME = 16777619;

    public int Seed;
    public int WorldSize = REFERENCE_SIZE;
    public WorldAmount Towns = WorldAmount.Normal;
    public WorldAmount Wilderness = WorldAmount.Normal;
    public WorldAmount Rivers = WorldAmount.Normal;
    public WorldAmount Lakes = WorldAmount.Normal;
    public WorldAmount Craters = WorldAmount.Few;
    public WorldAmount Rifts = WorldAmount.Few;
    public WorldAmount Canyons = WorldAmount.Few;
    public int Plains = 30;
    public int Hills = 45;
    public int Mountains = 25;
    public int PineForest = 35;
    public int BurntForest = 15;
    public int Desert = 20;
    public int Snow = 15;
    public int Wasteland = 15;
    public BiomeLayout Layout = BiomeLayout.Random;

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

        CheckAmount(Towns, nameof(Towns));
        CheckAmount(Wilderness, nameof(Wilderness));
        CheckAmount(Rivers, nameof(Rivers));
        CheckAmount(Lakes, nameof(Lakes));
        CheckAmount(Craters, nameof(Craters));
        CheckAmount(Rifts, nameof(Rifts));
        CheckAmount(Canyons, nameof(Canyons));

        if (!Enum.IsDefined(typeof(BiomeLayout), Layout))
            throw new ArgumentOutOfRangeException(nameof(Layout), Layout, "Unknown biome layout");

        CheckShares(nameof(Plains), Plains, Hills, Mountains);
        CheckShares(nameof(PineForest), PineForest, BurntForest, Desert, Snow, Wasteland);
    }

    public ReliefShares Relief()
    {
        float plains = Math.Max(0, Plains);
        float hills = Math.Max(0, Hills);
        float mountains = Math.Max(0, Mountains);
        float total = plains + hills + mountains;

        if (total <= 0f)
            return new ReliefShares(1f / 3f, 1f / 3f, 1f / 3f);

        plains /= total;
        hills /= total;
        mountains /= total;

        float cap = MAX_MOUNTAINS / 100f;
        if (mountains > cap)
        {
            float excess = mountains - cap;
            mountains = cap;
            float low = plains + hills;

            if (low <= 0f)
                hills = excess;
            else
            {
                plains += excess * plains / low;
                hills += excess * hills / low;
            }
        }

        return new ReliefShares(plains, hills, mountains);
    }

    public float[] BiomeShares()
    {
        int[] raw = { PineForest, BurntForest, Desert, Snow, Wasteland };
        var shares = new float[raw.Length];
        float total = 0f;

        for (int i = 0; i < raw.Length; i++)
        {
            shares[i] = Math.Max(0, raw[i]);
            total += shares[i];
        }

        for (int i = 0; i < shares.Length; i++)
            shares[i] = total > 0f ? shares[i] / total : 1f / shares.Length;

        return shares;
    }

    public WorldVariation Clone() => (WorldVariation)MemberwiseClone();

    public bool SameWorld(WorldVariation other)
    {
        if (other == null)
            return false;

        return Seed == other.Seed && WorldSize == other.WorldSize && Towns == other.Towns && Wilderness == other.Wilderness
            && Rivers == other.Rivers && Lakes == other.Lakes && Craters == other.Craters && Rifts == other.Rifts && Canyons == other.Canyons
            && Plains == other.Plains && Hills == other.Hills && Mountains == other.Mountains
            && PineForest == other.PineForest && BurntForest == other.BurntForest && Desert == other.Desert && Snow == other.Snow
            && Wasteland == other.Wasteland && Layout == other.Layout;
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

    public static int Count(WorldAmount amount, int normal, float areaScale)
    {
        if (amount == WorldAmount.None)
            return 0;

        float count = normal * Factor(amount, 0.4f, 2f) * areaScale;
        return Math.Max(1, (int)Math.Round(count, MidpointRounding.AwayFromZero));
    }

    private static void CheckAmount(WorldAmount amount, string name)
    {
        if (!Enum.IsDefined(typeof(WorldAmount), amount))
            throw new ArgumentOutOfRangeException(name, amount, "Unknown amount");
    }

    private static void CheckShares(string name, params int[] shares)
    {
        int total = 0;

        foreach (int share in shares)
        {
            if (share < 0 || share > 100)
                throw new ArgumentOutOfRangeException(name, share, "A share is a percentage between 0 and 100");

            total += share;
        }

        if (total <= 0)
            throw new ArgumentOutOfRangeException(name, total, "At least one share must be above zero");
    }
}

public readonly struct ReliefShares
{
    public readonly float Plains;
    public readonly float Hills;
    public readonly float Mountains;

    public ReliefShares(float plains, float hills, float mountains)
    {
        Plains = plains;
        Hills = hills;
        Mountains = mountains;
    }
}

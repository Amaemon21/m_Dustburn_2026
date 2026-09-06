using UnityEngine;

public enum DecorKind
{
    Tree,
    Rock,
    Grass
}

public enum DecorScope
{
    All,
    Trees,
    Rocks,
    Grass
}

public readonly struct DecorSurface
{
    public Vector2 Origin { get; }
    public float Span { get; }
    public float VoxelSize { get; }
    public int Morph { get; }

    public DecorSurface(Vector2 origin, float span, float voxelSize, int morph)
    {
        Origin = origin;
        Span = span;
        VoxelSize = voxelSize;
        Morph = morph;
    }

    public bool IsPlain => Span <= 0f;
}

public class VoxelDecorLayer
{
    public GameObject Prefab { get; private set; }
    public Texture2D Card { get; private set; }
    public DecorKind Kind { get; private set; }
    public int Biome { get; private set; }

    public string Name => Card == null ? Prefab.name : Card.name;

    public float Spacing { get; private set; }
    public float Chance { get; private set; }

    public float PatchFrequency { get; private set; }
    public float PatchThreshold { get; private set; }

    public float MaxSlope { get; private set; }
    public float MinHeight { get; private set; }
    public float MaxHeight { get; private set; }

    public float MinScale { get; private set; }
    public float MaxScale { get; private set; }
    public float Squash { get; private set; }
    public float HeightScale { get; private set; }

    public Color Tint { get; private set; }
    public float TintVariance { get; private set; }

    public float Footprint { get; private set; }
    public float RoadClearance { get; private set; }

    public static VoxelDecorLayer FromScatter(int biome, ScatterLayer layer, DecorKind kind)
    {
        return new VoxelDecorLayer
        {
            Prefab = layer.Prefab,
            Kind = kind,
            Biome = biome,
            Spacing = layer.Spacing,
            Chance = layer.Chance,
            PatchFrequency = layer.PatchFrequency,
            PatchThreshold = layer.PatchThreshold,
            MaxSlope = layer.MaxSlope,
            MinHeight = layer.MinHeight,
            MaxHeight = layer.MaxHeight,
            MinScale = layer.MinScale,
            MaxScale = layer.MaxScale,
            Squash = layer.Squash,
            HeightScale = 1f,
            Tint = layer.Tint,
            TintVariance = layer.TintVariance,
            Footprint = layer.Footprint,
            RoadClearance = layer.RoadClearance
        };
    }

    public static VoxelDecorLayer FromGrass(int biome, GrassLayer layer)
    {
        float spacing = 0.8f / Mathf.Sqrt(Mathf.Max(0.0001f, layer.Density));

        return new VoxelDecorLayer
        {
            Prefab = layer.Card == null ? layer.Prefab : null,
            Card = layer.Card,
            Kind = DecorKind.Grass,
            Biome = biome,
            Spacing = spacing,
            Chance = Mathf.Clamp01(layer.Density * spacing * spacing),
            PatchFrequency = layer.PatchFrequency,
            PatchThreshold = layer.PatchThreshold,
            MaxSlope = layer.MaxSlope,
            MinHeight = 0f,
            MaxHeight = 1f,
            MinScale = layer.MinWidth,
            MaxScale = layer.MaxWidth,
            Squash = 0f,
            HeightScale = HeightRatio(layer),
            Tint = layer.HealthyColor,
            TintVariance = layer.NoiseSpread * 0.3f,
            Footprint = spacing * 0.5f,
            RoadClearance = 0f
        };
    }

    private static float HeightRatio(GrassLayer layer)
    {
        float width = Mathf.Max(0.01f, (layer.MinWidth + layer.MaxWidth) * 0.5f);

        return (layer.MinHeight + layer.MaxHeight) * 0.5f / width;
    }
}

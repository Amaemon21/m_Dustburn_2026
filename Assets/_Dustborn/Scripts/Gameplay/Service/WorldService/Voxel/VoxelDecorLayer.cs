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

public readonly struct GrassCardStyle : System.IEquatable<GrassCardStyle>
{
    public Texture2D Card { get; }
    public Texture2D Mask { get; }

    public Color Leaves { get; }
    public Color Stems { get; }
    public Color Flowers { get; }
    public Color TipTint { get; }

    public float SnowAmount { get; }
    public float RootDarkening { get; }
    public float TextureShading { get; }
    public float Translucency { get; }
    public float WrapLight { get; }
    public float Variation { get; }

    public float WindStrength { get; }
    public float WindSpeed { get; }
    public float Flutter { get; }
    public float Roundness { get; }
    public float NormalUp { get; }

    public GrassCardStyle(Texture2D card, Texture2D mask, GrassLayer layer)
    {
        Card = card;
        Mask = mask;
        Leaves = layer.LeavesColor;
        Stems = layer.StemsColor;
        Flowers = layer.FlowersColor;
        TipTint = layer.TipTint;
        SnowAmount = layer.SnowAmount;
        RootDarkening = layer.RootDarkening;
        TextureShading = layer.TextureShading;
        Translucency = layer.Translucency;
        WrapLight = layer.WrapLight;
        Variation = layer.ColorVariation;
        WindStrength = layer.WindStrength;
        WindSpeed = layer.WindSpeed;
        Flutter = layer.Flutter;
        Roundness = layer.Roundness;
        NormalUp = layer.NormalUp;
    }

    public GrassCardStyle(Texture2D card, Color tint)
    {
        Card = card;
        Mask = null;
        Leaves = tint;
        Stems = tint;
        Flowers = tint;
        TipTint = Color.white;
        SnowAmount = 0f;
        RootDarkening = 0.15f;
        TextureShading = 0.85f;
        Translucency = 0.18f;
        WrapLight = 0.25f;
        Variation = 0.08f;
        WindStrength = 0.08f;
        WindSpeed = 1.4f;
        Flutter = 0.0025f;
        Roundness = 0.7f;
        NormalUp = 0.3f;
    }

    public bool Equals(GrassCardStyle other)
    {
        return Card == other.Card && Mask == other.Mask
            && Same(Leaves, other.Leaves) && Same(Stems, other.Stems)
            && Same(Flowers, other.Flowers) && Same(TipTint, other.TipTint)
            && SnowAmount == other.SnowAmount && RootDarkening == other.RootDarkening
            && TextureShading == other.TextureShading && Translucency == other.Translucency
            && WrapLight == other.WrapLight && Variation == other.Variation
            && WindStrength == other.WindStrength && WindSpeed == other.WindSpeed
            && Flutter == other.Flutter && Roundness == other.Roundness && NormalUp == other.NormalUp;
    }

    public override bool Equals(object other)
    {
        return other is GrassCardStyle style && Equals(style);
    }

    public override int GetHashCode()
    {
        int hash = Card == null ? 0 : Card.GetHashCode();

        hash = hash * 397 ^ (Mask == null ? 0 : Mask.GetHashCode());
        hash = hash * 397 ^ Leaves.r.GetHashCode() ^ Leaves.g.GetHashCode() ^ Leaves.b.GetHashCode();
        hash = hash * 397 ^ SnowAmount.GetHashCode() ^ WindStrength.GetHashCode() ^ Variation.GetHashCode();

        return hash;
    }

    private static bool Same(Color left, Color right)
    {
        return left.r == right.r && left.g == right.g && left.b == right.b;
    }
}

public class VoxelDecorLayer
{
    public GameObject Prefab { get; private set; }
    public Texture2D Card { get; private set; }
    public Texture2D Mask { get; private set; }
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
    public GrassCardStyle Style { get; private set; }

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
            Mask = layer.Mask,
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
            Tint = layer.LeavesColor,
            TintVariance = layer.ColorVariation,
            Style = new GrassCardStyle(layer.Card, layer.Mask, layer),
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

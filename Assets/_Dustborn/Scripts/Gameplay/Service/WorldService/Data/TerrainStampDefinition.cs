using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public enum TerrainStampOperation
{
    Add,
    Subtract
}

public enum TerrainStampCategory
{
    Peak,
    Ridge,
    Chain,
    Massif,
    Foothills,
    Pass,
    Plateau,
    Volcano,
    Cirque,
    Escarpment,
    Valley,
    Canyon,
    Ravines,
    Basin,
    Dunes,
    Badlands
}

[Serializable]
public class TerrainStampDefinition
{
    [field: SerializeField] public string Name { get; private set; }

    [field: SerializeField]
    [field: Tooltip("RAW16 height data: uint16 little-endian, no header, NativeResolution squared samples, top row first. Zero is zero displacement, 65535 is the full amplitude.")]
    public TextAsset HeightData { get; private set; }

    [field: SerializeField] public TerrainStampOperation Operation { get; private set; }

    [field: SerializeField, MinValue(2)] public int NativeResolution { get; private set; } = 1024;

    [field: SerializeField]
    [field: Tooltip("Footprint in world metres along local X and Z. The pixel size of the map says nothing about it.")]
    public Vector2 RecommendedSize { get; private set; } = new(1500f, 1500f);

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Displacement in metres at a white sample, before TerrainStampSettings.AmplitudeScale.")]
    public float RecommendedAmplitude { get; private set; } = 100f;

    [field: SerializeField] public TerrainStampCategory Category { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Biomes this stamp belongs to. Elsewhere it is picked with OffBiomeWeight of the settings.")]
    public List<BiomeType> PreferredBiomes { get; private set; } = new();

    [field: SerializeField, Range(0f, 4f)] public float Weight { get; private set; } = 1f;

    public TerrainStampDefinition(string name, TextAsset heightData, TerrainStampOperation operation, int nativeResolution,
        Vector2 recommendedSize, float recommendedAmplitude, TerrainStampCategory category, IEnumerable<BiomeType> preferredBiomes)
    {
        Name = name;
        HeightData = heightData;
        Operation = operation;
        NativeResolution = nativeResolution;
        RecommendedSize = recommendedSize;
        RecommendedAmplitude = recommendedAmplitude;
        Category = category;
        PreferredBiomes = new List<BiomeType>(preferredBiomes);
    }

    public long ExpectedBytes => (long)NativeResolution * NativeResolution * sizeof(ushort);

    public bool IsValid => HeightData != null && NativeResolution >= 2 && HeightData.dataSize == ExpectedBytes
        && RecommendedSize.x > 0f && RecommendedSize.y > 0f;
}

public static class TerrainStampCategories
{
    private static readonly (string Keyword, TerrainStampCategory Category)[] Keywords =
    {
        ("massif", TerrainStampCategory.Massif),
        ("chain", TerrainStampCategory.Chain),
        ("peak", TerrainStampCategory.Peak),
        ("ridge", TerrainStampCategory.Ridge),
        ("foothill", TerrainStampCategory.Foothills),
        ("pass", TerrainStampCategory.Pass),
        ("mesa", TerrainStampCategory.Plateau),
        ("plateau", TerrainStampCategory.Plateau),
        ("volcano", TerrainStampCategory.Volcano),
        ("caldera", TerrainStampCategory.Volcano),
        ("cirque", TerrainStampCategory.Cirque),
        ("escarpment", TerrainStampCategory.Escarpment),
        ("valley", TerrainStampCategory.Valley),
        ("canyon", TerrainStampCategory.Canyon),
        ("ravine", TerrainStampCategory.Ravines),
        ("basin", TerrainStampCategory.Basin),
        ("dune", TerrainStampCategory.Dunes),
        ("badland", TerrainStampCategory.Badlands)
    };

    public static TerrainStampCategory FromName(string name)
    {
        string lower = name.ToLowerInvariant();

        foreach ((string keyword, TerrainStampCategory category) in Keywords)
        {
            if (lower.Contains(keyword))
                return category;
        }

        return TerrainStampCategory.Foothills;
    }

    public static BiomeType[] DefaultBiomes(TerrainStampCategory category)
    {
        return category switch
        {
            TerrainStampCategory.Peak => new[] { BiomeType.Snow },
            TerrainStampCategory.Massif => new[] { BiomeType.Snow },
            TerrainStampCategory.Cirque => new[] { BiomeType.Snow },
            TerrainStampCategory.Chain => new[] { BiomeType.Snow, BiomeType.PineForest },
            TerrainStampCategory.Pass => new[] { BiomeType.PineForest },
            TerrainStampCategory.Ridge => new[] { BiomeType.PineForest, BiomeType.BurntForest },
            TerrainStampCategory.Foothills => new[] { BiomeType.PineForest, BiomeType.BurntForest },
            TerrainStampCategory.Plateau => new[] { BiomeType.Desert },
            TerrainStampCategory.Dunes => new[] { BiomeType.Desert },
            TerrainStampCategory.Escarpment => new[] { BiomeType.Desert },
            TerrainStampCategory.Badlands => new[] { BiomeType.Desert, BiomeType.Wasteland },
            TerrainStampCategory.Ravines => new[] { BiomeType.Wasteland, BiomeType.BurntForest },
            TerrainStampCategory.Canyon => new[] { BiomeType.Wasteland },
            TerrainStampCategory.Basin => new[] { BiomeType.Wasteland, BiomeType.BurntForest },
            TerrainStampCategory.Valley => new[] { BiomeType.PineForest, BiomeType.BurntForest },
            TerrainStampCategory.Volcano => new[] { BiomeType.Wasteland, BiomeType.Snow },
            _ => Array.Empty<BiomeType>()
        };
    }
}

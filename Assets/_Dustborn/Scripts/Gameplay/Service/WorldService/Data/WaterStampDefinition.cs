using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public enum WaterStampKind
{
    River,
    Lake,
    Pond
}

public enum WaterStampCategory
{
    StraightRiver,
    GentleCurve,
    StrongCurve,
    SMeander,
    WideMeander,
    NarrowMeander,
    CanyonRiver,
    RiverSource,
    RiverMouth,
    LakeOutlet,
    TributaryJoin,
    RiverFork,
    SmallIrregularLake,
    MediumIrregularLake,
    LargeIrregularLake,
    LongLake,
    CrescentLake,
    BranchingLake,
    MountainLake,
    BasinLake,
    Pond,
    IrregularPond
}

public enum WaterStampBranchKind
{
    TributaryIn,
    BranchOut
}

[Serializable]
public class WaterStampBranch
{
    [field: SerializeField] public WaterStampBranchKind Kind { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Socket on the stamp border in normalized UV, top row first.")]
    public Vector2 Position { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Flow direction at the socket in UV. A tributary flows in along it, a fork branch flows out along it.")]
    public Vector2 Direction { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Channel centreline in stamp metres at scale one, traced from the mask on import. A tributary runs socket to junction, a fork branch junction to socket.")]
    public Vector2[] Path { get; private set; } = Array.Empty<Vector2>();

    [field: SerializeField]
    [field: Tooltip("Index of the main centreline point where this branch meets the main channel.")]
    public int JunctionIndex { get; private set; }

    public WaterStampBranch(WaterStampBranchKind kind, Vector2 position, Vector2 direction)
    {
        Kind = kind;
        Position = position;
        Direction = direction;
    }

    public void SetPath(Vector2[] path, int junctionIndex)
    {
        Path = path;
        JunctionIndex = junctionIndex;
    }
}

[Serializable]
public class WaterStampDefinition
{
    [field: SerializeField] public int Id { get; private set; }
    [field: SerializeField] public string Name { get; private set; }
    [field: SerializeField] public WaterStampKind Kind { get; private set; }
    [field: SerializeField] public WaterStampCategory Category { get; private set; }

    [field: SerializeField]
    [field: Tooltip("RAW16 mask: uint16 little-endian, no header, NativeResolution squared samples, top row first. Zero is untouched ground, 65535 the channel or basin core.")]
    public TextAsset MaskData { get; private set; }

    [field: SerializeField, MinValue(2)] public int NativeResolution { get; private set; } = 1024;

    [field: SerializeField]
    [field: Tooltip("Footprint in world metres along stamp U and V at scale one.")]
    public Vector2 RecommendedSize { get; private set; } = new(900f, 900f);

    [field: SerializeField, MinValue(0f)] public float RecommendedDepth { get; private set; } = 2f;
    [field: SerializeField, MinValue(0f)] public float RecommendedBankWidth { get; private set; } = 20f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Water width in metres the river stamp was authored for. Zero for lakes and ponds.")]
    public float NominalChannelWidth { get; private set; }

    [field: SerializeField] public Vector2 Entry { get; private set; }
    [field: SerializeField] public Vector2 Exit { get; private set; }
    [field: SerializeField] public List<WaterStampBranch> Branches { get; private set; } = new();

    [field: SerializeField] public bool AllowMirror { get; private set; } = true;
    [field: SerializeField, MinValue(0.05f)] public float MinScale { get; private set; } = 0.75f;
    [field: SerializeField, MinValue(0.05f)] public float MaxScale { get; private set; } = 1.35f;
    [field: SerializeField, Range(0f, 4f)] public float Weight { get; private set; } = 1f;
    [field: SerializeField] public List<BiomeType> PreferredBiomes { get; private set; } = new();
    [field: SerializeField] public string Sha256 { get; private set; }

    [field: SerializeField, Foldout("Traced geometry")]
    [field: Tooltip("Main channel centreline in stamp metres at scale one, entry to exit, traced from the mask on import.")]
    public Vector2[] Centerline { get; private set; } = Array.Empty<Vector2>();

    [field: SerializeField, Foldout("Traced geometry")]
    [field: Tooltip("Half-width of the mask core at every centreline point, in stamp metres.")]
    public float[] CoreHalfWidths { get; private set; } = Array.Empty<float>();

    [field: SerializeField, Foldout("Traced geometry")] public float MedianCoreHalfWidth { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float CorridorHalfWidth { get; private set; }

    [field: SerializeField, Foldout("Traced geometry")]
    [field: Tooltip("Mask value just outside the core. Everything from it up counts as floodplain.")]
    public float BankShoulder { get; private set; } = 0.25f;

    [field: SerializeField, Foldout("Traced geometry")] public float ChordLength { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float PathLength { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float MaxDeviation { get; private set; }

    [field: SerializeField, Foldout("Traced geometry")] public float WaterArea { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public Vector2 WaterCentroid { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float WaterAxis { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float WaterMajor { get; private set; }
    [field: SerializeField, Foldout("Traced geometry")] public float WaterMinor { get; private set; }

    public WaterStampDefinition(int id, string name, WaterStampKind kind, WaterStampCategory category, TextAsset maskData, int nativeResolution,
        Vector2 recommendedSize, float recommendedDepth, float recommendedBankWidth, float nominalChannelWidth, Vector2 entry, Vector2 exit,
        IEnumerable<WaterStampBranch> branches, bool allowMirror, float minScale, float maxScale, float weight, IEnumerable<BiomeType> preferredBiomes, string sha256)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Category = category;
        MaskData = maskData;
        NativeResolution = nativeResolution;
        RecommendedSize = recommendedSize;
        RecommendedDepth = recommendedDepth;
        RecommendedBankWidth = recommendedBankWidth;
        NominalChannelWidth = nominalChannelWidth;
        Entry = entry;
        Exit = exit;
        Branches = new List<WaterStampBranch>(branches);
        AllowMirror = allowMirror;
        MinScale = minScale;
        MaxScale = maxScale;
        Weight = weight;
        PreferredBiomes = new List<BiomeType>(preferredBiomes);
        Sha256 = sha256;
    }

    public long ExpectedBytes => (long)NativeResolution * NativeResolution * sizeof(ushort);

    public bool IsRiver => Kind == WaterStampKind.River;

    public bool HasMask => MaskData != null && NativeResolution >= 2 && MaskData.dataSize == ExpectedBytes;

    public bool IsTraced => IsRiver
        ? Centerline != null && Centerline.Length >= 2 && CoreHalfWidths != null && CoreHalfWidths.Length == Centerline.Length && ChordLength > 0f
        : WaterArea > 0f && WaterMajor > 0f && WaterMinor > 0f;

    public bool IsValid => HasMask && IsTraced && RecommendedSize.x > 0f && RecommendedSize.y > 0f && MinScale > 0f && MaxScale >= MinScale
        && (!IsRiver || NominalChannelWidth > 0f);

    public Vector2 ToStamp(Vector2 uv)
    {
        return new Vector2(uv.x * RecommendedSize.x, uv.y * RecommendedSize.y);
    }

    public Vector2 ToUv(Vector2 stamp)
    {
        return new Vector2(stamp.x / RecommendedSize.x, stamp.y / RecommendedSize.y);
    }

    public WaterStampBranch Branch(WaterStampBranchKind kind)
    {
        foreach (WaterStampBranch branch in Branches)
        {
            if (branch.Kind == kind && branch.Path != null && branch.Path.Length >= 2)
                return branch;
        }

        return null;
    }

    public void SetRiverTrace(Vector2[] centerline, float[] coreHalfWidths, float corridorHalfWidth, float bankShoulder)
    {
        Centerline = centerline;
        CoreHalfWidths = coreHalfWidths;
        CorridorHalfWidth = corridorHalfWidth;
        BankShoulder = bankShoulder;

        var sorted = (float[])coreHalfWidths.Clone();
        Array.Sort(sorted);
        MedianCoreHalfWidth = sorted.Length == 0 ? 0f : sorted[sorted.Length / 2];

        Vector2 start = centerline[0];
        Vector2 end = centerline[^1];
        Vector2 axis = end - start;
        float chord = axis.magnitude;
        float length = 0f;
        float deviation = 0f;

        for (int i = 0; i < centerline.Length; i++)
        {
            if (i > 0)
                length += Vector2.Distance(centerline[i - 1], centerline[i]);

            if (chord > 1e-4f)
                deviation = Mathf.Max(deviation, Mathf.Abs(axis.x * (centerline[i].y - start.y) - axis.y * (centerline[i].x - start.x)) / chord);
        }

        ChordLength = chord;
        PathLength = length;
        MaxDeviation = deviation;
    }

    public void SetLakeTrace(float waterArea, Vector2 waterCentroid, float waterAxis, float waterMajor, float waterMinor)
    {
        WaterArea = waterArea;
        WaterCentroid = waterCentroid;
        WaterAxis = waterAxis;
        WaterMajor = waterMajor;
        WaterMinor = waterMinor;
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class WorldPreviewSettlement
{
    public SettlementType Type { get; }
    public Vector2 Center { get; }
    public IReadOnlyList<Vector2[]> Tiles { get; }

    public WorldPreviewSettlement(SettlementType type, Vector2 center, IReadOnlyList<Vector2[]> tiles)
    {
        Type = type;
        Center = center;
        Tiles = tiles;
    }
}

public sealed class WorldPreviewSource
{
    public const int WATER_RESOLUTION = 2048;
    public const int HEIGHT_RESOLUTION = 2048;

    public int WorldSize { get; }
    public HeightMap Heights { get; }
    public BiomeMap Biomes { get; }
    public Color32[] BiomeColors { get; }
    public BiomeType[] BiomeTypes { get; }
    public byte[] WaterKinds { get; }
    public float[] WaterSurface { get; }
    public int WaterResolution { get; }
    public IReadOnlyList<Road> Roads { get; }
    public IReadOnlyList<PoiPlacement> Buildings { get; }
    public IReadOnlyList<WorldPreviewSettlement> Settlements { get; }
    public WorldGameplayPlan Gameplay { get; private set; }

    public float WaterCell => WorldSize / (float)WaterResolution;

    private WorldPreviewSource(int worldSize, HeightMap heights, BiomeMap biomes, Color32[] biomeColors, BiomeType[] biomeTypes,
        byte[] waterKinds, float[] waterSurface, int waterResolution, IReadOnlyList<Road> roads, IReadOnlyList<PoiPlacement> buildings,
        IReadOnlyList<WorldPreviewSettlement> settlements)
    {
        WorldSize = worldSize;
        Heights = heights;
        Biomes = biomes;
        BiomeColors = biomeColors;
        BiomeTypes = biomeTypes;
        WaterKinds = waterKinds;
        WaterSurface = waterSurface;
        WaterResolution = waterResolution;
        Roads = roads ?? new List<Road>();
        Buildings = buildings ?? new List<PoiPlacement>();
        Settlements = settlements ?? new List<WorldPreviewSettlement>();
    }

    public static WorldPreviewSource Capture(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water,
        IReadOnlyList<Road> roads, IReadOnlyList<PoiPlacement> buildings, IReadOnlyList<SettlementLayout> settlements = null,
        WorldGameplayPlan gameplay = null)
    {
        var colors = new Color32[biomes.Count];
        var types = new BiomeType[biomes.Count];

        for (int i = 0; i < biomes.Count; i++)
        {
            colors[i] = biomes.Get(i).MapColor;
            types[i] = biomes.Get(i).Type;
        }

        int waterResolution = Mathf.Min(WATER_RESOLUTION, heights.WorldSize);
        var kinds = new byte[waterResolution * waterResolution];
        var surface = new float[kinds.Length];

        if (water != null)
            SampleWater(heights, water, waterResolution, kinds, surface);

        return new WorldPreviewSource(heights.WorldSize, Downsample(heights), biomeMap, colors, types, kinds, surface, waterResolution,
            roads, buildings, Outlines(settlements)) { Gameplay = gameplay };
    }

    public byte WaterKindAt(float x, float z) => WaterKinds[WaterIndex(x, z)];

    public float WaterSurfaceAt(float x, float z) => WaterSurface[WaterIndex(x, z)];

    public int BiomeAt(float x, float z) => Biomes.SampleWorld(new Vector3(x, 0f, z));

    private int WaterIndex(float x, float z)
    {
        int column = Mathf.Clamp((int)(x / WaterCell), 0, WaterResolution - 1);
        int row = Mathf.Clamp((int)(z / WaterCell), 0, WaterResolution - 1);
        return row * WaterResolution + column;
    }

    private static void SampleWater(HeightMap heights, WaterMap water, int resolution, byte[] kinds, float[] surface)
    {
        float cell = heights.WorldSize / (float)resolution;

        Parallel.For(0, resolution, row =>
        {
            float z = (row + 0.5f) * cell;

            for (int column = 0; column < resolution; column++)
            {
                float x = (column + 0.5f) * cell;
                float ground = heights.SampleWorldSmooth(x, z);
                WaterSample sample = water.Sample(x, z);
                int index = row * resolution + column;

                if (!sample.IsWater || ground >= sample.Surface)
                    continue;

                kinds[index] = (byte)sample.Kind;
                surface[index] = sample.Surface;
            }
        });
    }

    private static HeightMap Downsample(HeightMap heights)
    {
        int span = heights.Resolution - 1;
        int step = 1;

        while (span % (step * 2) == 0 && span / (step * 2) >= HEIGHT_RESOLUTION)
            step *= 2;

        if (step == 1)
            return heights;

        int resolution = span / step + 1;
        var coarse = new HeightMap(resolution, heights.WorldSize, heights.MaxHeight);
        float[] source = heights.Heights;
        float[] target = coarse.Heights;

        Parallel.For(0, resolution, row =>
        {
            int from = row * step * heights.Resolution;

            for (int column = 0; column < resolution; column++)
                target[row * resolution + column] = source[from + column * step];
        });

        return coarse;
    }

    private static List<WorldPreviewSettlement> Outlines(IReadOnlyList<SettlementLayout> layouts)
    {
        var settlements = new List<WorldPreviewSettlement>();

        if (layouts == null)
            return settlements;

        foreach (SettlementLayout layout in layouts)
        {
            var tiles = new List<Vector2[]>(layout.Tiles.Count);

            foreach (SettlementTile tile in layout.Tiles)
                tiles.Add((Vector2[])tile.Corners.Clone());

            settlements.Add(new WorldPreviewSettlement(layout.Hub.Type, layout.Hub.Position, tiles));
        }

        return settlements;
    }
}

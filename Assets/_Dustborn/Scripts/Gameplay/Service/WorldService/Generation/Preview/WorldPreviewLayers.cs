using System;
using System.Threading.Tasks;
using UnityEngine;

[Flags]
public enum WorldPreviewLayer
{
    None = 0,
    Biomes = 1,
    Relief = 2,
    Water = 4,
    Roads = 8,
    Settlements = 16,
    Buildings = 32,
    All = Biomes | Relief | Water | Roads | Settlements | Buildings
}

public sealed class WorldPreviewLayers
{
    public const byte ROAD_HIGHWAY = 1;
    public const byte ROAD_STREET = 2;
    public const byte ROAD_DIRT = 3;
    public const float SHADE_SCALE = 160f;
    public const float DEPTH_SCALE = 8f;

    private const float SETTLEMENT_TINT = 0.4f;
    private const float WATER_DEPTH_DARKENING = 0.45f;
    private static readonly Color32 NEUTRAL = new(176, 168, 150, 255);
    private static readonly Color32 SEA = new(26, 62, 108, 255);
    private static readonly Color32 LAKE = new(38, 92, 146, 255);
    private static readonly Color32 RIVER = new(48, 112, 166, 255);
    private static readonly Color32 HIGHWAY = new(46, 46, 48, 255);
    private static readonly Color32 STREET = new(78, 78, 82, 255);
    private static readonly Color32 DIRT = new(122, 92, 58, 255);
    private static readonly Color32 BUILDING = new(150, 60, 44, 255);
    private static readonly Color32 START = new(255, 228, 110, 255);
    private static readonly Color32[] SETTLEMENT_COLORS =
    {
        new(214, 196, 120, 255),
        new(200, 176, 112, 255),
        new(186, 160, 108, 255),
        new(150, 140, 128, 255)
    };

    public int Resolution { get; }
    public byte[] Biome { get; }
    public byte[] Shade { get; }
    public byte[] Water { get; }
    public byte[] WaterDepth { get; }
    public byte[] Road { get; }
    public byte[] Settlement { get; }
    public byte[] Building { get; }
    public Color32[] BiomeColors { get; }
    public Vector2? Start { get; set; }
    public static Color32 StartColor => START;

    public WorldPreviewLayers(int resolution, Color32[] biomeColors)
    {
        int count = resolution * resolution;
        Resolution = resolution;
        BiomeColors = biomeColors;
        Biome = new byte[count];
        Shade = new byte[count];
        Water = new byte[count];
        WaterDepth = new byte[count];
        Road = new byte[count];
        Settlement = new byte[count];
        Building = new byte[count];
    }

    public static Color32 SettlementColor(SettlementType type) => SETTLEMENT_COLORS[Mathf.Clamp((int)type, 0, SETTLEMENT_COLORS.Length - 1)];

    public Color32[] Compose(WorldPreviewLayer layers)
    {
        var pixels = new Color32[Biome.Length];
        Compose(layers, pixels);
        return pixels;
    }

    public void Compose(WorldPreviewLayer layers, Color32[] pixels)
    {
        bool biomes = (layers & WorldPreviewLayer.Biomes) != 0;
        bool relief = (layers & WorldPreviewLayer.Relief) != 0;
        bool water = (layers & WorldPreviewLayer.Water) != 0;
        bool roads = (layers & WorldPreviewLayer.Roads) != 0;
        bool settlements = (layers & WorldPreviewLayer.Settlements) != 0;
        bool buildings = (layers & WorldPreviewLayer.Buildings) != 0;

        Parallel.For(0, Resolution, row =>
        {
            int end = (row + 1) * Resolution;

            for (int i = row * Resolution; i < end; i++)
            {
                Color32 colour = biomes ? BiomeColors[Mathf.Min(Biome[i], BiomeColors.Length - 1)] : NEUTRAL;

                if (relief)
                    colour = Scale(colour, Shade[i] / SHADE_SCALE);

                if (settlements && Settlement[i] != 0)
                    colour = Mix(colour, SETTLEMENT_COLORS[Settlement[i] - 1], SETTLEMENT_TINT);

                if (water && Water[i] != 0)
                    colour = WaterColour((WaterKind)Water[i], WaterDepth[i] / DEPTH_SCALE);

                if (roads && Road[i] != 0)
                    colour = RoadColour(Road[i]);

                if (buildings && Building[i] != 0)
                    colour = Mix(BUILDING, new Color32(74, 16, 38, 255), (Building[i] - 1) * 0.18f);

                pixels[i] = colour;
            }
        });

        if (buildings && Start.HasValue)
        {
            int x = Mathf.Clamp((int)(Start.Value.x * Resolution), 0, Resolution - 1);
            int y = Mathf.Clamp((int)(Start.Value.y * Resolution), 0, Resolution - 1);
            for (int offset = -4; offset <= 4; offset++)
            {
                pixels[y * Resolution + Mathf.Clamp(x + offset, 0, Resolution - 1)] = START;
                pixels[Mathf.Clamp(y + offset, 0, Resolution - 1) * Resolution + x] = START;
            }
        }
    }

    public static Color32 WaterColour(WaterKind kind, float depth)
    {
        Color32 colour = kind switch
        {
            WaterKind.Sea => SEA,
            WaterKind.River => RIVER,
            _ => LAKE
        };

        return Scale(colour, 1f - WATER_DEPTH_DARKENING * Mathf.Clamp01(depth / WorldPreviewPainter.DEEP_WATER));
    }

    public static Color32 RoadColour(byte road)
    {
        return road switch
        {
            ROAD_HIGHWAY => HIGHWAY,
            ROAD_DIRT => DIRT,
            _ => STREET
        };
    }

    public static Color32 Mix(Color32 from, Color32 to, float share)
    {
        return new Color32(
            (byte)Mathf.RoundToInt(from.r + (to.r - from.r) * share),
            (byte)Mathf.RoundToInt(from.g + (to.g - from.g) * share),
            (byte)Mathf.RoundToInt(from.b + (to.b - from.b) * share),
            255);
    }

    public static Color32 Scale(Color32 colour, float factor)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255),
            255);
    }
}

using System.Collections.Generic;
using UnityEngine;

public static class WorldPreviewPainter
{
    public const int RESOLUTION = 1024;

    private const float RELIEF_EXAGGERATION = 2.5f;
    private const float SHADE_BASE = 0.62f;
    private const float SHADE_RANGE = 0.55f;
    private const float SHADE_MIN = 0.35f;
    private const float SHADE_MAX = 1.2f;
    private const float ALTITUDE_BASE = 0.85f;
    private const float ALTITUDE_RANGE = 0.3f;
    private const float DEEP_WATER = 12f;
    private const float WATER_DEPTH_DARKENING = 0.45f;
    private const float HIGHWAY_MIN_PIXELS = 2f;
    private const float STREET_MIN_PIXELS = 1f;
    private const float LINE_STEP = 0.5f;
    private const float LIGHT_X = -1f;
    private const float LIGHT_Y = 1.4f;
    private const float LIGHT_Z = 1f;
    private static readonly Color32 SEA = new(26, 62, 108, 255);
    private static readonly Color32 LAKE = new(38, 92, 146, 255);
    private static readonly Color32 RIVER = new(48, 112, 166, 255);
    private static readonly Color32 HIGHWAY = new(46, 46, 48, 255);
    private static readonly Color32 STREET = new(78, 78, 82, 255);
    private static readonly Color32 DIRT = new(122, 92, 58, 255);
    private static readonly Color32 BUILDING = new(150, 60, 44, 255);

    public static Color32[] Paint(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water,
        IReadOnlyList<Road> roads, IReadOnlyList<PoiPlacement> buildings, int resolution = RESOLUTION)
    {
        var pixels = new Color32[resolution * resolution];
        float metres = heights.WorldSize / (float)resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
                pixels[y * resolution + x] = Ground(biomes, biomeMap, heights, water, (x + 0.5f) * metres, (y + 0.5f) * metres, metres);
        }

        if (roads != null)
        {
            foreach (Road road in roads)
                DrawRoad(pixels, resolution, metres, road);
        }

        if (buildings != null)
        {
            foreach (PoiPlacement building in buildings)
                DrawBuilding(pixels, resolution, metres, building);
        }

        return pixels;
    }

    private static Color32 Ground(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water, float x, float z, float step)
    {
        float ground = heights.SampleWorldSmooth(x, z);
        WaterKind kind = water == null ? WaterKind.None : water.KindAt(x, z, ground);

        if (kind != WaterKind.None)
            return Water(kind, water.SurfaceHeight(x, z) - ground);

        byte biome = biomeMap.SampleWorld(new Vector3(x, 0f, z));
        Color32 colour = biomes.Get(biome).MapColor;

        float dx = (heights.SampleWorldSmooth(x + step, z) - heights.SampleWorldSmooth(x - step, z)) / (2f * step);
        float dz = (heights.SampleWorldSmooth(x, z + step) - heights.SampleWorldSmooth(x, z - step)) / (2f * step);
        float shade = Mathf.Clamp(SHADE_BASE + SHADE_RANGE * Lighting(dx * RELIEF_EXAGGERATION, dz * RELIEF_EXAGGERATION), SHADE_MIN, SHADE_MAX);
        shade *= ALTITUDE_BASE + ALTITUDE_RANGE * ground / heights.MaxHeight;

        return Scale(colour, shade);
    }

    private static float Lighting(float slopeX, float slopeZ)
    {
        float normal = Mathf.Sqrt(slopeX * slopeX + 1f + slopeZ * slopeZ);
        float light = Mathf.Sqrt(LIGHT_X * LIGHT_X + LIGHT_Y * LIGHT_Y + LIGHT_Z * LIGHT_Z);
        return (-slopeX * LIGHT_X + LIGHT_Y - slopeZ * LIGHT_Z) / (normal * light);
    }

    private static Color32 Water(WaterKind kind, float depth)
    {
        Color32 colour = kind switch
        {
            WaterKind.Sea => SEA,
            WaterKind.River => RIVER,
            _ => LAKE
        };

        float darkening = 1f - WATER_DEPTH_DARKENING * Mathf.Clamp01(depth / DEEP_WATER);
        return Scale(colour, darkening);
    }

    private static void DrawRoad(Color32[] pixels, int resolution, float metres, Road road)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        Color32 colour = road.Kind switch
        {
            RoadKind.Highway => HIGHWAY,
            RoadKind.DirtAccess => DIRT,
            _ => STREET
        };

        float minimum = road.Kind == RoadKind.Highway ? HIGHWAY_MIN_PIXELS : STREET_MIN_PIXELS;
        float radius = Mathf.Max(minimum, road.Width / metres) * 0.5f;

        for (int i = 1; i < road.Points.Length; i++)
        {
            Vector2 from = road.Points[i - 1] / metres;
            Vector2 to = road.Points[i] / metres;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / LINE_STEP));

            for (int s = 0; s <= steps; s++)
                Disc(pixels, resolution, Vector2.Lerp(from, to, s / (float)steps), radius, colour);
        }
    }

    private static void DrawBuilding(Color32[] pixels, int resolution, float metres, PoiPlacement building)
    {
        Vector2 centre = building.Ground / metres;
        Vector2 half = building.Footprint * (0.5f / metres);
        float angle = -building.Rotation * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        float reach = Mathf.Max(0.5f, half.magnitude);

        int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - reach));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(centre.x + reach));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - reach));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(centre.y + reach));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f - centre.x;
                float py = y + 0.5f - centre.y;
                float u = px * cos - py * sin;
                float v = px * sin + py * cos;

                if (Mathf.Abs(u) <= Mathf.Max(0.5f, half.x) && Mathf.Abs(v) <= Mathf.Max(0.5f, half.y))
                    pixels[y * resolution + x] = BUILDING;
            }
        }
    }

    private static void Disc(Color32[] pixels, int resolution, Vector2 centre, float radius, Color32 colour)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
        int maxX = Mathf.Min(resolution - 1, Mathf.FloorToInt(centre.x + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
        int maxY = Mathf.Min(resolution - 1, Mathf.FloorToInt(centre.y + radius));
        float squared = Mathf.Max(0.25f, radius * radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - centre.x;
                float dy = y + 0.5f - centre.y;

                if (dx * dx + dy * dy <= squared)
                    pixels[y * resolution + x] = colour;
            }
        }
    }

    private static Color32 Scale(Color32 colour, float factor)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255),
            255);
    }
}

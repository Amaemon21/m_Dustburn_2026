using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class WorldPreviewPainter
{
    public const int RESOLUTION = 1024;
    public const float DEEP_WATER = 12f;

    private const float RELIEF_EXAGGERATION = 2.5f;
    private const float SHADE_BASE = 0.62f;
    private const float SHADE_RANGE = 0.55f;
    private const float SHADE_MIN = 0.35f;
    private const float SHADE_MAX = 1.2f;
    private const float ALTITUDE_BASE = 0.85f;
    private const float ALTITUDE_RANGE = 0.3f;
    private const float HIGHWAY_MIN_PIXELS = 2f;
    private const float STREET_MIN_PIXELS = 1f;
    private const float LINE_STEP = 0.5f;
    private const float LIGHT_X = -1f;
    private const float LIGHT_Y = 1.4f;
    private const float LIGHT_Z = 1f;

    public static Color32[] Paint(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water,
        IReadOnlyList<Road> roads, IReadOnlyList<PoiPlacement> buildings, int resolution = RESOLUTION)
    {
        WorldPreviewSource source = WorldPreviewSource.Capture(biomes, biomeMap, heights, water, roads, buildings);
        return Layers(source, resolution).Compose(WorldPreviewLayer.All);
    }

    public static WorldPreviewLayers Layers(WorldPreviewSource source, int resolution)
    {
        var layers = new WorldPreviewLayers(resolution, source.BiomeColors);
        float metres = source.WorldSize / (float)resolution;

        Parallel.For(0, resolution, y =>
        {
            for (int x = 0; x < resolution; x++)
                Ground(source, layers, y * resolution + x, (x + 0.5f) * metres, (y + 0.5f) * metres, metres);
        });

        foreach (WorldPreviewSettlement settlement in source.Settlements)
        {
            byte value = (byte)((int)settlement.Type + 1);

            foreach (Vector2[] tile in settlement.Tiles)
                FillPolygon(layers.Settlement, resolution, metres, tile, value);
        }

        foreach (Road road in source.Roads)
            DrawRoad(layers.Road, resolution, metres, road);

        foreach (PoiPlacement building in source.Buildings)
            DrawBuilding(layers.Building, resolution, metres, building);

        if (source.Gameplay?.HasSpawn == true)
            layers.Start = new Vector2(source.Gameplay.Spawn.x, source.Gameplay.Spawn.z) / source.WorldSize;

        return layers;
    }

    private static void Ground(WorldPreviewSource source, WorldPreviewLayers layers, int index, float x, float z, float step)
    {
        HeightMap heights = source.Heights;
        float ground = heights.SampleWorldSmooth(x, z);

        layers.Biome[index] = (byte)source.BiomeAt(x, z);

        float dx = (heights.SampleWorldSmooth(x + step, z) - heights.SampleWorldSmooth(x - step, z)) / (2f * step);
        float dz = (heights.SampleWorldSmooth(x, z + step) - heights.SampleWorldSmooth(x, z - step)) / (2f * step);
        float shade = Mathf.Clamp(SHADE_BASE + SHADE_RANGE * Lighting(dx * RELIEF_EXAGGERATION, dz * RELIEF_EXAGGERATION), SHADE_MIN, SHADE_MAX);
        shade *= ALTITUDE_BASE + ALTITUDE_RANGE * ground / heights.MaxHeight;
        layers.Shade[index] = (byte)Mathf.Clamp(Mathf.RoundToInt(shade * WorldPreviewLayers.SHADE_SCALE), 0, 255);

        byte kind = source.WaterKindAt(x, z);
        if (kind == 0)
            return;

        layers.Water[index] = kind;
        float depth = source.WaterSurfaceAt(x, z) - ground;
        layers.WaterDepth[index] = (byte)Mathf.Clamp(Mathf.RoundToInt(depth * WorldPreviewLayers.DEPTH_SCALE), 0, 255);
    }

    private static float Lighting(float slopeX, float slopeZ)
    {
        float normal = Mathf.Sqrt(slopeX * slopeX + 1f + slopeZ * slopeZ);
        float light = Mathf.Sqrt(LIGHT_X * LIGHT_X + LIGHT_Y * LIGHT_Y + LIGHT_Z * LIGHT_Z);
        return (-slopeX * LIGHT_X + LIGHT_Y - slopeZ * LIGHT_Z) / (normal * light);
    }

    private static void DrawRoad(byte[] layer, int resolution, float metres, Road road)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        byte value = road.Kind switch
        {
            RoadKind.Highway => WorldPreviewLayers.ROAD_HIGHWAY,
            RoadKind.DirtAccess => WorldPreviewLayers.ROAD_DIRT,
            _ => WorldPreviewLayers.ROAD_STREET
        };

        float minimum = road.Kind == RoadKind.Highway ? HIGHWAY_MIN_PIXELS : STREET_MIN_PIXELS;
        float radius = Mathf.Max(minimum, road.Width / metres) * 0.5f;

        for (int i = 1; i < road.Points.Length; i++)
        {
            Vector2 from = road.Points[i - 1] / metres;
            Vector2 to = road.Points[i] / metres;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / LINE_STEP));

            for (int s = 0; s <= steps; s++)
                Disc(layer, resolution, Vector2.Lerp(from, to, s / (float)steps), radius, value);
        }
    }

    private static void DrawBuilding(byte[] layer, int resolution, float metres, PoiPlacement building)
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
                    layer[y * resolution + x] = (byte)Mathf.Clamp(building.Gameplay.Tier, 1, 5);
            }
        }
    }

    private static void FillPolygon(byte[] layer, int resolution, float metres, Vector2[] corners, byte value)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (Vector2 corner in corners)
        {
            min = Vector2.Min(min, corner / metres);
            max = Vector2.Max(max, corner / metres);
        }

        int minX = Mathf.Max(0, Mathf.FloorToInt(min.x));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(max.x));
        int minY = Mathf.Max(0, Mathf.FloorToInt(min.y));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(max.y));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (Inside(corners, new Vector2(x + 0.5f, y + 0.5f) * metres))
                    layer[y * resolution + x] = value;
            }
        }
    }

    private static bool Inside(Vector2[] corners, Vector2 point)
    {
        bool positive = false;
        bool negative = false;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 a = corners[i];
            Vector2 b = corners[(i + 1) % corners.Length];
            float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

            positive |= cross > 0f;
            negative |= cross < 0f;
        }

        return !(positive && negative);
    }

    private static void Disc(byte[] layer, int resolution, Vector2 centre, float radius, byte value)
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
                    layer[y * resolution + x] = value;
            }
        }
    }
}

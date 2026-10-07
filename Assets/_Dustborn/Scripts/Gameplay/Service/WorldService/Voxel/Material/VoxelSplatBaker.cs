using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

public static class VoxelSplatBaker
{
    public const int CHANNELS = 4;

    private const long TILE_WEIGHT_BUDGET = 32L << 20;
    private const int MIN_TILE = 256;
    private const float DIAGONAL = 0.70710678f;

    public static Texture2D[] Bake(WorldGenerationConfig config, GroundSplatPainter painter, HeightMap map, int resolution)
    {
        int layers = painter.Layers.Length;
        int tile = TileFor(resolution, layers);

        var pixels = new Color32[ControlCount(layers)][];

        for (int control = 0; control < pixels.Length; control++)
            pixels[control] = new Color32[resolution * resolution];

        for (int originY = 0; originY < resolution; originY += tile)
        {
            int tileY = Mathf.Min(originY, resolution - tile);

            for (int originX = 0; originX < resolution; originX += tile)
            {
                int tileX = Mathf.Min(originX, resolution - tile);

                Surface(config, map, resolution, tileX, tileY, tile, out float[] steepness, out float[] height, out float[] relief);

                NativeArray<float> weights = painter.BakeTile(resolution, tile, tileX, tileY, steepness, height, relief);

                try
                {
                    Pack(weights, pixels, resolution, tileX, tileY, tile, layers);
                }
                finally
                {
                    weights.Dispose();
                }
            }
        }

        return Upload(pixels, resolution);
    }

    public static void Surface(WorldGenerationConfig config, HeightMap map, int resolution, out float[] steepness, out float[] height)
    {
        Surface(config, map, resolution, 0, 0, resolution, out steepness, out height);
    }

    public static void Surface(WorldGenerationConfig config, HeightMap map, int resolution, int originX, int originY, int tile,
        out float[] steepness, out float[] height)
    {
        Measure(config, map, resolution, originX, originY, tile, out steepness, out height, null);
    }

    public static void Surface(WorldGenerationConfig config, HeightMap map, int resolution, int originX, int originY, int tile,
        out float[] steepness, out float[] height, out float[] relief)
    {
        relief = new float[tile * tile];
        Measure(config, map, resolution, originX, originY, tile, out steepness, out height, relief);
    }

    private static void Measure(WorldGenerationConfig config, HeightMap map, int resolution, int originX, int originY, int tile,
        out float[] steepness, out float[] height, float[] relief)
    {
        var slope = new float[tile * tile];
        var elevation = new float[tile * tile];

        float step = (float)config.WorldSize / resolution;
        float near = Mathf.Max(step, config.ReliefRadius);
        float diagonal = near * DIAGONAL;
        float far = near * GroundNoise.LARGE_RELIEF_RADIUS;
        float range = Mathf.Max(0.01f, config.ReliefRange);

        Parallel.For(0, tile, y =>
        {
            float worldZ = (originY + y + 0.5f) * step;

            for (int x = 0; x < tile; x++)
            {
                float worldX = (originX + x + 0.5f) * step;

                float here = map.SampleWorldSmooth(worldX, worldZ);

                float dx = map.SampleWorldSmooth(worldX + step, worldZ) - map.SampleWorldSmooth(worldX - step, worldZ);
                float dz = map.SampleWorldSmooth(worldX, worldZ + step) - map.SampleWorldSmooth(worldX, worldZ - step);

                float gradient = Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);

                slope[y * tile + x] = Mathf.Atan(gradient) * Mathf.Rad2Deg;
                elevation[y * tile + x] = here / config.MaxHeight;

                if (relief == null)
                    continue;

                float nearRing = (map.SampleWorldSmooth(worldX + near, worldZ) + map.SampleWorldSmooth(worldX - near, worldZ)
                                  + map.SampleWorldSmooth(worldX, worldZ + near) + map.SampleWorldSmooth(worldX, worldZ - near)
                                  + map.SampleWorldSmooth(worldX + diagonal, worldZ + diagonal) + map.SampleWorldSmooth(worldX - diagonal, worldZ - diagonal)
                                  + map.SampleWorldSmooth(worldX + diagonal, worldZ - diagonal) + map.SampleWorldSmooth(worldX - diagonal, worldZ + diagonal)) * 0.125f;

                float farRing = (map.SampleWorldSmooth(worldX + far, worldZ) + map.SampleWorldSmooth(worldX - far, worldZ)
                                 + map.SampleWorldSmooth(worldX, worldZ + far) + map.SampleWorldSmooth(worldX, worldZ - far)) * 0.25f;

                relief[y * tile + x] = GroundNoise.ReliefSignal(here, nearRing, farRing, range);
            }
        });

        steepness = slope;
        height = elevation;
    }

    public static int ControlCount(int layers)
    {
        return Mathf.CeilToInt(layers / (float)CHANNELS);
    }

    private static int TileFor(int resolution, int layers)
    {
        int tile = resolution;

        while (tile > MIN_TILE && (long)tile * tile * layers > TILE_WEIGHT_BUDGET)
            tile >>= 1;

        return tile;
    }

    private static void Pack(NativeArray<float> weights, Color32[][] pixels, int resolution, int originX, int originY, int tile, int layers)
    {
        for (int control = 0; control < pixels.Length; control++)
        {
            int first = control * CHANNELS;
            Color32[] target = pixels[control];

            Parallel.For(0, tile, row =>
            {
                int line = (originY + row) * resolution + originX;

                for (int column = 0; column < tile; column++)
                {
                    int origin = (row * tile + column) * layers + first;

                    target[line + column] = new Color32(
                        Channel(weights, origin, 0, layers, first),
                        Channel(weights, origin, 1, layers, first),
                        Channel(weights, origin, 2, layers, first),
                        Channel(weights, origin, 3, layers, first));
                }
            });
        }
    }

    private static Texture2D[] Upload(Color32[][] pixels, int resolution)
    {
        var textures = new Texture2D[pixels.Length];

        for (int control = 0; control < textures.Length; control++)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = $"VoxelControl{control}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture.SetPixels32(pixels[control]);
            texture.Apply(false, false);

            pixels[control] = null;

            textures[control] = texture;
        }

        return textures;
    }

    private static byte Channel(NativeArray<float> weights, int origin, int channel, int layers, int first)
    {
        if (first + channel >= layers)
            return 0;

        return (byte)Mathf.Clamp(Mathf.RoundToInt(weights[origin + channel] * 255f), 0, 255);
    }
}

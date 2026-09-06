using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

public static class VoxelSplatBaker
{
    public const int CHANNELS = 4;

    public static Texture2D[] Bake(WorldGenerationConfig config, GroundSplatPainter painter, HeightMap map, int resolution)
    {
        Surface(config, map, resolution, out float[] steepness, out float[] height);

        NativeArray<float> weights = painter.BakeWorld(resolution, steepness, height);

        try
        {
            return Pack(weights, resolution, painter.Layers.Length);
        }
        finally
        {
            weights.Dispose();
        }
    }

    public static void Surface(WorldGenerationConfig config, HeightMap map, int resolution, out float[] steepness, out float[] height)
    {
        int cells = resolution * resolution;

        var slope = new float[cells];
        var elevation = new float[cells];

        float step = (float)config.WorldSize / resolution;

        Parallel.For(0, resolution, y =>
        {
            float worldZ = (y + 0.5f) * step;

            for (int x = 0; x < resolution; x++)
            {
                float worldX = (x + 0.5f) * step;

                float here = map.SampleWorldSmooth(worldX, worldZ);

                float dx = map.SampleWorldSmooth(worldX + step, worldZ) - map.SampleWorldSmooth(worldX - step, worldZ);
                float dz = map.SampleWorldSmooth(worldX, worldZ + step) - map.SampleWorldSmooth(worldX, worldZ - step);

                float gradient = Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);

                slope[y * resolution + x] = Mathf.Atan(gradient) * Mathf.Rad2Deg;
                elevation[y * resolution + x] = here / config.MaxHeight;
            }
        });

        steepness = slope;
        height = elevation;
    }

    public static int ControlCount(int layers)
    {
        return Mathf.CeilToInt(layers / (float)CHANNELS);
    }

    private static Texture2D[] Pack(NativeArray<float> weights, int resolution, int layers)
    {
        var textures = new Texture2D[ControlCount(layers)];
        var pixels = new Color32[resolution * resolution];

        for (int control = 0; control < textures.Length; control++)
        {
            int first = control * CHANNELS;

            Parallel.For(0, resolution, row =>
            {
                for (int column = 0; column < resolution; column++)
                {
                    int cell = row * resolution + column;
                    int origin = cell * layers + first;

                    pixels[cell] = new Color32(
                        Channel(weights, origin, 0, layers, first),
                        Channel(weights, origin, 1, layers, first),
                        Channel(weights, origin, 2, layers, first),
                        Channel(weights, origin, 3, layers, first));
                }
            });

            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = $"VoxelControl{control}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

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

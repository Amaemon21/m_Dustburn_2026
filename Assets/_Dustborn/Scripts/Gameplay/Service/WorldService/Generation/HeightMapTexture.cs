using System.Threading.Tasks;
using UnityEngine;

public static class HeightMapTexture
{
    private const float SLOPE_SCALE = 60f;

    public static byte[] Hillshade(HeightMap map)
    {
        int resolution = map.Resolution;
        var values = new byte[resolution * resolution];

        Parallel.For(0, resolution, y =>
        {
            for (int x = 0; x < resolution; x++)
                values[y * resolution + x] = Shade(map, x, y, resolution);
        });

        return values;
    }

    public static Texture2D CreateHillshade(HeightMap map)
    {
        int resolution = map.Resolution;

        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "HeightMapPreview",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        byte[] values = Hillshade(map);
        var pixels = new Color32[values.Length];

        for (int i = 0; i < values.Length; i++)
            pixels[i] = new Color32(values[i], values[i], values[i], 255);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        return texture;
    }

    private static byte Shade(HeightMap map, int x, int y, int resolution)
    {
        int left = Mathf.Max(x - 1, 0);
        int right = Mathf.Min(x + 1, resolution - 1);
        int down = Mathf.Max(y - 1, 0);
        int up = Mathf.Min(y + 1, resolution - 1);

        float dx = (map.Get(right, y) - map.Get(left, y)) * SLOPE_SCALE;
        float dy = (map.Get(x, up) - map.Get(x, down)) * SLOPE_SCALE;

        float length = Mathf.Sqrt(dx * dx + dy * dy + 1f);
        float lambert = (-dx * 0.5f - dy * 0.5f + 1f) / length;
        float light = Mathf.Clamp(lambert * 0.8f + 0.25f, 0.15f, 1f);

        float elevation = map.Get(x, y);

        return (byte)Mathf.Clamp(Mathf.RoundToInt(255f * light * (0.35f + 0.65f * elevation)), 0, 255);
    }
}

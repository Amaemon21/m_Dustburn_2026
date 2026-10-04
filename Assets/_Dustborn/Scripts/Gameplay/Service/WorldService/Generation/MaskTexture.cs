using System.Threading.Tasks;
using UnityEngine;

public static class MaskTexture
{
    private const int BLOCK = 65536;

    public static byte[] ToGray(float[] mask)
    {
        var values = new byte[mask.Length];

        Parallel.For(0, Blocks(mask.Length), block =>
        {
            int end = Mathf.Min(mask.Length, (block + 1) * BLOCK);

            for (int i = block * BLOCK; i < end; i++)
                values[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(mask[i] * 255f), 0, 255);
        });

        return values;
    }

    public static Texture2D Create(float[] mask, int resolution, string name)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        byte[] values = ToGray(mask);
        var pixels = new Color32[values.Length];

        for (int i = 0; i < values.Length; i++)
            pixels[i] = new Color32(values[i], values[i], values[i], 255);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        return texture;
    }

    public static float[] Read(Texture2D texture)
    {
        if (texture.width != texture.height)
        {
            Debug.LogError($"Mask texture must be square, got {texture.width}x{texture.height}", texture);
            return null;
        }

        Color32[] pixels;

        try
        {
            pixels = texture.GetPixels32();
        }
        catch (UnityException)
        {
            Debug.LogError("Mask texture is not readable, enable Read/Write in its import settings", texture);
            return null;
        }

        var mask = new float[pixels.Length];

        Parallel.For(0, Blocks(pixels.Length), block =>
        {
            int end = Mathf.Min(pixels.Length, (block + 1) * BLOCK);

            for (int i = block * BLOCK; i < end; i++)
            {
                byte red = pixels[i].r;
                byte alpha = pixels[i].a;

                mask[i] = (red < alpha ? red : alpha) / 255f;
            }
        });

        return mask;
    }

    private static int Blocks(int count)
    {
        return (count + BLOCK - 1) / BLOCK;
    }
}

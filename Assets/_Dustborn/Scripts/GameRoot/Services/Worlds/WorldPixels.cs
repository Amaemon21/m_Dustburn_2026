using UnityEngine;

public static class WorldPixels
{
    private const int RGBA = 4;

    public static byte[] Rgba(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length * RGBA];

        for (int i = 0; i < pixels.Length; i++)
        {
            int offset = i * RGBA;
            bytes[offset] = pixels[i].r;
            bytes[offset + 1] = pixels[i].g;
            bytes[offset + 2] = pixels[i].b;
            bytes[offset + 3] = pixels[i].a;
        }

        return bytes;
    }

    public static byte[] Gray(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
            bytes[i] = pixels[i].r < pixels[i].a ? pixels[i].r : pixels[i].a;

        return bytes;
    }
}

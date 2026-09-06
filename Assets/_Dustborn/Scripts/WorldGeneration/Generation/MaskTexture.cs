using UnityEngine;

public static class MaskTexture
{
    public static Texture2D Create(float[] mask, int resolution, string name)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[mask.Length];

        for (int i = 0; i < mask.Length; i++)
        {
            byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(mask[i] * 255f), 0, 255);

            pixels[i] = new Color32(value, value, value, 255);
        }

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

        for (int i = 0; i < pixels.Length; i++)
            mask[i] = pixels[i].r / 255f;

        return mask;
    }
}

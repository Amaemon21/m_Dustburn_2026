using System.Collections.Generic;
using UnityEngine;

public static class BiomeMapTexture
{
    private static readonly Color32 UnknownColor = new(255, 0, 255, 255);

    public static BiomeMap Read(Texture2D texture, BiomeDatabase biomes, int worldSize)
    {
        if (texture.width != texture.height)
        {
            Debug.LogError($"Biome map texture must be square, got {texture.width}x{texture.height}");
            return null;
        }

        Color32[] pixels;

        try
        {
            pixels = texture.GetPixels32();
        }
        catch (UnityException)
        {
            Debug.LogError($"Biome map {texture} is not readable, enable Read/Write in its import settings");
            return null;
        }

        var palette = new Dictionary<int, byte>();

        for (int i = 0; i < biomes.Count; i++)
            palette[Pack(biomes.Get(i).MapColor)] = (byte)i;

        var map = new BiomeMap(texture.width, worldSize);
        byte[] cells = map.Cells;
        int unmatched = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            if (palette.TryGetValue(Pack(pixels[i]), out byte biome))
            {
                cells[i] = biome;
                continue;
            }

            cells[i] = FindNearest(pixels[i], biomes);
            unmatched++;
        }

        if (unmatched > 0)
            Debug.LogWarning($"Biome map has {texture} pixels with colors outside the palette, matched them to the nearest biome");

        return map;
    }

    private static int Pack(Color32 color)
    {
        return (color.r << 16) | (color.g << 8) | color.b;
    }

    private static byte FindNearest(Color32 color, BiomeDatabase biomes)
    {
        byte best = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < biomes.Count; i++)
        {
            Color32 candidate = biomes.Get(i).MapColor;

            int dr = color.r - candidate.r;
            int dg = color.g - candidate.g;
            int db = color.b - candidate.b;
            int distance = dr * dr + dg * dg + db * db;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = (byte)i;
        }

        return best;
    }

    public static Texture2D Create(BiomeMap map, BiomeDatabase biomes)
    {
        var texture = new Texture2D(map.Resolution, map.Resolution, TextureFormat.RGBA32, false)
        {
            name = "BiomeMap",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] palette = new Color32[biomes.Count];

        for (int i = 0; i < biomes.Count; i++)
            palette[i] = biomes.Get(i).MapColor;

        byte[] cells = map.Cells;
        Color32[] pixels = new Color32[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            byte index = cells[i];

            pixels[i] = index < palette.Length ? palette[index] : UnknownColor;
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        return texture;
    }
}

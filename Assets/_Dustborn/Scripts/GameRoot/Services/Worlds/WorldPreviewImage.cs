using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class WorldPreviewImage
{
    public static byte[] Paint(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water, IReadOnlyList<Road> roads,
        IReadOnlyList<PoiPlacement> buildings)
    {
        Color32[] pixels = WorldPreviewPainter.Paint(biomes, biomeMap, heights, water, roads, buildings);
        uint size = WorldPreviewPainter.RESOLUTION;
        return ImageConversion.EncodeArrayToPNG(WorldPixels.Rgba(pixels), GraphicsFormat.R8G8B8A8_SRGB, size, size);
    }

    public static Texture2D Load(byte[] png)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        if (texture.LoadImage(png, true))
            return texture;

        WorldData.Release(texture);
        return null;
    }
}

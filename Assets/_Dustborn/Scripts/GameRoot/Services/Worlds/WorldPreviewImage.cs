using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class WorldPreviewImage
{
    public static byte[] Paint(BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water, IReadOnlyList<Road> roads,
        IReadOnlyList<PoiPlacement> buildings)
    {
        Color32[] pixels = WorldPreviewPainter.Paint(biomes, biomeMap, heights, water, roads, buildings);
        return Encode(pixels, WorldPreviewPainter.RESOLUTION);
    }

    public static byte[] Encode(WorldPreviewLayers layers) => Encode(layers.Compose(WorldPreviewLayer.All), layers.Resolution);

    public static byte[] Encode(Color32[] pixels, int resolution)
    {
        uint size = (uint)resolution;
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

    public static Texture2D Create(int resolution)
    {
        return new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "WorldPreview",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }
}

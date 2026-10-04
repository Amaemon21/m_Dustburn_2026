using UnityEngine;

public static class BiomeMapTexture
{
    public static Texture2D Create(BiomeMap map, BiomeDatabase biomes) => null;
    public static BiomeMap Read(Texture2D texture, BiomeDatabase biomes, int worldSize) => null;
}

public static class HeightMapTexture
{
    public static Texture2D CreateHillshade(HeightMap map) => null;
}

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class StampBytes
{
    private sealed class ByReference : IEqualityComparer<TextAsset>
    {
        public bool Equals(TextAsset left, TextAsset right) => ReferenceEquals(left, right);
        public int GetHashCode(TextAsset asset) => RuntimeHelpers.GetHashCode(asset);
    }

    private static readonly ConcurrentDictionary<TextAsset, byte[]> CACHE = new(new ByReference());

    public static byte[] Of(TextAsset asset)
    {
        return CACHE.TryGetValue(asset, out byte[] bytes) ? bytes : asset.bytes;
    }

    public static long SizeOf(TextAsset asset)
    {
        if (ReferenceEquals(asset, null))
            return -1;

        if (CACHE.TryGetValue(asset, out byte[] bytes))
            return bytes.Length;

        return asset != null ? asset.dataSize : -1;
    }

    public static void Preload(WorldGenerationConfig config)
    {
        TerrainStampDatabase terrain = config.Stamps?.Database;
        if (terrain != null)
        {
            for (int i = 0; i < terrain.Count; i++)
                Preload(terrain.Get(i).HeightData);
        }

        WaterStampDatabase water = config.Water?.Stamps?.Database;
        if (water != null)
        {
            for (int i = 0; i < water.Count; i++)
                Preload(water.Get(i).MaskData);
        }
    }

    public static void Clear() => CACHE.Clear();

    private static void Preload(TextAsset asset)
    {
        if (ReferenceEquals(asset, null))
            return;

        CACHE[asset] = asset != null ? asset.bytes : System.Array.Empty<byte>();
    }
}

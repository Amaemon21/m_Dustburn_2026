using System.Threading.Tasks;
using UnityEngine;

public class HeightMap
{
    private const int RAW_BLOCK = 65536;

    private readonly float[] _heights;

    public int Resolution { get; }
    public int WorldSize { get; }
    public float MaxHeight { get; }

    public float[] Heights => _heights;

    public HeightMap(int resolution, int worldSize, float maxHeight)
    {
        Resolution = resolution;
        WorldSize = worldSize;
        MaxHeight = maxHeight;

        _heights = new float[resolution * resolution];
    }

    public float Get(int x, int y)
    {
        return _heights[y * Resolution + x];
    }

    public void Set(int x, int y, float height)
    {
        _heights[y * Resolution + x] = height;
    }

    public float SampleWorld(Vector3 worldPosition)
    {
        float cellSize = (float)WorldSize / (Resolution - 1);

        int x = Mathf.Clamp(Mathf.RoundToInt(worldPosition.x / cellSize), 0, Resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(worldPosition.z / cellSize), 0, Resolution - 1);

        return Get(x, y) * MaxHeight;
    }

    public float SampleWorldSmooth(float worldX, float worldZ)
    {
        float cellSize = (float)WorldSize / (Resolution - 1);

        float u = Mathf.Clamp(worldX / cellSize, 0f, Resolution - 1.001f);
        float v = Mathf.Clamp(worldZ / cellSize, 0f, Resolution - 1.001f);

        int x = (int)u;
        int y = (int)v;

        float fx = u - x;
        float fy = v - y;

        float bottom = Mathf.Lerp(Get(x, y), Get(x + 1, y), fx);
        float top = Mathf.Lerp(Get(x, y + 1), Get(x + 1, y + 1), fx);

        return Mathf.Lerp(bottom, top, fy) * MaxHeight;
    }

    public static HeightMap FromRaw16(byte[] bytes, int resolution, int worldSize, float maxHeight)
    {
        var map = new HeightMap(resolution, worldSize, maxHeight);

        if (bytes.Length != map._heights.Length * 2)
        {
            int actual = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2f));

            Debug.LogError($"HeightMap.bytes is {bytes.Length} bytes, that is {actual}x{actual}, but the config expects {resolution}x{resolution}. The missing samples stay zero: regenerate the map or restore the HeightCellSize it was built with");
        }

        int count = Mathf.Min(map._heights.Length, bytes.Length / 2);
        float[] heights = map._heights;

        Parallel.For(0, Blocks(count), block =>
        {
            int end = Mathf.Min(count, (block + 1) * RAW_BLOCK);

            for (int i = block * RAW_BLOCK; i < end; i++)
            {
                int value = bytes[i * 2] | (bytes[i * 2 + 1] << 8);

                heights[i] = value / (float)ushort.MaxValue;
            }
        });

        return map;
    }

    public byte[] ToRaw16()
    {
        float[] heights = _heights;
        byte[] bytes = new byte[heights.Length * 2];

        Parallel.For(0, Blocks(heights.Length), block =>
        {
            int end = Mathf.Min(heights.Length, (block + 1) * RAW_BLOCK);

            for (int i = block * RAW_BLOCK; i < end; i++)
            {
                ushort value = (ushort)Mathf.Clamp(Mathf.RoundToInt(heights[i] * ushort.MaxValue), 0, ushort.MaxValue);

                bytes[i * 2] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)(value >> 8);
            }
        });

        return bytes;
    }

    private static int Blocks(int count)
    {
        return (count + RAW_BLOCK - 1) / RAW_BLOCK;
    }
}

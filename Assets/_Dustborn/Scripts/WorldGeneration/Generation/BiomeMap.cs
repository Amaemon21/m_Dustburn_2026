using UnityEngine;

public class BiomeMap
{
    private readonly byte[] _cells;

    public int Resolution { get; }
    public int WorldSize { get; }

    public float CellSize => (float)WorldSize / Resolution;

    public byte[] Cells => _cells;

    public BiomeMap(int resolution, int worldSize)
    {
        Resolution = resolution;
        WorldSize = worldSize;

        _cells = new byte[resolution * resolution];
    }

    public byte Get(int x, int y)
    {
        return _cells[y * Resolution + x];
    }

    public void Set(int x, int y, byte biomeIndex)
    {
        _cells[y * Resolution + x] = biomeIndex;
    }

    public byte SampleWorld(Vector3 worldPosition)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(worldPosition.x / CellSize), 0, Resolution - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(worldPosition.z / CellSize), 0, Resolution - 1);

        return Get(x, y);
    }
}

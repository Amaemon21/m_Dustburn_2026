using System.Collections.Generic;
using UnityEngine;

public sealed class RiverSegmentIndex
{
    public const float CELL = 32f;

    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<(int River, int Point)> _segments = new();

    public RiverSegmentIndex(IReadOnlyList<RiverPath> rivers)
    {
        for (int river = 0; river < rivers.Count; river++)
        {
            List<RiverPoint> points = rivers[river].Points;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i];
                RiverPoint b = points[i + 1];
                float reach = 0.5f * Mathf.Max(a.Width, b.Width);

                int id = _segments.Count;
                _segments.Add((river, i));

                int minX = Cell(Mathf.Min(a.Position.x, b.Position.x) - reach);
                int maxX = Cell(Mathf.Max(a.Position.x, b.Position.x) + reach);
                int minZ = Cell(Mathf.Min(a.Position.y, b.Position.y) - reach);
                int maxZ = Cell(Mathf.Max(a.Position.y, b.Position.y) + reach);

                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        long key = Key(cx, cz);

                        if (!_cells.TryGetValue(key, out List<int> list))
                            _cells[key] = list = new List<int>();

                        list.Add(id);
                    }
                }
            }
        }
    }

    public static int Cell(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / CELL);
    }

    public bool TryGet(float x, float z, out List<int> segments)
    {
        return TryGetCell(Cell(x), Cell(z), out segments);
    }

    public bool TryGetCell(int cx, int cz, out List<int> segments)
    {
        return _cells.TryGetValue(Key(cx, cz), out segments);
    }

    public (int River, int Point) Segment(int id)
    {
        return _segments[id];
    }

    private static long Key(int x, int z)
    {
        return ((long)x << 32) ^ (uint)z;
    }
}

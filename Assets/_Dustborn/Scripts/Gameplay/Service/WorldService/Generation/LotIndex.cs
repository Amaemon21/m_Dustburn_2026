using System.Collections.Generic;
using UnityEngine;

public class LotIndex
{
    private readonly List<Lot>[] _cells;
    private readonly float _cellSize;
    private readonly int _resolution;
    private readonly Dictionary<Lot, int> _seen = new();

    private int _query;

    public LotIndex(int worldSize, float cellSize)
    {
        _cellSize = cellSize;
        _resolution = Mathf.Max(1, Mathf.CeilToInt(worldSize / cellSize));
        _cells = new List<Lot>[_resolution * _resolution];
    }

    public void Add(Lot lot)
    {
        Bounds(lot, out int minX, out int maxX, out int minY, out int maxY);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cell = y * _resolution + x;

                _cells[cell] ??= new List<Lot>();
                _cells[cell].Add(lot);
            }
        }
    }

    public void Remove(Lot lot)
    {
        Bounds(lot, out int minX, out int maxX, out int minY, out int maxY);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
                _cells[y * _resolution + x]?.Remove(lot);
        }

        _seen.Remove(lot);
    }

    public bool Overlaps(Lot candidate, float shrink)
    {
        Bounds(candidate, out int minX, out int maxX, out int minY, out int maxY);

        _query++;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<Lot> bucket = _cells[y * _resolution + x];

                if (bucket == null)
                    continue;

                foreach (Lot lot in bucket)
                {
                    if (_seen.TryGetValue(lot, out int visited) && visited == _query)
                        continue;

                    _seen[lot] = _query;

                    if (candidate.Overlaps(lot, shrink))
                        return true;
                }
            }
        }

        return false;
    }

    private void Bounds(Lot lot, out int minX, out int maxX, out int minY, out int maxY)
    {
        float reach = Mathf.Sqrt(lot.Width * lot.Width + lot.Depth * lot.Depth) * 0.5f;

        minX = Cell(lot.Center.x - reach);
        maxX = Cell(lot.Center.x + reach);
        minY = Cell(lot.Center.y - reach);
        maxY = Cell(lot.Center.y + reach);
    }

    private int Cell(float coordinate)
    {
        return Mathf.Clamp(Mathf.FloorToInt(coordinate / _cellSize), 0, _resolution - 1);
    }
}

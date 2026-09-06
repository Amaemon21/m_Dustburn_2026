using System.Collections.Generic;
using UnityEngine;

public class PoiPadIndex
{
    private readonly float _cellSize;
    private readonly int _resolution;
    private readonly List<int>[] _cells;
    private readonly IReadOnlyList<PoiPlacement> _placements;
    private readonly float _margin;
    private readonly float _reserve;

    public PoiPadIndex(IReadOnlyList<PoiPlacement> placements, float worldSize, float cellSize, float margin, float reserve)
    {
        _placements = placements;
        _margin = margin;
        _reserve = Mathf.Max(0f, reserve);
        _cellSize = cellSize;
        _resolution = Mathf.Max(1, Mathf.CeilToInt(worldSize / cellSize));
        _cells = new List<int>[_resolution * _resolution];

        for (int i = 0; i < placements.Count; i++)
            Add(i);
    }

    public bool Covers(Vector2 point, float radius)
    {
        List<int> bucket = _cells[IndexOf(point)];

        if (bucket == null)
            return false;

        float extra = _margin + Mathf.Min(radius, _reserve);

        foreach (int index in bucket)
        {
            PoiPlacement placement = _placements[index];

            Vector2 forward = placement.Forward;
            Vector2 right = new(forward.y, -forward.x);
            Vector2 delta = point - placement.Ground;

            if (Mathf.Abs(delta.x * right.x + delta.y * right.y) > placement.Footprint.x * 0.5f + extra)
                continue;

            if (Mathf.Abs(delta.x * forward.x + delta.y * forward.y) > placement.Footprint.y * 0.5f + extra)
                continue;

            return true;
        }

        return false;
    }

    private void Add(int index)
    {
        PoiPlacement placement = _placements[index];

        Vector2 center = placement.Ground;
        float extra = _margin + _reserve;
        float reach = new Vector2(placement.Footprint.x * 0.5f + extra, placement.Footprint.y * 0.5f + extra).magnitude;

        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / _cellSize));
        int maxX = Mathf.Min(_resolution - 1, Mathf.FloorToInt((center.x + reach) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - reach) / _cellSize));
        int maxY = Mathf.Min(_resolution - 1, Mathf.FloorToInt((center.y + reach) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cell = y * _resolution + x;

                _cells[cell] ??= new List<int>();
                _cells[cell].Add(index);
            }
        }
    }

    private int IndexOf(Vector2 point)
    {
        int x = Mathf.Clamp((int)(point.x / _cellSize), 0, _resolution - 1);
        int y = Mathf.Clamp((int)(point.y / _cellSize), 0, _resolution - 1);

        return y * _resolution + x;
    }
}

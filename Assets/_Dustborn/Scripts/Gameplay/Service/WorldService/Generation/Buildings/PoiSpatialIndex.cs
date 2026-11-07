using System.Collections.Generic;
using UnityEngine;

public sealed class PoiSpatialIndex
{
    private const float CELL_SIZE = 128f;
    private readonly IReadOnlyList<PoiPlacement> _placements;
    private readonly List<int>[] _cells;
    private readonly int _resolution;
    private readonly float _reach;
    private readonly float[] _radii;
    private readonly Vector2[] _forwards;

    public PoiSpatialIndex(int worldSize, IReadOnlyList<PoiPlacement> placements)
    {
        _placements = placements;
        _resolution = Mathf.Max(1, Mathf.CeilToInt(worldSize / CELL_SIZE));
        _cells = new List<int>[_resolution * _resolution];
        _radii = new float[placements.Count];
        _forwards = new Vector2[placements.Count];

        for (int i = 0; i < placements.Count; i++)
        {
            PoiPlacement poi = placements[i];
            int cell = Cell(poi.Ground.y) * _resolution + Cell(poi.Ground.x);
            (_cells[cell] ??= new List<int>()).Add(i);
            _radii[i] = poi.Footprint.magnitude * 0.5f;
            _forwards[i] = poi.Forward;
            _reach = Mathf.Max(_reach, _radii[i]);
        }
    }

    public void Query(Vector2 point, float radius, List<int> result)
    {
        result.Clear();
        float reach = radius + _reach;
        int minX = Cell(point.x - reach), maxX = Cell(point.x + reach);
        int minY = Cell(point.y - reach), maxY = Cell(point.y + reach);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];
                if (bucket == null)
                    continue;

                foreach (int index in bucket)
                {
                    if ((_placements[index].Ground - point).sqrMagnitude <= reach * reach)
                        result.Add(index);
                }
            }
        }
    }

    public float Radius(int index) => _radii[index];

    public bool Occupied(Vector2 point, float margin, int ignore = -1)
    {
        float reach = margin + _reach;
        int minX = Cell(point.x - reach), maxX = Cell(point.x + reach);
        int minY = Cell(point.y - reach), maxY = Cell(point.y + reach);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                List<int> bucket = _cells[y * _resolution + x];
                if (bucket == null)
                    continue;

                foreach (int index in bucket)
                {
                    if (index != ignore && Contains(index, point, margin))
                        return true;
                }
            }
        }

        return false;
    }

    private bool Contains(int index, Vector2 point, float margin)
    {
        PoiPlacement poi = _placements[index];
        Vector2 delta = point - poi.Ground;
        Vector2 forward = _forwards[index];
        Vector2 right = new(forward.y, -forward.x);
        return Mathf.Abs(Vector2.Dot(delta, right)) <= poi.Footprint.x * 0.5f + margin
            && Mathf.Abs(Vector2.Dot(delta, forward)) <= poi.Footprint.y * 0.5f + margin;
    }

    private int Cell(float position) => Mathf.Clamp(Mathf.FloorToInt(position / CELL_SIZE), 0, _resolution - 1);
}

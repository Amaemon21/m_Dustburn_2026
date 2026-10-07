using UnityEngine;

public partial class RoadPlanner
{
    private void MarkApproachPoints()
    {
        foreach (int cell in _nearPointCells)
            _nearApproachPoint[cell] = false;

        _nearPointCells.Clear();

        float reach = _config.HighwayMinCurveRadius * APPROACH_TURN_RADII;
        int span = Mathf.CeilToInt(reach / _cellSize) + 1;

        foreach (Vector2 point in _approachPoints)
        {
            int column = Mathf.Clamp((int)(point.x / _cellSize), 0, _resolution - 1);
            int row = Mathf.Clamp((int)(point.y / _cellSize), 0, _resolution - 1);

            for (int y = Mathf.Max(0, row - span); y <= Mathf.Min(_resolution - 1, row + span); y++)
            {
                for (int x = Mathf.Max(0, column - span); x <= Mathf.Min(_resolution - 1, column + span); x++)
                {
                    int cell = y * _resolution + x;

                    if (_nearApproachPoint[cell] || !NearApproachPoint(cell))
                        continue;

                    _nearApproachPoint[cell] = true;
                    _nearPointCells.Add(cell);
                }
            }
        }
    }
}

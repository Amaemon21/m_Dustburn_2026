using System.Collections.Generic;
using UnityEngine;

public partial class SettlementPlanner
{
    private bool FindCore(IReadOnlyList<Hub> hubs, SettlementLayout layout, out int coreI, out int coreJ)
    {
        var order = new List<(int I, int J)>();

        for (int j = -CORE_SEARCH; j <= CORE_SEARCH; j++)
        {
            for (int i = -CORE_SEARCH; i <= CORE_SEARCH; i++)
                order.Add((i, j));
        }

        order.Sort((left, right) =>
        {
            int compare = (left.I * left.I + left.J * left.J).CompareTo(right.I * right.I + right.J * right.J);

            if (compare != 0)
                return compare;

            compare = left.J.CompareTo(right.J);
            return compare != 0 ? compare : left.I.CompareTo(right.I);
        });

        foreach ((int i, int j) in order)
        {
            if (!Buildable(hubs, layout, i, j))
                continue;

            coreI = i;
            coreJ = j;
            return true;
        }

        coreI = 0;
        coreJ = 0;
        return false;
    }

    private bool Buildable(IReadOnlyList<Hub> hubs, SettlementLayout layout, int i, int j)
    {
        long key = SettlementLayout.Key(i, j);

        if (_buildable.TryGetValue(key, out bool cached))
            return cached;

        bool result = Evaluate(hubs, layout, i, j);
        _buildable[key] = result;

        return result;
    }

    private bool Evaluate(IReadOnlyList<Hub> hubs, SettlementLayout layout, int i, int j)
    {
        Vector2 center = layout.TileCenter(i, j);
        float half = layout.TileSize * 0.5f;
        float margin = _config.HighwaySettlementClearance;
        float low = float.MaxValue;
        float high = float.MinValue;
        bool wet = false;

        for (int b = 0; b < TILE_SAMPLES; b++)
        {
            for (int a = 0; a < TILE_SAMPLES; a++)
            {
                float u = (a / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                float v = (b / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                Vector2 point = center + layout.AxisU * u + layout.AxisV * v;

                if (point.x < margin || point.y < margin || point.x > _config.WorldSize - margin || point.y > _config.WorldSize - margin)
                {
                    RefusedOutside++;
                    return false;
                }

                float height = _map.SampleWorldSmooth(point.x, point.y);

                wet |= WaterMap.Wet(_config, _water, point.x, point.y, height);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);

                if ((a == 0 || a == TILE_SAMPLES - 1 || a == TILE_SAMPLES / 2) && (b == 0 || b == TILE_SAMPLES - 1 || b == TILE_SAMPLES / 2)
                    && IsCrowded(hubs, layout.Index, point))
                {
                    RefusedCrowded++;
                    return false;
                }
            }
        }

        if (wet || HoldsStandingWater(layout, center, half + _config.SettlementPadSkirt))
        {
            RefusedFlooded++;
            return false;
        }

        if (high - low > _config.MaxTileRelief)
        {
            RefusedSteep++;
            return false;
        }

        return true;
    }

    private bool HoldsStandingWater(SettlementLayout layout, Vector2 center, float reach)
    {
        if (_water == null)
            return false;

        float extent = reach * Mathf.Sqrt(2f);
        int c0 = Mathf.Max(0, Mathf.FloorToInt((center.x - extent) / _water.CellSize));
        int c1 = Mathf.Min(_water.Resolution - 1, Mathf.FloorToInt((center.x + extent) / _water.CellSize));
        int r0 = Mathf.Max(0, Mathf.FloorToInt((center.y - extent) / _water.CellSize));
        int r1 = Mathf.Min(_water.Resolution - 1, Mathf.FloorToInt((center.y + extent) / _water.CellSize));

        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                int cell = r * _water.Resolution + c;
                var kind = (WaterKind)_water.Kinds[cell];

                if (kind != WaterKind.Lake && kind != WaterKind.Pond)
                    continue;

                Vector2 offset = _water.CellCenter(cell) - center;

                if (Mathf.Abs(Vector2.Dot(offset, layout.AxisU)) <= reach && Mathf.Abs(Vector2.Dot(offset, layout.AxisV)) <= reach)
                    return true;
            }
        }

        return false;
    }

    private bool IsCrowded(IReadOnlyList<Hub> hubs, int index, Vector2 point)
    {
        float own = (point - hubs[index].Position).magnitude + _config.SettlementGap;
        float ownSqr = own * own;

        for (int other = 0; other < hubs.Count; other++)
        {
            if (other != index && (point - hubs[other].Position).sqrMagnitude < ownSqr)
                return true;
        }

        return false;
    }

    private bool InWater(Vector2 point)
    {
        return _water != null && (_water.TryRiver(point.x, point.y, out _) || WaterMap.Wet(_config, _water, point.x, point.y, _map.SampleWorldSmooth(point.x, point.y)));
    }

    private bool Outside(Vector2 point)
    {
        return point.x < 0f || point.y < 0f || point.x > _config.WorldSize || point.y > _config.WorldSize;
    }

    private static float Wrap(float angle)
    {
        while (angle > Mathf.PI)
            angle -= Mathf.PI * 2f;

        while (angle < -Mathf.PI)
            angle += Mathf.PI * 2f;

        return angle;
    }
}

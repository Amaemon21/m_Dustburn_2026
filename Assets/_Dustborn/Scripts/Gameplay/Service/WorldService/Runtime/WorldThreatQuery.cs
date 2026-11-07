using System.Collections.Generic;
using UnityEngine;

public sealed class WorldThreatQuery
{
    private const float CELL_SIZE = 128f;
    private readonly Dictionary<long, List<WorldThreatZone>> _cells = new();
    private readonly Vector2 _start;
    private readonly float _safeSquare;

    public WorldThreatQuery(WorldGameplayPlan plan)
    {
        _start = new Vector2(plan.Spawn.x, plan.Spawn.z);
        _safeSquare = plan.SafeRadius * plan.SafeRadius;
        foreach (WorldThreatZone zone in plan.ThreatZones)
        {
            if (zone.Profile == PoiThreatProfile.Quiet)
                continue;

            int minX = Cell(zone.Center.x - zone.Radius), maxX = Cell(zone.Center.x + zone.Radius);
            int minY = Cell(zone.Center.y - zone.Radius), maxY = Cell(zone.Center.y + zone.Radius);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long key = Key(x, y);
                    if (!_cells.TryGetValue(key, out List<WorldThreatZone> bucket))
                    {
                        bucket = new List<WorldThreatZone>();
                        _cells.Add(key, bucket);
                    }

                    bucket.Add(zone);
                }
            }
        }
    }

    public int At(Vector2 point)
    {
        if ((point - _start).sqrMagnitude <= _safeSquare || !_cells.TryGetValue(Key(Cell(point.x), Cell(point.y)), out List<WorldThreatZone> zones))
            return 0;

        int tier = 0;
        foreach (WorldThreatZone zone in zones)
        {
            if ((point - zone.Center).sqrMagnitude <= zone.Radius * zone.Radius)
                tier = System.Math.Max(tier, zone.Tier);
        }

        return tier;
    }

    private static int Cell(float value) => Mathf.FloorToInt(value / CELL_SIZE);
    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}

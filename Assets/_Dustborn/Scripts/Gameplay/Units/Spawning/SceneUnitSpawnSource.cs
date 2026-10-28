using System;
using System.Collections.Generic;

public sealed class SceneUnitSpawnSource : IUnitSpawnSource
{
    private readonly UnitSpawnPoint[] _points;

    public SceneUnitSpawnSource(UnitSpawnPoint[] points)
    {
        _points = points ?? Array.Empty<UnitSpawnPoint>();
    }

    public IEnumerable<UnitSpawnRequest> Requests()
    {
        foreach (UnitSpawnPoint point in _points)
        {
            if (point == null)
                continue;

            yield return point.ToRequest();
        }
    }
}

using System;
using System.Collections.Generic;

public sealed partial class WaterStampLakes
{
    private int[] Crowding()
    {
        int reach = Hydrology.ShoreReach(_config.Water, _grid.CellSize);

        if (reach <= 0)
            return null;

        int resolution = _grid.Resolution;
        var crowd = new int[_water.BodyIds.Length];
        var steps = new int[crowd.Length];
        var queue = new Queue<int>();
        Array.Fill(crowd, FREE);

        for (int cell = 0; cell < crowd.Length; cell++)
        {
            int owner = _water.BodyIds[cell] >= 0 ? _water.BodyIds[cell] : _config.SeaLevel > 0f && _grid.Height[cell] < _config.SeaLevel ? SEA : FREE;

            if (owner == FREE)
                continue;

            crowd[cell] = owner;
            queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();

            if (steps[cell] >= reach)
                continue;

            int column = cell % resolution, row = cell / resolution;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int c = column + dx, r = row + dz;

                    if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                        continue;

                    int next = r * resolution + c;

                    if (crowd[next] == FREE)
                    {
                        crowd[next] = crowd[cell];
                        steps[next] = steps[cell] + 1;
                        queue.Enqueue(next);
                    }
                    else if (crowd[next] != crowd[cell] && crowd[next] != MANY && steps[next] > 0)
                    {
                        crowd[next] = MANY;
                    }
                }
            }
        }

        return crowd;
    }

    private bool Crowds(int cell, int id)
    {
        return _crowd != null && _crowd[cell] != FREE && _crowd[cell] != id;
    }

    private bool Containable(int cell, int id, float surface)
    {
        int resolution = _grid.Resolution;
        int column = cell % resolution, row = cell / resolution;
        float floor = surface + CONTAIN_MARGIN;

        for (int dz = -OTHER_BODY_REACH; dz <= OTHER_BODY_REACH; dz++)
        {
            for (int dx = -OTHER_BODY_REACH; dx <= OTHER_BODY_REACH; dx++)
            {
                int c = column + dx, r = row + dz;

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    return false;

                int next = r * resolution + c;
                int other = _water.BodyIds[next];
                int basin = _grid.Basin[next];

                if (other >= 0 && other != id || basin >= 0 && basin != _basin && _held.Contains(basin))
                    return false;

                if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1)
                    continue;

                if (_grid.Outlet[next] || _grid.Filled[next] < floor)
                    return false;
            }
        }

        return true;
    }

    private bool Foreign(int cell, int id)
    {
        int resolution = _grid.Resolution;
        int column = cell % resolution, row = cell / resolution;

        for (int dz = -OTHER_BODY_REACH; dz <= OTHER_BODY_REACH; dz++)
        {
            for (int dx = -OTHER_BODY_REACH; dx <= OTHER_BODY_REACH; dx++)
            {
                int c = column + dx, r = row + dz;

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int other = _water.BodyIds[r * resolution + c];

                if (other >= 0 && other != id)
                    return true;
            }
        }

        return false;
    }
}

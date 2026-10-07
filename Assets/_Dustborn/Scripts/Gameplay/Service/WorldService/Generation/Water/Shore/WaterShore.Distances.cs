using UnityEngine;

public static partial class WaterShore
{
    private static void Distances(WaterMap water)
    {
        for (int i = 0; i < water.ShoreDistance.Length; i++)
        {
            water.ShoreDistance[i] = float.PositiveInfinity;
            water.ShoreSurface[i] = float.NegativeInfinity;
        }

        Seed(water);
        Chamfer(water);
    }

    private static void Seed(WaterMap water)
    {
        int resolution = water.Resolution;
        float cell = water.CellSize;

        for (int i = 0; i < water.Kinds.Length; i++)
        {
            var kind = (WaterKind)water.Kinds[i];

            if (kind == WaterKind.None)
                continue;

            water.ShoreDistance[i] = 0f;
            water.ShoreSurface[i] = kind == WaterKind.Sea ? water.SeaLevel : water.Bodies[water.BodyIds[i]].Surface;
        }

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (point.Submerged)
                    continue;

                float half = point.Width * 0.5f;
                int reach = Mathf.CeilToInt(half / cell) + 1;
                int column = Mathf.FloorToInt(point.Position.x / cell);
                int row = Mathf.FloorToInt(point.Position.y / cell);

                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int c = column + dx, r = row + dz;

                        if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                            continue;

                        int index = r * resolution + c;
                        var center = new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
                        float distance = Mathf.Max(0f, Vector2.Distance(center, point.Position) - half);

                        if (distance >= water.ShoreDistance[index])
                            continue;

                        water.ShoreDistance[index] = distance;
                        water.ShoreSurface[index] = point.Surface;
                    }
                }
            }
        }
    }

    private static void Chamfer(WaterMap water)
    {
        int resolution = water.Resolution;
        float straight = water.CellSize;
        float diagonal = water.CellSize * 1.41421356f;
        float[] distance = water.ShoreDistance;
        float[] surface = water.ShoreSurface;

        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;

                Relax(distance, surface, index, column - 1, row, straight, resolution);
                Relax(distance, surface, index, column - 1, row - 1, diagonal, resolution);
                Relax(distance, surface, index, column, row - 1, straight, resolution);
                Relax(distance, surface, index, column + 1, row - 1, diagonal, resolution);
            }
        }

        for (int row = resolution - 1; row >= 0; row--)
        {
            for (int column = resolution - 1; column >= 0; column--)
            {
                int index = row * resolution + column;

                Relax(distance, surface, index, column + 1, row, straight, resolution);
                Relax(distance, surface, index, column + 1, row + 1, diagonal, resolution);
                Relax(distance, surface, index, column, row + 1, straight, resolution);
                Relax(distance, surface, index, column - 1, row + 1, diagonal, resolution);
            }
        }
    }

    private static void Relax(float[] distance, float[] surface, int index, int column, int row, float step, int resolution)
    {
        if (column < 0 || row < 0 || column >= resolution || row >= resolution)
            return;

        int other = row * resolution + column;
        float candidate = distance[other] + step;

        if (candidate >= distance[index])
            return;

        distance[index] = candidate;
        surface[index] = surface[other];
    }
}

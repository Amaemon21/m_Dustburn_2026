using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private void SelectLakes(HydrologyGrid grid, WaterMap water, List<HydrologyBasin> owners)
    {
        float cellArea = grid.CellSize * grid.CellSize;
        var centers = new List<(Vector2 Center, float Spacing)>();
        var candidates = new List<(HydrologyBasin Basin, float Surface, float Area, bool Big)>();

        foreach (HydrologyBasin basin in Basins)
        {
            float surface = basin.Spill - LAKE_DROP - (basin.River ? Mathf.Min(_settings.OutletErosion, basin.Depth * 0.5f) : 0f);

            if (basin.River && DrainsToDryLand(grid, basin))
                surface = Mathf.Min(surface, DryShore(grid, basin) - LAKE_DROP);

            if (_config.SeaLevel > 0f && surface <= _config.SeaLevel + WaterMap.LAGOON_BAR)
                continue;

            if (!basin.River && DryBasin(grid, basin))
                continue;

            int flooded = 0;

            foreach (int cell in basin.Cells)
            {
                if (grid.Height[cell] < surface)
                    flooded++;
            }

            float area = flooded * cellArea;

            if (area < _settings.MinPondArea || basin.Depth < _settings.MinLakeDepth / 3f)
                continue;

            bool big = area >= _settings.MinLakeArea && basin.Depth >= _settings.MinLakeDepth;

            if (!basin.River)
            {
                if (big && area > _settings.MaxLakeArea)
                    continue;

                float roll = Roll(basin.Cells[0]);
                float abundance = _climate == null ? 1f : _climate.Abundance(basin.Center.x, basin.Center.y);

                if (roll >= (big ? _settings.LakeDensity : _settings.PondDensity) * abundance)
                    continue;

                float spacing = big ? _settings.LakeSpacing : _settings.LakeSpacing * 0.5f;

                if (Crowded(centers, basin.Center, spacing))
                    continue;

                centers.Add((basin.Center, spacing));
            }

            candidates.Add((basin, surface, area, big));
        }

        bool[] keep = KeepApart(grid, candidates);

        for (int k = 0; k < candidates.Count; k++)
        {
            if (!keep[k])
            {
                CrowdedBodies++;
                continue;
            }

            (HydrologyBasin basin, float surface, float area, bool big) = candidates[k];
            int id = water.Bodies.Count;

            water.Bodies.Add(new WaterBody
            {
                Kind = big ? WaterKind.Lake : WaterKind.Pond,
                Surface = surface,
                Area = area,
                Depth = basin.Depth,
                Center = basin.Center
            });

            owners.Add(basin);

            if (big)
                Lakes++;
            else
                Ponds++;

            foreach (int cell in basin.Cells)
            {
                if (grid.Height[cell] < surface)
                    water.BodyIds[cell] = (short)id;
            }
        }
    }

    private bool[] KeepApart(HydrologyGrid grid, List<(HydrologyBasin Basin, float Surface, float Area, bool Big)> candidates)
    {
        var keep = new bool[candidates.Count];
        int reach = ShoreReach(grid.CellSize);

        if (reach <= 0)
        {
            Array.Fill(keep, true);
            return keep;
        }

        var order = new List<int>(candidates.Count);
        var anchored = new bool[candidates.Count];

        for (int k = 0; k < candidates.Count; k++)
        {
            order.Add(k);
            anchored[k] = candidates[k].Basin.River && (candidates[k].Big || DrainsToDryLand(grid, candidates[k].Basin));
        }

        order.Sort((a, b) =>
        {
            if (anchored[a] != anchored[b])
                return anchored[a] ? -1 : 1;

            return candidates[a].Area != candidates[b].Area ? candidates[b].Area.CompareTo(candidates[a].Area) : a.CompareTo(b);
        });

        var claim = new int[grid.Height.Length];
        Array.Fill(claim, -1);
        var seeds = new List<int>();

        for (int cell = 0; cell < claim.Length; cell++)
        {
            if (_config.SeaLevel > 0f && grid.Height[cell] < _config.SeaLevel)
                seeds.Add(cell);
        }

        for (int cell = 0; cell < _main.Length; cell++)
        {
            if (_main[cell])
                seeds.Add(cell);
        }

        Claim(grid, claim, seeds, reach, int.MaxValue);
        int[] riverSide = RiverSide(grid);

        foreach (int k in order)
        {
            (HydrologyBasin basin, float surface, _, bool big) = candidates[k];
            seeds.Clear();

            foreach (int cell in basin.Cells)
            {
                if (grid.Height[cell] < surface)
                    seeds.Add(cell);
            }

            bool crowded = false;

            foreach (int cell in seeds)
            {
                if ((claim[cell] < 0 || claim[cell] == k) && (basin.River || riverSide[cell] < 0))
                    continue;

                crowded = true;
                break;
            }

            if (crowded && !anchored[k])
                continue;

            keep[k] = true;
            Claim(grid, claim, seeds, reach, k);
        }

        return keep;
    }

    private static bool DrainsToDryLand(HydrologyGrid grid, HydrologyBasin basin)
    {
        int current = basin.Cells[0];

        for (int step = 0; step < grid.Receiver.Length && current >= 0; step++)
        {
            if (grid.Outlet[current])
                return grid.Sink[current];

            int other = grid.Basin[current];

            if (other >= 0 && other != basin.Id)
                return false;

            current = grid.Receiver[current];
        }

        return false;
    }

    private int[] RiverSide(HydrologyGrid grid)
    {
        var side = new int[grid.Height.Length];
        Array.Fill(side, -1);
        float start = RiverStartCells(grid);
        var seeds = new List<int>();

        for (int cell = 0; cell < side.Length; cell++)
        {
            if (grid.Accumulation[cell] >= start && !grid.Outlet[cell] && grid.Basin[cell] < 0)
                seeds.Add(cell);
        }

        for (int cell = 0; cell < _main.Length; cell++)
        {
            if (_main[cell] && grid.Basin[cell] < 0)
                seeds.Add(cell);
        }

        Claim(grid, side, seeds, Mathf.CeilToInt(RIVER_POND_GAP / grid.CellSize), 0);
        return side;
    }

    private static void Claim(HydrologyGrid grid, int[] claim, List<int> seeds, int reach, int owner)
    {
        int resolution = grid.Resolution;
        var distance = new Dictionary<int, int>(seeds.Count * 4);
        var queue = new Queue<int>();

        foreach (int cell in seeds)
        {
            if (distance.ContainsKey(cell))
                continue;

            distance[cell] = 0;
            queue.Enqueue(cell);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int steps = distance[cell];

            if (claim[cell] < 0)
                claim[cell] = owner;

            if (steps >= reach)
                continue;

            int column = cell % resolution, row = cell / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction], w = row + OffsetZ[direction];

                if (c < 0 || w < 0 || c >= resolution || w >= resolution)
                    continue;

                int next = w * resolution + c;

                if (distance.ContainsKey(next))
                    continue;

                distance[next] = steps + 1;
                queue.Enqueue(next);
            }
        }
    }

    private float Roll(int cell)
    {
        unchecked
        {
            uint hash = (uint)_config.Seed * 2654435761u ^ (uint)(cell + 1) * 2246822519u;

            hash ^= hash >> 15;
            hash *= 3266489917u;
            hash ^= hash >> 13;

            return (hash & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private int ShoreReach(float cell)
    {
        return ShoreReach(_settings, cell);
    }

    public static int ShoreReach(WaterGenerationSettings settings, float cell)
    {
        return settings.MinShoreGap <= 0f ? 0 : Mathf.CeilToInt(settings.MinShoreGap / cell) + SHORE_SLACK;
    }
}

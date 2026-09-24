using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class WaterShore
{
    public const float SHELF = 0.35f;
    public const float DRY_LIFT = 0.05f;
    public const float SUNK_DEPTH = 0.1f;
    public const float SHELF_FADE_START = 1f;
    public const float SHELF_FADE_END = 2.5f;
    public const float DAM_REACH = 6f;
    public const int RESURFACE_PASSES = 3;
    public const float RESURFACE_TOLERANCE = 0.1f;

    public static StandingWaterRegions Finish(WaterMap water, HeightMap map)
    {
        var regions = new StandingWaterRegions(water, map);

        regions.Build(SHELF);
        regions.Publish();

        for (int pass = 0; pass < RESURFACE_PASSES && Resurface(water); pass++)
        {
            regions.Build(SHELF);
            regions.Publish();
        }

        Distances(water);

        Shelf(water, map, regions);

        regions.Publish();
        Distances(water);
        water.MarkShoreReady();

        return regions;
    }

    private static bool Resurface(WaterMap water)
    {
        bool changed = false;

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (!point.Submerged || water.Covers(point.Position.x, point.Position.y, out short owner) && Mathf.Abs(water.LevelOf(owner) - point.Surface) <= RESURFACE_TOLERANCE)
                    continue;

                if (water.InsideEarlierRiver(river, point.Position, 0.5f * point.Width, out float trunk) && point.Surface <= trunk + Hydrology.MERGE_TOLERANCE)
                    continue;

                point.Submerged = false;
                points[i] = point;
                changed = true;
            }
        }

        if (changed)
            water.InvalidateIndex();

        return changed;
    }

    public static void Refresh(WaterMap water, HeightMap map)
    {
        Classify(water, map, (cell, i, j) => StoredOwner(water, cell, i, j), cell => water.DetailSlot[cell] >= 0 || water.Kinds[cell] != 0);
        Distances(water);
    }

    private static short StoredOwner(WaterMap water, int cell, int i, int j)
    {
        int start = water.DetailStart(cell);

        if (start < 0)
            return water.CellOwner(cell);

        int local = (j - cell / water.Resolution * WaterMap.SUBDIVISION) * WaterMap.CELL_SIDE_NODES + i - cell % water.Resolution * WaterMap.SUBDIVISION;

        return water.DetailOwner(start + local);
    }

    public static void Classify(WaterMap water, HeightMap map, Func<int, int, int, short> ownerOf, Func<int, bool> consider)
    {
        int resolution = water.Resolution;
        int sub = WaterMap.SUBDIVISION;
        float step = water.NodeStep;

        var rowCells = new List<int>[resolution];
        var rowOwners = new List<short>[resolution];
        var rowGround = new List<float>[resolution];

        Parallel.For(0, resolution, row =>
        {
            var owners = new short[WaterMap.CELL_NODES];
            var ground = new float[WaterMap.CELL_NODES];
            var tally = new List<(short Owner, int Wet, int Mesh)>(4);

            for (int column = 0; column < resolution; column++)
            {
                int cell = row * resolution + column;

                if (!consider(cell))
                    continue;

                for (int k = 0; k < WaterMap.CELL_NODES; k++)
                {
                    int i = column * sub + k % WaterMap.CELL_SIDE_NODES;
                    int j = row * sub + k / WaterMap.CELL_SIDE_NODES;

                    owners[k] = ownerOf(cell, i, j);
                    ground[k] = map.SampleWorldSmooth(i * step, j * step);
                }

                if (!Evaluate(water, owners, ground, tally, out WaterKind kind, out short body))
                {
                    water.Kinds[cell] = (byte)kind;
                    water.BodyIds[cell] = body;
                    continue;
                }

                water.Kinds[cell] = (byte)kind;
                water.BodyIds[cell] = body;

                rowCells[row] ??= new List<int>();
                rowOwners[row] ??= new List<short>();
                rowGround[row] ??= new List<float>();

                rowCells[row].Add(cell);
                rowOwners[row].AddRange(owners);
                rowGround[row].AddRange(ground);
            }
        });

        var cells = new List<int>();
        var allOwners = new List<short>();
        var allGround = new List<float>();

        for (int row = 0; row < resolution; row++)
        {
            if (rowCells[row] == null)
                continue;

            cells.AddRange(rowCells[row]);
            allOwners.AddRange(rowOwners[row]);
            allGround.AddRange(rowGround[row]);
        }

        water.SetDetail(cells.ToArray(), allOwners.ToArray(), allGround.ToArray());
    }

    private static bool Evaluate(WaterMap water, short[] owners, float[] ground, List<(short Owner, int Wet, int Mesh)> tally,
        out WaterKind kind, out short body)
    {
        tally.Clear();

        bool anyMesh = false, allMesh = true, same = true, seaWet = false;
        short first = owners[0];

        for (int k = 0; k < owners.Length; k++)
        {
            short owner = owners[k];
            float level = water.LevelOf(owner);
            bool mesh = WaterMap.MeshInside(level, ground[k]);
            bool wet = ground[k] < level;

            anyMesh |= mesh;
            allMesh &= mesh;
            same &= owner == first;

            if (owner == WaterMap.OWNER_SEA)
                seaWet |= wet;

            if (owner < 0 || !mesh)
                continue;

            int at = tally.Count - 1;

            while (at >= 0 && tally[at].Owner != owner)
                at--;

            if (at < 0)
                tally.Add((owner, wet ? 1 : 0, 1));
            else
                tally[at] = (owner, tally[at].Wet + (wet ? 1 : 0), tally[at].Mesh + 1);
        }

        body = -1;
        int bestWet = 0, bestMesh = 0;

        foreach ((short owner, int wet, int mesh) in tally)
        {
            if (wet > bestWet || wet == bestWet && mesh > bestMesh || wet == bestWet && mesh == bestMesh && body >= 0 && owner < body)
            {
                body = owner;
                bestWet = wet;
                bestMesh = mesh;
            }
        }

        kind = bestWet > 0 ? water.Bodies[body].Kind : seaWet ? WaterKind.Sea : WaterKind.None;

        if (!anyMesh)
        {
            kind = WaterKind.None;
            body = -1;
            return false;
        }

        bool full = allMesh && same && first != WaterMap.OWNER_NONE && (first >= 0 ? bestWet > 0 : seaWet);

        if (full)
        {
            kind = water.KindOf(first);
            body = first >= 0 ? first : (short)-1;
            return false;
        }

        return true;
    }

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

    private static void Shelf(WaterMap water, HeightMap map, StandingWaterRegions regions)
    {
        int resolution = map.Resolution;
        float texel = (float)map.WorldSize / (resolution - 1);
        float maxHeight = map.MaxHeight;
        float[] heights = map.Heights;
        float near = water.CellSize * SHELF_FADE_END + water.CellSize;
        int cells = water.Resolution;
        var rows = new bool[cells];

        for (int cell = 0; cell < water.ShoreDistance.Length; cell++)
            rows[cell / cells] |= water.ShoreDistance[cell] <= near;

        Parallel.For(0, resolution, row =>
        {
            float z = row * texel;
            int cellRow = Mathf.Clamp((int)(z / water.CellSize), 0, cells - 1);

            if (!rows[cellRow])
                return;

            for (int column = 0; column < resolution; column++)
            {
                float x = column * texel;
                int cellColumn = Mathf.Clamp((int)(x / water.CellSize), 0, cells - 1);
                int cell = cellRow * cells + cellColumn;

                if (!(water.ShoreDistance[cell] <= near))
                {
                    column = Mathf.Max(column, Mathf.CeilToInt((cellColumn + 1) * water.CellSize / texel) - 2);
                    continue;
                }

                int index = row * resolution + column;
                float ground = heights[index] * maxHeight;
                int node = regions.NearestNode(x, z);

                if (regions.IsSunk(node))
                {
                    float level = water.LevelOf(regions.Owner(node));

                    if (ground > level - SUNK_DEPTH)
                        heights[index] = (level - SUNK_DEPTH) / maxHeight;

                    continue;
                }

                if (!Target(water, regions, x, z, ground, out float surface, out bool covered, out float reachWeight))
                    continue;

                float difference = ground - surface;
                float magnitude = Mathf.Abs(difference);

                if (magnitude >= SHELF)
                    continue;

                if (!covered && difference < 0f && water.TryRiver(x, z, out _))
                    continue;

                float steep = SHELF * Mathf.Sqrt(magnitude / SHELF);

                float weight = reachWeight * (1f - Smooth(water.CellSize * SHELF_FADE_START, water.CellSize * SHELF_FADE_END, SmoothDistance(water, x, z)));
                float shelved = surface + (difference < 0f && covered ? -steep : Mathf.Max(steep, DRY_LIFT));

                heights[index] = Mathf.Lerp(ground, shelved, weight) / maxHeight;
            }
        });
    }

    private static bool Target(WaterMap water, StandingWaterRegions regions, float x, float z, float ground, out float surface, out bool covered, out float weight)
    {
        weight = 1f;

        if (water.Covers(x, z, out short owner))
        {
            surface = water.LevelOf(owner);
            covered = true;
            return true;
        }

        covered = false;
        bool drowned = water.SeaLevel > 0f && ground < water.SeaLevel;
        float reach = Mathf.Max(DAM_REACH, 2f * water.NodeStep);

        if (!drowned && regions.NearestBody(x, z, reach, out short body, out float distance))
        {
            surface = water.LevelOf(body);

            if (Mathf.Abs(ground - surface) < SHELF)
            {
                weight = 1f - Smooth(0.5f * reach, reach, distance);
                return true;
            }
        }

        surface = water.SeaLevel;
        return water.SeaLevel > 0f && Mathf.Abs(ground - water.SeaLevel) < SHELF;
    }

    private static float SmoothDistance(WaterMap water, float x, float z)
    {
        float cell = water.CellSize;
        int last = water.Resolution - 1;
        float u = Mathf.Clamp(x / cell - 0.5f, 0f, last);
        float v = Mathf.Clamp(z / cell - 0.5f, 0f, last);
        int column = Mathf.Min((int)u, last - 1);
        int row = Mathf.Min((int)v, last - 1);
        int origin = row * water.Resolution + column;
        float[] distance = water.ShoreDistance;

        float a = distance[origin], b = distance[origin + 1];
        float c = distance[origin + water.Resolution], d = distance[origin + water.Resolution + 1];

        if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsInfinity(c) || float.IsInfinity(d))
            return Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)) + cell;

        return Mathf.Lerp(Mathf.Lerp(a, b, u - column), Mathf.Lerp(c, d, u - column), v - row);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / (to - from));

        return t * t * (3f - 2f * t);
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

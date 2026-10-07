using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Hydrology
{
    private static float[] Floors(HydrologyGrid grid, WaterMap water)
    {
        var floors = new float[water.Bodies.Count];
        Array.Fill(floors, float.MaxValue);

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            int body = water.BodyIds[cell];

            if (body >= 0)
                floors[body] = Mathf.Min(floors[body], grid.Height[cell]);
        }

        return floors;
    }

    private void DropPerched(WaterMap water)
    {
        int count = water.Bodies.Count;
        var entered = new bool[count];
        var perched = new bool[count];

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                Vector2 at = point.Position;
                int inside = LakeNear(water, at.x, at.y, 0.5f * point.Width + water.CellSize);

                if (inside >= 0 && Mathf.Abs(water.Bodies[inside].Surface - point.Surface) <= PERCHED_RISE)
                    entered[inside] = true;

                if (point.Submerged)
                    continue;

                int beside = LakeNear(water, at.x, at.y, 0.5f * point.Width + RIVER_POND_GAP);

                if (beside >= 0 && water.Bodies[beside].Surface > point.Surface + PERCHED_RISE)
                    perched[beside] = true;
            }
        }

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            int body = water.BodyIds[cell];

            if (body < 0 || !perched[body] || entered[body] || water.Bodies[body].Kind != WaterKind.Pond)
                continue;

            water.BodyIds[cell] = -1;
        }

        for (int body = 0; body < count; body++)
        {
            if (!perched[body] || entered[body] || water.Bodies[body].Kind != WaterKind.Pond)
                continue;

            PerchedBodies++;
            Log.Note(HydrologyAction.BodyPerched, -1, water.Bodies[body].Center, $"pond {body} sits over {PERCHED_RISE:0.0} m above an open river beside it and no river enters it");
        }
    }

    private void PruneCrowded(HydrologyGrid grid, WaterMap water, List<RiverCourse> courses)
    {
        int reach = ShoreReach(grid.CellSize);

        if (reach <= 0 || water.Bodies.Count == 0)
            return;

        int resolution = grid.Resolution;
        var touched = new bool[water.Bodies.Count];
        var cells = new List<int>[water.Bodies.Count];

        for (int body = 0; body < cells.Length; body++)
            cells[body] = new List<int>();

        for (int cell = 0; cell < water.BodyIds.Length; cell++)
        {
            if (water.BodyIds[cell] >= 0)
                cells[water.BodyIds[cell]].Add(cell);
        }

        foreach (RiverCourse course in courses)
        {
            for (int i = 0; i < course.Points.Count; i++)
            {
                int body = LakeNear(water, course.Points[i].x, course.Points[i].y, 0.5f * Width(course.Areas[i], course.MaxWidth) + grid.CellSize);

                if (body >= 0 && (water.Bodies[body].Kind == WaterKind.Lake || i == 0 || i == course.Points.Count - 1))
                    touched[body] = true;
            }
        }

        if (Stamps != null)
        {
            foreach (WaterStampPlacement placement in Stamps.Placements)
            {
                if (placement.Body >= 0 && placement.Body < touched.Length)
                    touched[placement.Body] = true;
            }
        }

        var order = new List<int>();

        for (int body = 0; body < cells.Length; body++)
        {
            if (!touched[body] && cells[body].Count > 0)
                order.Add(body);
        }

        order.Sort((a, b) => cells[a].Count != cells[b].Count ? cells[a].Count.CompareTo(cells[b].Count) : a.CompareTo(b));

        foreach (int body in order)
        {
            if (!Crowded(grid, water, cells, body, reach))
                continue;

            foreach (int cell in cells[body])
                water.BodyIds[cell] = -1;

            cells[body].Clear();
            CrowdedBodies++;
            Log.Note(HydrologyAction.BodyCrowded, -1, water.Bodies[body].Center, $"body {body} lies within the shore gap of bigger water and no river touches it");
        }
    }

    private bool Crowded(HydrologyGrid grid, WaterMap water, List<int>[] bodies, int body, int reach)
    {
        int resolution = grid.Resolution;

        foreach (int cell in bodies[body])
        {
            int column = cell % resolution, row = cell / resolution;

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int c = column + dx, w = row + dz;

                    if (c < 0 || w < 0 || c >= resolution || w >= resolution || dx * dx + dz * dz > reach * reach)
                        continue;

                    int near = w * resolution + c;
                    int other = water.BodyIds[near];

                    if (other >= 0 && other != body && bodies[other].Count >= bodies[body].Count || other < 0 && _config.SeaLevel > 0f && grid.Height[near] < _config.SeaLevel)
                        return true;
                }
            }
        }

        return false;
    }

    private void Touching(HydrologyGrid grid, WaterMap water, Dictionary<int, float> lowered)
    {
        int resolution = grid.Resolution;
        short[] ids = water.BodyIds;
        var sills = new Dictionary<(int High, int Low), float>();

        for (int cell = 0; cell < ids.Length; cell++)
        {
            int high = ids[cell];

            if (high < 0)
                continue;

            int column = cell % resolution, row = cell / resolution;

            for (int direction = 0; direction < 8; direction++)
            {
                int c = column + OffsetX[direction], r = row + OffsetZ[direction];

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                int next = r * resolution + c;
                int low = ids[next];

                if (low < 0 || low == high || water.Bodies[high].Surface <= water.Bodies[low].Surface + LOWER_MARGIN)
                    continue;

                float sill = Mathf.Max(grid.Height[cell], grid.Height[next]);
                sills[(high, low)] = sills.TryGetValue((high, low), out float known) ? Mathf.Min(known, sill) : sill;
            }
        }

        foreach (KeyValuePair<(int High, int Low), float> pair in sills)
        {
            (int high, int low) = pair.Key;
            float target = Mathf.Max(water.Bodies[low].Surface, pair.Value - LOWER_MARGIN);

            if (target >= water.Bodies[high].Surface - LOWER_MARGIN || lowered.TryGetValue(high, out float current) && current <= target)
                continue;

            lowered[high] = target;
            _lowerReasons[high] = $"it touches body {low} at {water.Bodies[low].Surface:0.00} m over a sill of {pair.Value:0.00} m";
        }
    }

    private static void Lower(HydrologyGrid grid, WaterMap water, HydrologyBasin basin, int id, float surface)
    {
        WaterBody body = water.Bodies[id];

        if (surface >= body.Surface)
            return;

        int flooded = 0;

        foreach (int cell in basin.Cells)
        {
            bool wet = grid.Height[cell] < surface;

            if (water.BodyIds[cell] == id && !wet)
                water.BodyIds[cell] = -1;
            else if (wet)
                water.BodyIds[cell] = (short)id;

            if (wet)
                flooded++;
        }

        body.Surface = surface;
        body.Area = flooded * grid.CellSize * grid.CellSize;
        water.Bodies[id] = body;
    }

    private static bool Crowded(List<(Vector2 Center, float Spacing)> centers, Vector2 center, float spacing)
    {
        foreach ((Vector2 other, float otherSpacing) in centers)
        {
            float gap = Mathf.Min(spacing, otherSpacing);

            if ((other - center).sqrMagnitude < gap * gap)
                return true;
        }

        return false;
    }

    public static int LakeNear(WaterMap water, float x, float z, float reach)
    {
        int resolution = water.Resolution;
        int column = Mathf.Clamp((int)(x / water.CellSize), 0, resolution - 1);
        int row = Mathf.Clamp((int)(z / water.CellSize), 0, resolution - 1);

        int own = water.BodyIds[row * resolution + column];

        if (own >= 0)
            return own;

        int found = -1;
        float lowest = float.MaxValue;

        int span = Mathf.Max(1, Mathf.CeilToInt(reach / water.CellSize));

        for (int dz = -span; dz <= span; dz++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                int c = column + dx, r = row + dz;

                if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                    continue;

                float nearX = Mathf.Clamp(x, c * water.CellSize, (c + 1) * water.CellSize) - x;
                float nearZ = Mathf.Clamp(z, r * water.CellSize, (r + 1) * water.CellSize) - z;

                if (nearX * nearX + nearZ * nearZ > reach * reach)
                    continue;

                int id = water.BodyIds[r * resolution + c];

                if (id < 0 || water.Bodies[id].Surface >= lowest)
                    continue;

                lowest = water.Bodies[id].Surface;
                found = id;
            }
        }

        return found;
    }
}

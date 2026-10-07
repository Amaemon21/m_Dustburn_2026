using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class WaterStampLakes
{
    private void CellRange(WaterStampPlacement placement, int pad, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        placement.Bounds(out Vector2 min, out Vector2 max);
        int last = _grid.Resolution - 1;

        minX = Mathf.Max(0, Mathf.FloorToInt(min.x / _grid.CellSize) - pad);
        maxX = Mathf.Min(last, Mathf.CeilToInt(max.x / _grid.CellSize) + pad);
        minZ = Mathf.Max(0, Mathf.FloorToInt(min.y / _grid.CellSize) - pad);
        maxZ = Mathf.Min(last, Mathf.CeilToInt(max.y / _grid.CellSize) + pad);
    }

    private void CellRange(Vector2 min, Vector2 max, int pad, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        int last = _grid.Resolution - 1;

        minX = Mathf.Max(0, Mathf.FloorToInt(min.x / _grid.CellSize) - pad);
        maxX = Mathf.Min(last, Mathf.CeilToInt(max.x / _grid.CellSize) + pad);
        minZ = Mathf.Max(0, Mathf.FloorToInt(min.y / _grid.CellSize) - pad);
        maxZ = Mathf.Min(last, Mathf.CeilToInt(max.y / _grid.CellSize) + pad);
    }

    private void Carve(int id, HydrologyBasin basin, WaterStampPlacement placement)
    {
        WaterStampDefinition definition = _library.Get(placement.StampIndex);
        WaterStampShape shape = _library.Shape(placement.StampIndex);
        float surface = _water.Bodies[id].Surface;

        float reach = Reach(definition, placement);
        Region(id, basin, placement, reach, out Vector2 min, out Vector2 max);
        CellRange(min, max, 1, out int minX, out int maxX, out int minZ, out int maxZ);

        int width = maxX - minX + 1;
        int height = maxZ - minZ + 1;
        var contain = new float[width * height];
        var fillable = new float[width * height];
        var foreign = new float[width * height];

        for (int row = minZ; row <= maxZ; row++)
        {
            for (int column = minX; column <= maxX; column++)
            {
                int cell = row * _grid.Resolution + column;
                int local = (row - minZ) * width + column - minX;

                contain[local] = Containable(cell, id, surface) ? 1f : 0f;
                fillable[local] = _water.BodyIds[cell] == id || _grid.Basin[cell] == basin.Id ? 1f : 0f;
                foreign[local] = Foreign(cell, id) ? 1f : 0f;
            }
        }

        float earthworks = _settings.MaxLakeEarthworks;

        float Effective(Vector2 world)
        {
            float mask = Mask(shape, placement, world);

            if (mask <= 0f)
                return 0f;

            float held = WaterStampLibrary.SmoothStep(0.5f, 1f, Sample(contain, width, height, minX, minZ, world));
            float free = 1f - WaterStampLibrary.SmoothStep(0f, 0.5f, Sample(foreign, width, height, minX, minZ, world));
            float dig = 1f - WaterStampLibrary.SmoothStep(0.75f * earthworks, earthworks, _map.SampleWorldSmooth(world.x, world.y) - surface);

            return mask * held * free * dig;
        }

        ShoreField field = ShoreField.Build(Effective, min, max, FIELD_STEP);

        int resolution = _map.Resolution;
        float texel = (float)_map.WorldSize / (resolution - 1);
        float maxHeight = _map.MaxHeight;
        float[] heights = _map.Heights;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(min.x / texel)), x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(max.x / texel));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(min.y / texel)), z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(max.y / texel));

        float depth = Mathf.Max(SHELF_DEPTH + 0.1f, definition.RecommendedDepth);
        float influence = _settings.LakeInfluence;
        long changed = 0;

        Parallel.For(z0, z1 + 1, () => 0L, (row, _, count) =>
        {
            float z = row * texel;

            for (int column = x0; column <= x1; column++)
            {
                float x = column * texel;
                var world = new Vector2(x, z);
                float shore = field.Sample(world);
                int index = row * resolution + column;
                float ground = heights[index] * maxHeight;
                float target;

                if (shore < -reach && ground >= surface)
                    continue;

                if (shore >= 0f)
                {
                    float mask = Mask(shape, placement, world);
                    float profile = SHELF_DEPTH + (depth - SHELF_DEPTH) * WaterStampLibrary.SmoothStep(WaterStampTracer.LAKE_LEVEL, 1f, mask);
                    float bed = surface - Mathf.Min(shore * BEACH_SLOPE, profile);
                    target = Mathf.Min(ground, Mathf.Max(bed, ground - earthworks));
                }
                else
                {
                    float outside = -shore;
                    float bank = surface + outside * BANK_SLOPE;

                    target = Mathf.Min(ground, Mathf.Max(bank, ground - earthworks * Mathf.Max(0f, 1f - outside / reach)));

                    if (ground < surface + 2f * DRY_LIFT_MAX)
                    {
                        float lift = surface + Mathf.Min(outside * BEACH_SLOPE, DRY_LIFT_MAX);
                        float level = ground < surface ? lift : lift + 0.5f * (ground - surface);
                        float raised = Mathf.Min(Mathf.Max(target, level), ground + earthworks);
                        target = Mathf.Lerp(target, raised, Sample(fillable, width, height, minX, minZ, world));
                    }
                }

                target = ground + (target - ground) * influence * (1f - WaterStampLibrary.SmoothStep(0f, 0.5f, Sample(foreign, width, height, minX, minZ, world)));

                if (Mathf.Abs(target - ground) < 1e-4f)
                    continue;

                heights[index] = Mathf.Clamp(target, 0f, maxHeight) / maxHeight;
                count++;
            }

            return count;
        }, count => System.Threading.Interlocked.Add(ref changed, count));

        _layout.LakeCells += changed;
    }

    private void Region(int id, HydrologyBasin basin, WaterStampPlacement placement, float reach, out Vector2 min, out Vector2 max)
    {
        placement.Bounds(out min, out max);
        min -= new Vector2(reach, reach);
        max += new Vector2(reach, reach);

        foreach (int cell in basin.Cells)
        {
            if (_water.BodyIds[cell] != id)
                continue;

            Vector2 center = _water.CellCenter(cell);
            min = Vector2.Min(min, center - new Vector2(_grid.CellSize, _grid.CellSize));
            max = Vector2.Max(max, center + new Vector2(_grid.CellSize, _grid.CellSize));
        }

        min = Vector2.Max(min, Vector2.zero);
        max = Vector2.Min(max, new Vector2(_config.WorldSize, _config.WorldSize));
    }

    private float Reach(WaterStampDefinition definition, WaterStampPlacement placement)
    {
        return Mathf.Max(Mathf.Max(2f * FIELD_STEP, BANK_REACH_EARTHWORKS * _settings.MaxLakeEarthworks), definition.RecommendedBankWidth * placement.Scale);
    }

    private static float Mask(WaterStampShape shape, WaterStampPlacement placement, Vector2 world)
    {
        Vector2 uv = placement.Uv(placement.Stamp(world));

        return shape.Sample(uv.x, uv.y);
    }

    private float Sample(float[] values, int width, int height, int minX, int minZ, Vector2 world)
    {
        float u = Mathf.Clamp(world.x / _grid.CellSize - 0.5f - minX, 0f, width - 1.001f);
        float v = Mathf.Clamp(world.y / _grid.CellSize - 0.5f - minZ, 0f, height - 1.001f);
        int i = Mathf.Min((int)u, Mathf.Max(0, width - 2)), j = Mathf.Min((int)v, Mathf.Max(0, height - 2));
        float tx = Mathf.Clamp01(u - i), tz = Mathf.Clamp01(v - j);

        if (width < 2 || height < 2)
            return values[j * width + i];

        int origin = j * width + i;
        float top = Mathf.Lerp(values[origin], values[origin + 1], tx);
        float bottom = Mathf.Lerp(values[origin + width], values[origin + width + 1], tx);

        return Mathf.Lerp(top, bottom, tz);
    }

    private void Refresh(int id, HydrologyBasin basin, WaterStampPlacement placement)
    {
        WaterStampDefinition definition = _library.Get(placement.StampIndex);
        Region(id, basin, placement, Reach(definition, placement), out Vector2 min, out Vector2 max);
        CellRange(min, max, 1, out int minX, out int maxX, out int minZ, out int maxZ);

        WaterBody body = _water.Bodies[id];
        var members = new HashSet<int>(basin.Cells);

        for (int row = minZ; row <= maxZ; row++)
        {
            for (int column = minX; column <= maxX; column++)
            {
                int cell = row * _grid.Resolution + column;
                _grid.Height[cell] = _map.SampleWorldSmooth((column + 0.5f) * _grid.CellSize, (row + 0.5f) * _grid.CellSize);

                bool wet = _grid.Height[cell] < body.Surface;
                int owner = _water.BodyIds[cell];

                if (owner == id && !wet)
                {
                    _water.BodyIds[cell] = -1;
                    continue;
                }

                if (!wet || owner >= 0 && owner != id)
                    continue;

                if (!members.Contains(cell) && (!Containable(cell, id, body.Surface) || _grid.Basin[cell] >= 0 && _grid.Basin[cell] != basin.Id))
                    continue;

                _water.BodyIds[cell] = (short)id;

                if (members.Add(cell))
                    basin.Cells.Add(cell);
            }
        }

        int flooded = 0;
        float deepest = 0f;
        double sumX = 0, sumZ = 0;

        foreach (int cell in basin.Cells)
        {
            if (_water.BodyIds[cell] != id)
                continue;

            flooded++;
            deepest = Mathf.Max(deepest, body.Surface - _grid.Height[cell]);

            Vector2 center = _water.CellCenter(cell);
            sumX += center.x;
            sumZ += center.y;
        }

        body.Area = flooded * _grid.CellSize * _grid.CellSize;
        body.Depth = Mathf.Max(body.Depth, deepest);

        if (flooded > 0)
            body.Center = new Vector2((float)(sumX / flooded), (float)(sumZ / flooded));

        _water.Bodies[id] = body;
    }
}

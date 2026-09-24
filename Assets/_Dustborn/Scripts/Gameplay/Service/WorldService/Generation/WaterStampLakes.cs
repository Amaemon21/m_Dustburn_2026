using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class WaterStampLakes
{
    private const float SHELF_DEPTH = 0.3f;
    private const float DRY_LIFT = 0.4f;
    private const float CONTAIN_MARGIN = 0.05f;
    private const float MAX_CLIPPED = 0.35f;
    private const float BANK_SLOPE = 0.3f;
    private const float BEACH_SLOPE = 0.1f;
    private const float DRY_LIFT_MAX = 0.25f;
    private const float BANK_REACH_EARTHWORKS = 3f;
    private const float FIELD_STEP = 2f;
    private const float SEED_CELLS = 1.5f;
    private const float MIN_GRADIENT = 1e-4f;

    private sealed class ShoreField
    {
        private float[] _signed;
        private int _width;
        private int _height;
        private Vector2 _origin;
        private float _step;

        public static ShoreField Build(Func<Vector2, float> sample, Vector2 min, Vector2 max, float step)
        {
            var field = new ShoreField
            {
                _origin = min,
                _step = step,
                _width = Mathf.Max(2, Mathf.CeilToInt((max.x - min.x) / step) + 1),
                _height = Mathf.Max(2, Mathf.CeilToInt((max.y - min.y) / step) + 1)
            };

            int count = field._width * field._height;
            var mask = new float[count];
            var distance = new float[count];

            Parallel.For(0, field._height, j =>
            {
                for (int i = 0; i < field._width; i++)
                    mask[j * field._width + i] = sample(new Vector2(min.x + i * step, min.y + j * step));
            });

            float level = WaterStampTracer.LAKE_LEVEL;

            for (int j = 0; j < field._height; j++)
            {
                for (int i = 0; i < field._width; i++)
                {
                    int index = j * field._width + i;
                    distance[index] = float.MaxValue;

                    float dx = mask[j * field._width + Mathf.Min(i + 1, field._width - 1)] - mask[j * field._width + Mathf.Max(i - 1, 0)];
                    float dz = mask[Mathf.Min(j + 1, field._height - 1) * field._width + i] - mask[Mathf.Max(j - 1, 0) * field._width + i];
                    float gradient = 0.5f * Mathf.Sqrt(dx * dx + dz * dz) / step;

                    if (gradient < MIN_GRADIENT)
                        continue;

                    float estimate = Mathf.Abs(mask[index] - level) / gradient;

                    if (estimate <= SEED_CELLS * step)
                        distance[index] = estimate;
                }
            }

            Chamfer(distance, field._width, field._height, step);

            field._signed = new float[count];

            for (int index = 0; index < count; index++)
            {
                float value = distance[index] == float.MaxValue ? 1e6f : distance[index];
                field._signed[index] = mask[index] >= level ? value : -value;
            }

            return field;
        }

        public float Sample(Vector2 world)
        {
            float u = Mathf.Clamp((world.x - _origin.x) / _step, 0f, _width - 1.001f);
            float v = Mathf.Clamp((world.y - _origin.y) / _step, 0f, _height - 1.001f);
            int i = (int)u, j = (int)v;
            float tx = u - i, tz = v - j;
            int origin = j * _width + i;

            float top = Mathf.Lerp(_signed[origin], _signed[origin + 1], tx);
            float bottom = Mathf.Lerp(_signed[origin + _width], _signed[origin + _width + 1], tx);

            return Mathf.Lerp(top, bottom, tz);
        }

        private static void Chamfer(float[] distance, int width, int height, float step)
        {
            float diagonal = step * 1.41421356f;

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    Relax(distance, width, height, i, j, i - 1, j, step);
                    Relax(distance, width, height, i, j, i - 1, j - 1, diagonal);
                    Relax(distance, width, height, i, j, i, j - 1, step);
                    Relax(distance, width, height, i, j, i + 1, j - 1, diagonal);
                }
            }

            for (int j = height - 1; j >= 0; j--)
            {
                for (int i = width - 1; i >= 0; i--)
                {
                    Relax(distance, width, height, i, j, i + 1, j, step);
                    Relax(distance, width, height, i, j, i + 1, j + 1, diagonal);
                    Relax(distance, width, height, i, j, i, j + 1, step);
                    Relax(distance, width, height, i, j, i - 1, j + 1, diagonal);
                }
            }
        }

        private static void Relax(float[] distance, int width, int height, int i, int j, int ni, int nj, float step)
        {
            if (ni < 0 || nj < 0 || ni >= width || nj >= height)
                return;

            float other = distance[nj * width + ni];

            if (other == float.MaxValue)
                return;

            if (other + step < distance[j * width + i])
                distance[j * width + i] = other + step;
        }
    }
    private const float SCORE_STEP = 16f;
    private const float JITTER = 12f;
    private const float SMALLER = 0.9f;
    private const float OTHER_KIND = 0.3f;
    private const float HIGH_GROUND = 0.5f;
    private const int OTHER_BODY_REACH = 2;

    private readonly WorldGenerationConfig _config;
    private readonly WaterStampLibrary _library;
    private readonly WaterStampSettings _settings;
    private readonly HeightMap _map;
    private readonly HydrologyGrid _grid;
    private readonly WaterMap _water;
    private readonly WaterStampLayout _layout;
    private readonly HashSet<int> _held = new();
    private int _basin;

    private struct Fit
    {
        public WaterStampPlacement Placement;
        public float Score;
    }

    public WaterStampLakes(WorldGenerationConfig config, WaterStampLibrary library, HeightMap map, HydrologyGrid grid, WaterMap water, WaterStampLayout layout)
    {
        _config = config;
        _library = library;
        _settings = library.Settings;
        _map = map;
        _grid = grid;
        _water = water;
        _layout = layout;
    }

    public void Apply(List<HydrologyBasin> owners)
    {
        if (_library.Lakes.Count + _library.Ponds.Count == 0)
            return;

        foreach (HydrologyBasin owner in owners)
            _held.Add(owner.Id);

        for (int id = 0; id < _water.Bodies.Count; id++)
        {
            _basin = owners[id].Id;

            if (!TryFit(id, owners[id], out Fit best))
            {
                _layout.ProceduralBodies++;
                continue;
            }

            best.Placement.Body = id;
            _layout.Add(best.Placement);
            Carve(id, owners[id], best.Placement);
            Refresh(id, owners[id], best.Placement);
        }
    }

    private bool TryFit(int id, HydrologyBasin basin, out Fit best)
    {
        best = default;
        best.Score = 0f;

        WaterBody body = _water.Bodies[id];
        var flooded = new List<int>();

        foreach (int cell in basin.Cells)
        {
            if (_water.BodyIds[cell] == id)
                flooded.Add(cell);
        }

        if (flooded.Count < 4)
            return false;

        Moments(flooded, out Vector2 centroid, out float axis, out float major, out float minor);

        float area = flooded.Count * _grid.CellSize * _grid.CellSize;
        float aspect = major / Mathf.Max(1e-3f, minor);
        bool high = body.Surface > HIGH_GROUND * _config.MaxHeight;

        foreach (List<int> group in new[] { _library.Lakes, _library.Ponds })
        {
            foreach (int index in group)
            {
                WaterStampDefinition definition = _library.Get(index);
                float affinity = Affinity(definition, body.Kind, aspect, high);
                float natural = Mathf.Sqrt(area / definition.WaterArea);

                for (int size = 0; size < 2; size++)
                {
                    float scale = size == 0 ? natural : natural * SMALLER;

                    if (scale < definition.MinScale || scale > definition.MaxScale)
                        continue;

                    for (int mirror = 0; mirror < (_library.Mirrors(definition) ? 2 : 1); mirror++)
                    {
                        float stampAxis = mirror == 1 ? -definition.WaterAxis : definition.WaterAxis;

                        for (int flip = 0; flip < 2; flip++)
                        {
                            for (int jitter = -1; jitter <= 1; jitter++)
                            {
                                float rotation = axis - stampAxis + flip * 180f + jitter * JITTER;
                                var placement = WaterStampPlacement.Around(index, definition, mirror == 1, scale, rotation, definition.WaterCentroid, centroid);
                                _layout.Candidates++;

                                float score = Score(id, basin, placement, area, out string reason);

                                if (score <= 0f)
                                {
                                    _layout.Reject(_settings.RecordRejections, centroid, definition.Name, reason);
                                    continue;
                                }

                                int variant = ((size * 2 + mirror) * 2 + flip) * 3 + jitter + 1;
                                score *= definition.Weight * affinity * (0.9f + 0.2f * WaterStampLibrary.Hash01(_config.Seed, id, index, variant));

                                if (score <= best.Score)
                                    continue;

                                best.Score = score;
                                best.Placement = placement;
                            }
                        }
                    }
                }
            }
        }

        return best.Score > 0f;
    }

    private static float Affinity(WaterStampDefinition definition, WaterKind kind, float aspect, bool high)
    {
        bool pond = definition.Kind == WaterStampKind.Pond;
        float match = pond == (kind == WaterKind.Pond) ? 1f : OTHER_KIND;
        float stampAspect = definition.WaterMajor / Mathf.Max(1e-3f, definition.WaterMinor);
        float shape = Mathf.Exp(-2f * Mathf.Abs(Mathf.Log(stampAspect / Mathf.Max(1f, aspect))));

        if (definition.Category == WaterStampCategory.MountainLake)
            match *= high ? 2f : 0.5f;

        return match * (0.25f + shape);
    }

    private void Moments(List<int> cells, out Vector2 centroid, out float axis, out float major, out float minor)
    {
        double sumX = 0, sumZ = 0;

        foreach (int cell in cells)
        {
            Vector2 center = _water.CellCenter(cell);
            sumX += center.x;
            sumZ += center.y;
        }

        centroid = new Vector2((float)(sumX / cells.Count), (float)(sumZ / cells.Count));
        double xx = 0, zz = 0, xz = 0;

        foreach (int cell in cells)
        {
            Vector2 offset = _water.CellCenter(cell) - centroid;
            xx += offset.x * offset.x;
            zz += offset.y * offset.y;
            xz += offset.x * offset.y;
        }

        xx /= cells.Count;
        zz /= cells.Count;
        xz /= cells.Count;

        double half = 0.5 * (xx + zz);
        double root = Math.Sqrt(0.25 * (xx - zz) * (xx - zz) + xz * xz);

        axis = (float)(0.5 * Math.Atan2(2.0 * xz, xx - zz)) * Mathf.Rad2Deg;
        major = (float)Math.Sqrt(half + root);
        minor = (float)Math.Sqrt(Math.Max(1e-6, half - root));
    }

    private float Score(int id, HydrologyBasin basin, WaterStampPlacement placement, float area, out string reason)
    {
        reason = null;
        WaterStampShape shape = _library.Shape(placement.StampIndex);
        float surface = _water.Bodies[id].Surface;

        CellRange(placement, 0, out int minX, out int maxX, out int minZ, out int maxZ);

        if (minX > maxX || minZ > maxZ)
        {
            reason = "outside the world";
            return 0f;
        }

        int stride = Mathf.Max(1, Mathf.RoundToInt(SCORE_STEP / _grid.CellSize));
        float sample = stride * _grid.CellSize;
        float cellArea = sample * sample;
        int stamp = 0, covered = 0, clipped = 0;
        float fill = 0f;

        for (int row = minZ; row <= maxZ; row += stride)
        {
            for (int column = minX; column <= maxX; column += stride)
            {
                int cell = row * _grid.Resolution + column;
                Vector2 uv = placement.Uv(placement.Stamp(_water.CellCenter(cell)));
                float mask = shape.Sample(uv.x, uv.y);
                bool flooded = _water.BodyIds[cell] == id;

                if (mask >= WaterStampTracer.LAKE_LEVEL)
                {
                    stamp++;

                    if (flooded)
                        covered++;
                    else if (!Containable(cell, id, surface) || _grid.Height[cell] - surface > _settings.MaxLakeEarthworks)
                        clipped++;

                    continue;
                }

                if (flooded && mask > 0f)
                    fill = Mathf.Max(fill, surface + DRY_LIFT - _grid.Height[cell]);
            }
        }

        if (stamp == 0)
        {
            reason = "no water inside the world";
            return 0f;
        }

        float coveredArea = covered * cellArea;
        float stampArea = stamp * cellArea;
        float coverage = coveredArea / area;
        float clippedShare = clipped / (float)stamp;

        if (coverage < _settings.MinLakeCoverage)
        {
            reason = $"covers {coverage:P0} of the basin";
            return 0f;
        }

        if (clippedShare > MAX_CLIPPED)
        {
            reason = $"{clippedShare:P0} of the outline cannot hold water";
            return 0f;
        }

        if (fill > _settings.MaxLakeEarthworks)
        {
            reason = $"needs {fill:0.0} m of fill";
            return 0f;
        }

        return coveredArea / (area + stampArea - coveredArea) * (1f - clippedShare);
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

    private void CellRange(WaterStampPlacement placement, int pad, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        placement.Bounds(out Vector2 min, out Vector2 max);
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

    private void CellRange(Vector2 min, Vector2 max, int pad, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        int last = _grid.Resolution - 1;

        minX = Mathf.Max(0, Mathf.FloorToInt(min.x / _grid.CellSize) - pad);
        maxX = Mathf.Min(last, Mathf.CeilToInt(max.x / _grid.CellSize) + pad);
        minZ = Mathf.Max(0, Mathf.FloorToInt(min.y / _grid.CellSize) - pad);
        maxZ = Mathf.Min(last, Mathf.CeilToInt(max.y / _grid.CellSize) + pad);
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

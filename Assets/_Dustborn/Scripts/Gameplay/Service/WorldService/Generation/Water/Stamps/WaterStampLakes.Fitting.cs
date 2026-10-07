using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WaterStampLakes
{
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
        int stamp = 0, covered = 0, clipped = 0, crowded = 0;
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

                    if (Crowds(cell, id))
                        crowded++;

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

        if (crowded > 0)
        {
            reason = $"reaches within {_config.Water.MinShoreGap:0} m of other water";
            return 0f;
        }

        return coveredArea / (area + stampArea - coveredArea) * (1f - clippedShare);
    }
}

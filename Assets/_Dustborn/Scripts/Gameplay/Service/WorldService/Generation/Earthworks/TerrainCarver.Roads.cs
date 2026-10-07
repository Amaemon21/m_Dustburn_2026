using UnityEngine;

public partial class TerrainCarver
{
    private void CarveRoad(Road road)
    {
        if (road?.Points == null || road.Points.Length < 2)
            return;

        RoadKindProfile kind = RoadKindProfile.For(_config, road.Kind);
        float halfWidth = kind.HalfWidth;
        float shoulder = kind.Shoulder;
        float maxFill = kind.MaxFill;
        float maxCut = kind.MaxCut;
        int smoothing = kind.ProfileSmoothing;

        Vector2[] points = Densify(road.Points);
        float[] distance = Cumulative(points);

        float[] original = SampleGround(points);
        float[] surface = SampleSurface(points);
        float[] profile = BuildProfile(original, maxFill, maxCut, smoothing);
        bool[] anchored = LevelToExistingRoads(points, surface, profile);

        RoadProfile.Fit(profile, original, distance, kind.MaxGrade * GRADE_MARGIN / _source.MaxHeight,
            maxFill / _source.MaxHeight, maxCut / _source.MaxHeight, anchored, RoadProfile.EARTHWORK_OVERRUN);

        MeetExistingRoads(points, profile, surface, anchored, kind.MaxGrade, halfWidth + shoulder);

        bool[] bridged = RaiseAboveWater(points, distance, profile, original, halfWidth, shoulder, kind.MaxGrade * GRADE_MARGIN);
        RecordBridges(road, points, profile, bridged, halfWidth);

        Profiles?.Add((road, points, profile, anchored));

        float reach = halfWidth + shoulder * 0.5f;
        var skirts = new float[points.Length];
        var fills = new float[points.Length];
        var cuts = new float[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            float moved = LateralEarthworks(points, i, profile[i], reach);

            skirts[i] = shoulder + moved * _config.RoadEmbankmentSlope;
            fills[i] = Mathf.Max(maxFill, (profile[i] - original[i]) * _source.MaxHeight + OVERRUN_MARGIN);
            cuts[i] = Mathf.Max(maxCut, (original[i] - profile[i]) * _source.MaxHeight + OVERRUN_MARGIN);
        }

        StampRoad(points, distance, profile, skirts, halfWidth, reach, fills, cuts, bridged);
    }

    private void MeetExistingRoads(Vector2[] points, float[] profile, float[] surface, bool[] anchored, float maxGrade, float footprint)
    {
        int count = points.Length;

        for (int start = 0; start < count; start++)
        {
            if (!anchored[start])
                continue;

            int end = start;

            while (end + 1 < count && anchored[end + 1])
                end++;

            int worst = start;

            for (int i = start; i <= end; i++)
            {
                if (Mathf.Abs(surface[i] - profile[i]) > Mathf.Abs(surface[worst] - profile[worst]))
                    worst = i;
            }

            float step = (profile[worst] - surface[worst]) * _source.MaxHeight;

            if (Mathf.Abs(step) > JUNCTION_STEP)
                Reshape(points[worst], step, ReshapeRadius(points[worst], step, Mathf.Min(maxGrade, StrictestGrade), footprint));

            start = end;
        }
    }

    private float ReshapeRadius(Vector2 center, float step, float maxGrade, float footprint)
    {
        float radius = footprint + RESHAPE_PROBE;

        for (int pass = 0; pass < RESHAPE_PASSES; pass++)
        {
            float budget = Mathf.Max(maxGrade * MIN_BUDGET_SHARE, maxGrade * RESHAPE_GRADE_SHARE - PassGrade(center, radius));
            radius = Mathf.Min(MAX_RESHAPE_RADIUS, SMOOTHSTEP_PEAK * Mathf.Abs(step) / budget + footprint);
        }

        return radius;
    }

    private float PassGrade(Vector2 center, float radius)
    {
        float steepest = 0f;

        for (int k = 0; k < RESHAPE_PROBES; k++)
        {
            float angle = k * Mathf.PI * 2f / RESHAPE_PROBES;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            for (float distance = RESHAPE_PROBE; distance <= radius; distance += RESHAPE_PROBE)
            {
                if (!PassTarget(center + direction * (distance - RESHAPE_PROBE), out float inner) || !PassTarget(center + direction * distance, out float outer))
                    continue;

                steepest = Mathf.Max(steepest, Mathf.Abs(outer - inner) * _source.MaxHeight / RESHAPE_PROBE);
            }
        }

        return steepest;
    }

    private bool PassTarget(Vector2 point, out float target)
    {
        int resolution = _source.Resolution;
        int x = Mathf.RoundToInt(point.x / _cellSize), y = Mathf.RoundToInt(point.y / _cellSize);
        target = 0f;

        if (x < 0 || y < 0 || x >= resolution || y >= resolution || _carveWeight[y * resolution + x] <= 0f)
            return false;

        target = _carveTarget[y * resolution + x];
        return true;
    }

    private void Reshape(Vector2 center, float step, float radius)
    {
        int resolution = _source.Resolution;
        float shift = step / _source.MaxHeight;
        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - radius) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.x + radius) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - radius) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.y + radius) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cell = y * resolution + x;

                if (_carveWeight[cell] <= 0f)
                    continue;

                float distance = Vector2.Distance(new Vector2(x * _cellSize, y * _cellSize), center);

                if (distance >= radius)
                    continue;

                _carveTarget[cell] += shift * Mathf.SmoothStep(0f, 1f, 1f - distance / radius);
            }
        }
    }

    private bool[] LevelToExistingRoads(Vector2[] points, float[] ground, float[] profile)
    {
        var anchored = new bool[points.Length];

        if (_roadMask == null)
            return anchored;

        for (int i = 0; i < points.Length; i++)
        {
            float mask = SampleMask(points[i]);

            if (mask <= 0f)
                continue;

            profile[i] = Mathf.Lerp(profile[i], ground[i], mask);
            anchored[i] = mask >= ANCHOR_MASK;
        }

        return anchored;
    }
}

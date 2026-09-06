using System.Collections.Generic;
using UnityEngine;

public class TerrainCarver
{
    private readonly WorldGenerationConfig _config;

    private float[] _carveWeight;
    private float[] _carveTarget;
    private float[] _roadMask;
    private float[] _padDistance;

    private HeightMap _source;
    private float _cellSize;

    public TerrainCarver(WorldGenerationConfig config)
    {
        _config = config;
    }

    public HeightMap Carve(HeightMap source, RoadNetwork network, out float[] roadMask)
    {
        Begin(source);

        foreach (Hub hub in network.Hubs)
            CarveHub(hub);

        HeightMap pads = Apply();

        Begin(pads);

        _roadMask = new float[pads.Heights.Length];

        foreach (Road road in network.Roads)
            CarveRoad(road, _config.RoadHalfWidth, _config.RoadShoulder, _config.MaxRoadFill, _config.MaxRoadCut, _config.RoadProfileSmoothing);

        roadMask = _roadMask;

        return Apply();
    }

    public HeightMap CarveStreets(HeightMap source, IReadOnlyList<CityLayout> layouts, float[] roadMask)
    {
        Begin(source);

        _roadMask = roadMask;

        foreach (CityLayout layout in layouts)
        {
            foreach (Road street in layout.Streets)
                CarveRoad(street, _config.StreetHalfWidth, _config.StreetShoulder, _config.MaxStreetFill, _config.MaxStreetCut, _config.StreetProfileSmoothing);
        }

        return Apply();
    }

    public HeightMap CarvePads(HeightMap source, IReadOnlyList<PoiPlacement> placements)
    {
        Begin(source);

        _padDistance = new float[source.Heights.Length];

        for (int i = 0; i < _padDistance.Length; i++)
            _padDistance[i] = float.MaxValue;

        foreach (PoiPlacement placement in placements)
            CarvePad(placement);

        return Apply();
    }

    private void Begin(HeightMap source)
    {
        _source = source;
        _cellSize = (float)source.WorldSize / (source.Resolution - 1);

        int cellCount = source.Heights.Length;

        _carveWeight = new float[cellCount];
        _carveTarget = new float[cellCount];
        _roadMask = null;
    }

    private HeightMap Apply()
    {
        var result = new HeightMap(_source.Resolution, _source.WorldSize, _source.MaxHeight);

        for (int i = 0; i < result.Heights.Length; i++)
            result.Heights[i] = Mathf.Lerp(_source.Heights[i], _carveTarget[i], _carveWeight[i]);

        return result;
    }

    private void CarveRoad(Road road, float halfWidth, float shoulder, float maxFill, float maxCut, int smoothing)
    {
        Vector2[] points = Densify(road.Points);

        float[] ground = SampleGround(points);
        float[] profile = BuildProfile(ground, maxFill, maxCut, smoothing);

        LevelToExistingRoads(points, ground, profile);

        float reach = halfWidth + shoulder * 0.5f;

        for (int i = 0; i < points.Length; i++)
        {
            float moved = LateralEarthworks(points, i, profile[i], reach);
            float skirt = shoulder + moved * _config.RoadEmbankmentSlope;

            Stamp(points[i], profile[i], halfWidth, skirt, shoulder, maxFill, maxCut);
        }
    }

    private void LevelToExistingRoads(Vector2[] points, float[] ground, float[] profile)
    {
        if (_roadMask == null)
            return;

        for (int i = 0; i < points.Length; i++)
        {
            float mask = SampleMask(points[i]);

            if (mask <= 0f)
                continue;

            profile[i] = Mathf.Lerp(profile[i], ground[i], mask);
        }
    }

    private float LateralEarthworks(Vector2[] points, int index, float profile, float reach)
    {
        Vector2 direction = Direction(points, index);
        Vector2 right = new(direction.y, -direction.x);
        Vector2 point = points[index];

        float moved = Mathf.Abs(profile - SampleNormalized(point.x, point.y));

        moved = Mathf.Max(moved, Mathf.Abs(profile - SampleNormalized(point.x + right.x * reach, point.y + right.y * reach)));
        moved = Mathf.Max(moved, Mathf.Abs(profile - SampleNormalized(point.x - right.x * reach, point.y - right.y * reach)));

        return moved * _source.MaxHeight;
    }

    private static Vector2 Direction(Vector2[] points, int index)
    {
        int from = Mathf.Max(0, index - 1);
        int to = Mathf.Min(points.Length - 1, index + 1);

        Vector2 delta = points[to] - points[from];

        return delta.sqrMagnitude > 1e-6f ? delta.normalized : new Vector2(0f, 1f);
    }

    private float SampleMask(Vector2 point)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(point.x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(point.y / _cellSize), 0, resolution - 1);

        return _roadMask[cellY * resolution + cellX];
    }

    private void CarveHub(Hub hub)
    {
        float height = SampleNormalized(hub.Position.x, hub.Position.y);

        Stamp(hub.Position, height, hub.Radius * _config.HubPadFraction, hub.Radius * (1f - _config.HubPadFraction), 0f, _config.MaxHubFill, _config.MaxHubCut);
    }

    private void CarvePad(PoiPlacement placement)
    {
        Vector2 forward = placement.Forward;
        Vector2 right = new(forward.y, -forward.x);
        Vector2 center = placement.Ground;

        float street = SampleNormalized(center.x + forward.x * placement.Footprint.y * 0.5f, center.y + forward.y * placement.Footprint.y * 0.5f);

        float halfWidth = placement.Footprint.x * 0.5f;
        float halfDepth = placement.Footprint.y * 0.5f;
        float margin = Mathf.Max(_config.PoiPadMargin, _cellSize);
        float skirt = _config.PoiPadSkirt;
        float reach = Mathf.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth) + margin + skirt;

        float maxFill = _config.MaxPoiFill / _source.MaxHeight;
        float maxCut = _config.MaxPoiCut / _source.MaxHeight;

        int resolution = _source.Resolution;

        int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.x + reach) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - reach) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((center.y + reach) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float deltaX = x * _cellSize - center.x;
                float deltaY = y * _cellSize - center.y;

                float alongRight = Mathf.Abs(deltaX * right.x + deltaY * right.y) - halfWidth;
                float alongForward = Mathf.Abs(deltaX * forward.x + deltaY * forward.y) - halfDepth;

                float outsideX = Mathf.Max(alongRight, 0f);
                float outsideY = Mathf.Max(alongForward, 0f);

                float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                    + Mathf.Min(Mathf.Max(alongRight, alongForward), 0f);

                if (distance > margin + skirt)
                    continue;

                int index = y * resolution + x;

                if (distance >= _padDistance[index])
                    continue;

                float ground = _source.Heights[index];

                _padDistance[index] = distance;
                _carveWeight[index] = distance <= margin ? 1f : Mathf.SmoothStep(0f, 1f, 1f - (distance - margin) / skirt);
                _carveTarget[index] = Mathf.Clamp(street, ground - maxCut, ground + maxFill);
            }
        }
    }

    private Vector2[] Densify(Vector2[] points)
    {
        var result = new List<Vector2>();

        for (int i = 0; i < points.Length - 1; i++)
        {
            float length = Vector2.Distance(points[i], points[i + 1]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (_cellSize * 0.5f)));

            for (int step = 0; step < steps; step++)
                result.Add(Vector2.Lerp(points[i], points[i + 1], step / (float)steps));
        }

        result.Add(points[^1]);

        return result.ToArray();
    }

    private float[] SampleGround(Vector2[] points)
    {
        var ground = new float[points.Length];

        for (int i = 0; i < ground.Length; i++)
            ground[i] = SampleNormalized(points[i].x, points[i].y);

        return ground;
    }

    private float[] BuildProfile(float[] ground, float maxFill, float maxCut, int smoothing)
    {
        float[] profile = MovingAverage(ground, smoothing);

        ClampEarthworks(profile, ground, maxFill, maxCut);

        profile = MovingAverage(profile, Mathf.Max(1, smoothing / 3));

        ClampEarthworks(profile, ground, maxFill, maxCut);

        return profile;
    }

    private void ClampEarthworks(float[] profile, float[] ground, float maxFillMeters, float maxCutMeters)
    {
        float maxFill = maxFillMeters / _source.MaxHeight;
        float maxCut = maxCutMeters / _source.MaxHeight;

        for (int i = 0; i < profile.Length; i++)
            profile[i] = Mathf.Clamp(profile[i], ground[i] - maxCut, ground[i] + maxFill);
    }

    private static float[] MovingAverage(float[] values, int window)
    {
        var result = new float[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int samples = 0;

            for (int offset = -window; offset <= window; offset++)
            {
                sum += values[Mathf.Clamp(i + offset, 0, values.Length - 1)];
                samples++;
            }

            result[i] = sum / samples;
        }

        return result;
    }

    private void Stamp(Vector2 point, float normalizedHeight, float inner, float outer, float surfaceOuter, float maxFillMeters, float maxCutMeters)
    {
        float radius = inner + outer;
        int resolution = _source.Resolution;

        float maxFill = maxFillMeters / _source.MaxHeight;
        float maxCut = maxCutMeters / _source.MaxHeight;

        int minX = Mathf.Max(0, Mathf.FloorToInt((point.x - radius) / _cellSize));
        int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt((point.x + radius) / _cellSize));
        int minY = Mathf.Max(0, Mathf.FloorToInt((point.y - radius) / _cellSize));
        int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt((point.y + radius) / _cellSize));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float deltaX = x * _cellSize - point.x;
                float deltaY = y * _cellSize - point.y;
                float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

                if (distance > radius)
                    continue;

                float weight = distance <= inner ? 1f : 1f - (distance - inner) / outer;
                weight = Mathf.SmoothStep(0f, 1f, weight);

                int index = y * resolution + x;

                if (weight > _carveWeight[index])
                {
                    float ground = _source.Heights[index];

                    _carveWeight[index] = weight;
                    _carveTarget[index] = Mathf.Clamp(normalizedHeight, ground - maxCut, ground + maxFill);
                }

                if (surfaceOuter <= 0f || _roadMask == null)
                    continue;

                float surfaceFalloff = surfaceOuter * _config.RoadSurfaceFraction;
                float surface = distance <= inner ? 1f : 1f - (distance - inner) / surfaceFalloff;

                if (surface > _roadMask[index])
                    _roadMask[index] = Mathf.Clamp01(surface);
            }
        }
    }

    private float SampleNormalized(float x, float y)
    {
        int resolution = _source.Resolution;

        int cellX = Mathf.Clamp(Mathf.RoundToInt(x / _cellSize), 0, resolution - 1);
        int cellY = Mathf.Clamp(Mathf.RoundToInt(y / _cellSize), 0, resolution - 1);

        return _source.Get(cellX, cellY);
    }
}

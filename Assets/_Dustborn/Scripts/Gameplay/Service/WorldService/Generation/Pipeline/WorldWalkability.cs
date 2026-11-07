using System;
using UnityEngine;

internal readonly struct WorldWalkability
{
    private const float WALK_STEP = 2f;
    private readonly Func<float, float, float> _ground;
    private readonly WaterMap _water;
    private readonly PoiSpatialIndex _buildings;
    private readonly float _seaLevel;
    private readonly float _shoreMargin;
    private readonly float _maxGrade;

    public WorldWalkability(WorldGenerationConfig config, Func<float, float, float> ground, WaterMap water, PoiSpatialIndex buildings)
    {
        _ground = ground;
        _water = water;
        _buildings = buildings;
        _seaLevel = config.SeaLevel;
        _shoreMargin = config.ShoreMargin;
        _maxGrade = Mathf.Tan((config.Gameplay?.MaxWalkSlope ?? 25f) * Mathf.Deg2Rad);
    }

    public bool CanWalk(Vector2 from, Vector2 to, int ignore = -1)
    {
        float distance = (to - from).magnitude;
        int steps = Math.Max(1, Mathf.CeilToInt(distance / WALK_STEP));
        float maxRise = _maxGrade * distance / steps + 0.05f;
        float previous = _ground(from.x, from.y);
        if (!Dry(from, previous) || _buildings.Occupied(from, 1f, ignore))
            return false;

        for (int i = 1; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
            float height = _ground(point.x, point.y);
            if (Mathf.Abs(height - previous) > maxRise || !Dry(point, height) || _buildings.Occupied(point, 1f, ignore))
                return false;
            previous = height;
        }

        return true;
    }

    public bool Dry(Vector2 point, float height)
    {
        return !float.IsNaN(height) && !float.IsInfinity(height) && (_seaLevel <= 0f || height >= _seaLevel + _shoreMargin)
            && (_water == null || !_water.IsWet(point.x, point.y, height, _shoreMargin));
    }

    public static Vector2 Approach(PoiPlacement poi) => poi.Ground + poi.Forward * (poi.Footprint.y * 0.5f + 2f);
}

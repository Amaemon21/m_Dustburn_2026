using System.Collections.Generic;
using UnityEngine;

public sealed partial class PropPlacer
{
    private const float VERGE_GAP = 0.8f;
    private const float VERGE_SPREAD = 2.5f;
    private const float FLIP_CHANCE = 0.5f;
    private const float ROAD_CLEARANCE = 0.4f;
    private const float CAR_HALF_WIDTH_SHARE = 0.4f;

    private void PlaceAlongRoads(IReadOnlyList<Road> roads, PropContext context)
    {
        if (_byContext[context].Definitions.Count == 0)
            return;

        foreach (Road road in roads)
        {
            if (road?.Points == null || road.Points.Length < 2)
                continue;

            float per100m = PerHundredMetres(road.Kind);
            if (per100m <= 0f)
                continue;

            float spacing = 100f / per100m;
            float next = spacing * (0.5f + _random.NextFloat());
            float travelled = 0f;

            for (int i = 1; i < road.Points.Length; i++)
            {
                Vector2 from = road.Points[i - 1];
                Vector2 to = road.Points[i];
                float length = Vector2.Distance(from, to);

                if (length <= 0f)
                    continue;

                Vector2 tangent = (to - from) / length;

                while (next <= travelled + length)
                {
                    Vector2 point = Vector2.Lerp(from, to, (next - travelled) / length);
                    PlaceBesideRoad(road, context, point, tangent);
                    next += spacing * (0.5f + _random.NextFloat());
                }

                travelled += length;
            }
        }
    }

    private void PlaceBesideRoad(Road road, PropContext context, Vector2 point, Vector2 tangent)
    {
        if (!TryPick(context, out PropDefinition definition))
            return;

        Vector2 normal = new(-tangent.y, tangent.x);
        float side = _random.NextFloat() < 0.5f ? -1f : 1f;
        float lateral;

        if (definition.OnRoad)
        {
            float lane = Mathf.Max(0f, road.HalfWidth - definition.Radius * CAR_HALF_WIDTH_SHARE);
            lateral = context == PropContext.Street ? side * lane : (_random.NextFloat() * 2f - 1f) * lane;
        }
        else
        {
            float shoulder = RoadKindProfile.For(_config, road.Kind).Shoulder;
            lateral = side * (road.HalfWidth + VERGE_GAP + definition.Radius + _random.NextFloat() * Mathf.Min(VERGE_SPREAD, shoulder));
        }

        Vector2 position = point + normal * lateral;
        float yaw = definition.AlongRoad ? YawOf(tangent) + (_random.NextFloat() < FLIP_CHANCE ? 180f : 0f) : _random.NextFloat() * 360f;
        yaw += Jitter(definition.YawJitter);

        if (!TryAccept(definition, position, !definition.OnRoad, ROAD_CLEARANCE, out float ground))
            return;

        Commit(definition, context, position, ground, yaw);
        PlaceCluster(definition, context, position, yaw, !definition.OnRoad, ROAD_CLEARANCE);
    }

    private float PerHundredMetres(RoadKind kind) => kind switch
    {
        RoadKind.Highway => _database.HighwayPer100m,
        RoadKind.DirtAccess => _database.DirtPer100m,
        _ => _database.StreetPer100m
    };
}

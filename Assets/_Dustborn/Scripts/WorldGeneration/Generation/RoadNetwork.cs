using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Hub
{
    [field: SerializeField] public Vector2 Position { get; private set; }
    [field: SerializeField] public float Radius { get; private set; }
    [field: SerializeField] public float Relief { get; private set; }
    [field: SerializeField] public SettlementTier Tier { get; private set; }

    public Hub(Vector2 position, float radius, float relief, SettlementTier tier = SettlementTier.City)
    {
        Position = position;
        Radius = radius;
        Relief = relief;
        Tier = tier;
    }
}

[Serializable]
public class Road
{
    [field: SerializeField] public Vector2[] Points { get; private set; }
    [field: SerializeField] public float Width { get; private set; }

    public Road(Vector2[] points, float width)
    {
        Points = points;
        Width = width;
    }
}

public class RoadNetwork
{
    public List<Hub> Hubs { get; } = new();
    public List<Road> Roads { get; } = new();
    public List<Road> Streets { get; } = new();
}

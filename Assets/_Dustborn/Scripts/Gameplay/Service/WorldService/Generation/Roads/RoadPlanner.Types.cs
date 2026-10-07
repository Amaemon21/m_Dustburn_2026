using System.Collections.Generic;
using UnityEngine;

public partial class RoadPlanner
{
    public enum PieceShape
    {
        Routed,
        Facing,
        Straight,
        Direct
    }

    public readonly struct RouteRecord
    {
        public int Route { get; }
        public PieceShape Shape { get; }
        public bool FromGateway { get; }
        public bool ToGateway { get; }
        public int Level { get; }
        public bool Relaxed { get; }
        public int Hard { get; }

        public RouteRecord(int route, PieceShape shape, bool fromGateway, bool toGateway, int level, bool relaxed, int hard)
        {
            Route = route;
            Shape = shape;
            FromGateway = fromGateway;
            ToGateway = toGateway;
            Level = level;
            Relaxed = relaxed;
            Hard = hard;
        }
    }

    private sealed class Endpoint
    {
        public bool Direct;
        public SettlementGateway Gateway;
        public int Edge = -1;
        public float Along;
        public float Cost;
        public Vector2 Point;
        public Vector2 Tangent;
    }

    private sealed class Route
    {
        public Vector2[] Points;
        public Endpoint Start;
        public Endpoint End;
        public float Cost;
        public int Hard;
        public int Shallow;
        public int Intrusions;
        public int Parallel;
        public int Steep;
        public int Tilted;
        public int Tight;
        public int Self;
        public int Flooded;
        public int Oblique;
        public float Water;
        public PieceShape Shape;
        public int Id = -1;
        public readonly List<(float Along, int Edge, float EdgeAlong, Vector2 Point)> Crossings = new();
        public readonly List<Vector2> Problems = new();
    }

    private sealed class Journey
    {
        public readonly List<Route> Pieces = new();
        public readonly List<Vector2> Problems = new();
        public float Cost;
        public int Hard;
        public int Self;
        public int Rides;
        public float Water;
        public int Level;
        public bool Relaxed;
    }

    private struct LinkPlan
    {
        public SettlementGateway Start;
        public SettlementGateway End;
        public Journey Journey;
        public bool Loop;
        public float Existing;
    }
}

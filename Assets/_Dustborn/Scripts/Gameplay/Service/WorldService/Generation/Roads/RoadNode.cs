using System.Collections.Generic;
using UnityEngine;

public sealed class RoadNode
{
    public int Id { get; }
    public Vector2 Position { get; }
    public RoadNodeKind Kind { get; }
    public int Settlement { get; }
    public int Gateway { get; }
    public List<int> Edges { get; } = new();

    public RoadNode(int id, Vector2 position, RoadNodeKind kind, int settlement, int gateway)
    {
        Id = id;
        Position = position;
        Kind = kind;
        Settlement = settlement;
        Gateway = gateway;
    }
}

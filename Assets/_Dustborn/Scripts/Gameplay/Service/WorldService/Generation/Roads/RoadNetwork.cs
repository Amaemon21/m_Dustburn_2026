using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Hub
{
    [field: SerializeField] public Vector2 Position { get; private set; }
    [field: SerializeField] public float Radius { get; private set; }
    [field: SerializeField] public float Relief { get; private set; }
    [field: SerializeField] public int Houses { get; private set; }
    [field: SerializeField] public SettlementType Type { get; private set; }

    public Hub(Vector2 position, float radius, float relief, int houses, SettlementType type)
    {
        Position = position;
        Radius = radius;
        Relief = relief;
        Houses = houses;
        Type = type;
    }

    public void SetRadius(float radius)
    {
        Radius = radius;
    }

    public void SetHouses(int houses)
    {
        Houses = houses;
    }
}

[Serializable]
public class Road
{
    [field: SerializeField] public Vector2[] Points { get; private set; }
    [field: SerializeField] public float Width { get; private set; }
    [field: SerializeField] public RoadKind Kind { get; private set; }

    public Road(Vector2[] points, float width, RoadKind kind)
    {
        Points = points;
        Width = width;
        Kind = kind;
    }

    public float HalfWidth => Width * 0.5f;
}

[Serializable]
public class RoadJunction
{
    [field: SerializeField] public Vector2 Position { get; private set; }
    [field: SerializeField] public int Degree { get; private set; }
    [field: SerializeField] public RoadNodeKind Kind { get; private set; }

    public RoadJunction(Vector2 position, int degree, RoadNodeKind kind)
    {
        Position = position;
        Degree = degree;
        Kind = kind;
    }
}

[Serializable]
public class TileRecord
{
    [field: SerializeField] public int I { get; private set; }
    [field: SerializeField] public int J { get; private set; }
    [field: SerializeField] public TilePorts Ports { get; private set; }
    [field: SerializeField] public DistrictType District { get; private set; }

    public TileRecord(int i, int j, TilePorts ports, DistrictType district)
    {
        I = i;
        J = j;
        Ports = ports;
        District = district;
    }
}

[Serializable]
public class GatewayRecord
{
    [field: SerializeField] public Vector2 Position { get; private set; }
    [field: SerializeField] public Vector2 Tangent { get; private set; }

    public GatewayRecord(Vector2 position, Vector2 tangent)
    {
        Position = position;
        Tangent = tangent;
    }
}

[Serializable]
public class SettlementRecord
{
    [field: SerializeField] public SettlementType Type { get; private set; }
    [field: SerializeField] public Vector2 Origin { get; private set; }
    [field: SerializeField] public float Angle { get; private set; }
    [field: SerializeField] public float TileSize { get; private set; }
    [field: SerializeField] public List<TileRecord> Tiles { get; private set; } = new();
    [field: SerializeField] public List<GatewayRecord> Gateways { get; private set; } = new();

    public SettlementRecord(SettlementType type, Vector2 origin, float angle, float tileSize)
    {
        Type = type;
        Origin = origin;
        Angle = angle;
        TileSize = tileSize;
    }

    public Vector2 AxisU => new(Mathf.Cos(Angle), Mathf.Sin(Angle));

    public Vector2 AxisV => new(-Mathf.Sin(Angle), Mathf.Cos(Angle));

    public Vector2 TileCenter(TileRecord tile)
    {
        return Origin + AxisU * (tile.I * TileSize) + AxisV * (tile.J * TileSize);
    }

    public static SettlementRecord From(SettlementLayout layout)
    {
        var record = new SettlementRecord(layout.Type, layout.Origin, layout.Angle, layout.TileSize);

        foreach (SettlementTile tile in layout.Tiles)
            record.Tiles.Add(new TileRecord(tile.I, tile.J, tile.Ports, tile.District));

        foreach (SettlementGateway gateway in layout.Gateways)
            record.Gateways.Add(new GatewayRecord(gateway.Port, gateway.Tangent));

        return record;
    }
}

public sealed class RegionalLink
{
    public int From { get; }
    public int To { get; }
    public bool Backbone { get; }
    public float Estimate { get; }
    public float Priority { get; }

    public RegionalLink(int from, int to, bool backbone, float estimate, float priority)
    {
        From = from;
        To = to;
        Backbone = backbone;
        Estimate = estimate;
        Priority = priority;
    }

    public int Other(int settlement)
    {
        return settlement == From ? To : From;
    }
}

public sealed class RuralSite
{
    public Vector2 Anchor { get; }
    public Vector2 Forward { get; }
    public Vector2 Tangent { get; }
    public int Edge { get; }
    public int Buildings { get; }

    public RuralSite(Vector2 anchor, Vector2 forward, int edge, int buildings)
    {
        Anchor = anchor;
        Forward = forward;
        Tangent = new Vector2(forward.y, -forward.x);
        Edge = edge;
        Buildings = buildings;
    }
}

public class RoadNetwork
{
    public List<Hub> Hubs { get; } = new();
    public List<(int From, int To)> Links { get; } = new();
    public List<RegionalLink> RegionalLinks { get; } = new();
    public List<Road> Roads { get; } = new();
    public List<Road> Streets { get; } = new();
    public List<RoadJunction> Junctions { get; } = new();
    public List<SettlementRecord> Settlements { get; } = new();
    public List<RuralSite> RuralSites { get; } = new();
    public RoadGraph Graph { get; set; }

    public List<Road> Paved()
    {
        var paved = new List<Road>(Roads.Count + Streets.Count);

        paved.AddRange(Roads);
        paved.AddRange(Streets);

        return paved;
    }

    public void SetPlan(IReadOnlyList<RegionalLink> links)
    {
        Links.Clear();
        RegionalLinks.Clear();

        foreach (RegionalLink link in links)
        {
            RegionalLinks.Add(link);
            Links.Add((link.From, link.To));
        }
    }

    public void Publish(WorldGenerationConfig config, IReadOnlyList<SettlementLayout> layouts)
    {
        Roads.Clear();
        Streets.Clear();
        Junctions.Clear();
        Settlements.Clear();

        if (Graph != null)
        {
            Roads.AddRange(Graph.BuildRoads(config));

            foreach (RoadNode node in Graph.Nodes)
            {
                int degree = Graph.Degree(node.Id);

                if (degree > 0 && (degree != 2 || node.Kind != RoadNodeKind.Junction))
                    Junctions.Add(new RoadJunction(node.Position, degree, node.Kind));
            }
        }

        if (layouts == null)
            return;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            Streets.AddRange(layout.Streets);
            Settlements.Add(SettlementRecord.From(layout));
        }
    }
}

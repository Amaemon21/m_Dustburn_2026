using System.Collections.Generic;
using UnityEngine;

public class Lot
{
    public Vector2 Center { get; }
    public float Width { get; }
    public float Depth { get; }
    public Vector2 Forward { get; }
    public DistrictType District { get; }

    public Lot(Vector2 center, float width, float depth, Vector2 forward, DistrictType district)
    {
        Center = center;
        Width = width;
        Depth = depth;
        Forward = forward;
        District = district;
    }

    public Vector2 Right => new(Forward.y, -Forward.x);

    public Vector2 FrontEdge => Center + Forward * (Depth * 0.5f);

    public bool Overlaps(Lot other, float shrink)
    {
        Vector2 half = new(Width * 0.5f * shrink, Depth * 0.5f * shrink);
        Vector2 otherHalf = new(other.Width * 0.5f * shrink, other.Depth * 0.5f * shrink);

        return !Separated(this, half, other, otherHalf) && !Separated(other, otherHalf, this, half);
    }

    private static bool Separated(Lot lot, Vector2 half, Lot other, Vector2 otherHalf)
    {
        Vector2 delta = other.Center - lot.Center;

        return SeparatedAlong(lot.Right, delta, half.x, other, otherHalf)
            || SeparatedAlong(lot.Forward, delta, half.y, other, otherHalf);
    }

    private static bool SeparatedAlong(Vector2 axis, Vector2 delta, float extent, Lot other, Vector2 otherHalf)
    {
        float reach = Mathf.Abs(Vector2.Dot(other.Right, axis)) * otherHalf.x
            + Mathf.Abs(Vector2.Dot(other.Forward, axis)) * otherHalf.y;

        return Mathf.Abs(Vector2.Dot(delta, axis)) > extent + reach;
    }
}

public sealed class SettlementTile
{
    public int I { get; }
    public int J { get; }
    public Vector2 Center { get; }
    public Vector2[] Corners { get; }
    public TilePorts Ports { get; set; }
    public TilePorts ArterialPorts { get; set; }
    public TilePorts GatewayPorts { get; set; }
    public DistrictType District { get; set; } = DistrictType.Residential;
    public int Hops { get; set; }

    public SettlementTile(int i, int j, Vector2 center, Vector2[] corners)
    {
        I = i;
        J = j;
        Center = center;
        Corners = corners;
    }

    public TileShape Shape => TilePortRules.ShapeOf(Ports);

    public bool IsGateway => GatewayPorts != TilePorts.None;

    public bool IsArterial => ArterialPorts != TilePorts.None;
}

public sealed class SettlementGateway
{
    public int Index { get; }
    public SettlementTile Tile { get; }
    public TilePorts Side { get; }
    public Vector2 Port { get; }
    public Vector2 Tangent { get; }
    public Vector2 Approach { get; }
    public List<int> Neighbours { get; } = new();
    public int Node { get; set; } = -1;

    public SettlementGateway(int index, SettlementTile tile, TilePorts side, Vector2 port, Vector2 tangent, float approach)
    {
        Index = index;
        Tile = tile;
        Side = side;
        Port = port;
        Tangent = tangent;
        Approach = port + tangent * approach;
    }
}

public class Frontage
{
    public Vector2 From { get; }
    public Vector2 To { get; }
    public Vector2 Normal { get; }
    public float HalfWidth { get; }
    public DistrictType District { get; }
    public float MaxDepth { get; }
    public float Density { get; }
    public RoadKind Kind { get; }

    public Frontage(Vector2 from, Vector2 to, Vector2 normal, float halfWidth, DistrictType district, float maxDepth, float density, RoadKind kind)
    {
        From = from;
        To = to;
        Normal = normal;
        HalfWidth = halfWidth;
        District = district;
        MaxDepth = maxDepth;
        Density = density;
        Kind = kind;
    }

    public float Length => Vector2.Distance(From, To);
}

public class SettlementLayout
{
    private readonly Dictionary<long, SettlementTile> _lookup = new();

    public Hub Hub { get; }
    public int Index { get; }
    public float Angle { get; }
    public float TileSize { get; }
    public Vector2 Origin { get; }
    public Vector2 AxisU { get; }
    public Vector2 AxisV { get; }

    public List<SettlementTile> Tiles { get; } = new();
    public List<SettlementGateway> Gateways { get; } = new();
    public List<Road> Streets { get; } = new();
    public List<Vector2> StreetNodes { get; } = new();
    public List<Frontage> Frontages { get; } = new();
    public List<Lot> Lots { get; } = new();
    public List<int> Neighbours { get; } = new();

    public Vector2 Min { get; private set; }
    public Vector2 Max { get; private set; }
    public float Radius { get; private set; }
    public int TopologyViolations { get; set; }

    public SettlementLayout(Hub hub, int index, float angle, float tileSize)
    {
        Hub = hub;
        Index = index;
        Angle = angle;
        TileSize = tileSize;
        Origin = hub.Position;
        AxisU = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        AxisV = new Vector2(-AxisU.y, AxisU.x);
        Min = hub.Position;
        Max = hub.Position;
    }

    public SettlementType Type => Hub.Type;

    public bool IsEmpty => Tiles.Count == 0;

    public static long Key(int i, int j)
    {
        return ((long)(i + 32768) << 32) | (uint)(j + 32768);
    }

    public Vector2 TileCenter(int i, int j)
    {
        return Origin + AxisU * (i * TileSize) + AxisV * (j * TileSize);
    }

    public Vector2 LocalToWorld(float u, float v)
    {
        return Origin + AxisU * u + AxisV * v;
    }

    public void ToLocal(Vector2 point, out float u, out float v)
    {
        Vector2 delta = point - Origin;

        u = Vector2.Dot(delta, AxisU);
        v = Vector2.Dot(delta, AxisV);
    }

    public SettlementTile TileAt(int i, int j)
    {
        return _lookup.TryGetValue(Key(i, j), out SettlementTile tile) ? tile : null;
    }

    public SettlementTile AddTile(int i, int j)
    {
        SettlementTile existing = TileAt(i, j);

        if (existing != null)
            return existing;

        float half = TileSize * 0.5f;
        Vector2 center = TileCenter(i, j);

        var corners = new[]
        {
            center - AxisU * half - AxisV * half,
            center + AxisU * half - AxisV * half,
            center + AxisU * half + AxisV * half,
            center - AxisU * half + AxisV * half
        };

        var tile = new SettlementTile(i, j, center, corners);

        Tiles.Add(tile);
        _lookup[Key(i, j)] = tile;

        return tile;
    }

    public SettlementTile Neighbour(SettlementTile tile, TilePorts side)
    {
        return TileAt(tile.I + TilePortRules.StepI(side), tile.J + TilePortRules.StepJ(side));
    }

    public Vector2 PortPoint(SettlementTile tile, TilePorts side)
    {
        return tile.Center + TilePortRules.Direction(side, AxisU, AxisV) * (TileSize * 0.5f);
    }

    public bool Contains(Vector2 point)
    {
        if (IsEmpty || point.x < Min.x || point.y < Min.y || point.x > Max.x || point.y > Max.y)
            return false;

        ToLocal(point, out float u, out float v);

        return TileAt(Mathf.FloorToInt(u / TileSize + 0.5f), Mathf.FloorToInt(v / TileSize + 0.5f)) != null;
    }

    public bool IsWithin(Vector2 point, float margin)
    {
        if (IsEmpty || point.x < Min.x - margin || point.y < Min.y - margin || point.x > Max.x + margin || point.y > Max.y + margin)
            return false;

        return DistanceSqrToTiles(point, margin) < margin * margin || Contains(point);
    }

    public float DistanceSqrToTiles(Vector2 point, float limit)
    {
        ToLocal(point, out float u, out float v);

        int reach = Mathf.CeilToInt(limit / TileSize) + 1;
        int centerI = Mathf.FloorToInt(u / TileSize + 0.5f);
        int centerJ = Mathf.FloorToInt(v / TileSize + 0.5f);
        float half = TileSize * 0.5f;
        float best = limit * limit;

        for (int j = centerJ - reach; j <= centerJ + reach; j++)
        {
            for (int i = centerI - reach; i <= centerI + reach; i++)
            {
                if (TileAt(i, j) == null)
                    continue;

                float outsideU = Mathf.Max(0f, Mathf.Abs(u - i * TileSize) - half);
                float outsideV = Mathf.Max(0f, Mathf.Abs(v - j * TileSize) - half);
                float distanceSqr = outsideU * outsideU + outsideV * outsideV;

                if (distanceSqr < best)
                    best = distanceSqr;
            }
        }

        return best;
    }

    public SettlementGateway GatewayToward(int neighbour)
    {
        foreach (SettlementGateway gateway in Gateways)
        {
            if (gateway.Neighbours.Contains(neighbour))
                return gateway;
        }

        return null;
    }

    public void Measure()
    {
        if (IsEmpty)
        {
            Min = Hub.Position;
            Max = Hub.Position;
            Radius = 0f;
            return;
        }

        Vector2 min = Tiles[0].Corners[0];
        Vector2 max = min;
        float farthest = 0f;

        foreach (SettlementTile tile in Tiles)
        {
            foreach (Vector2 corner in tile.Corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
                farthest = Mathf.Max(farthest, (corner - Origin).sqrMagnitude);
            }
        }

        Min = min;
        Max = max;
        Radius = Mathf.Sqrt(farthest);
    }
}

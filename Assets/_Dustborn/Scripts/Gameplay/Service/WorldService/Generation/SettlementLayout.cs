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

public class Block
{
    public Vector2[] Corners { get; }
    public Vector2 Center { get; }
    public DistrictType District { get; private set; } = DistrictType.Residential;

    public Block(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Corners = new[] { a, b, c, d };
        Center = (a + b + c + d) * 0.25f;
    }

    public void Zone(DistrictType district)
    {
        District = district;
    }

    public bool Contains(Vector2 point)
    {
        for (int i = 0; i < Corners.Length; i++)
        {
            Vector2 from = Corners[i];
            Vector2 edge = Corners[(i + 1) % Corners.Length] - from;
            Vector2 offset = point - from;

            if (edge.x * offset.y - edge.y * offset.x < 0f)
                return false;
        }

        return true;
    }
}

public class Frontage
{
    public Vector2 From { get; }
    public Vector2 To { get; }
    public Vector2 Normal { get; }
    public float HalfWidth { get; }
    public Block Block { get; }

    public Frontage(Vector2 from, Vector2 to, Vector2 normal, float halfWidth, Block block)
    {
        From = from;
        To = to;
        Normal = normal;
        HalfWidth = halfWidth;
        Block = block;
    }
}

public class SettlementLayout
{
    public Hub Hub { get; }
    public float Angle { get; }

    public List<Block> Blocks { get; } = new();
    public List<Road> Streets { get; } = new();
    public List<Frontage> Frontages { get; } = new();
    public List<Lot> Lots { get; } = new();
    public List<int> Neighbours { get; } = new();
    public List<Vector2> Gates { get; } = new();

    public Vector2 Min { get; private set; }
    public Vector2 Max { get; private set; }
    public float Radius { get; private set; }

    public SettlementLayout(Hub hub, float angle)
    {
        Hub = hub;
        Angle = angle;
        Min = hub.Position;
        Max = hub.Position;
    }

    public bool IsEmpty => Blocks.Count == 0;

    public bool Contains(Vector2 point)
    {
        if (IsEmpty || point.x < Min.x || point.y < Min.y || point.x > Max.x || point.y > Max.y)
            return false;

        foreach (Block block in Blocks)
        {
            if (block.Contains(point))
                return true;
        }

        return false;
    }

    public Vector2 GateToward(int neighbour)
    {
        int index = Neighbours.IndexOf(neighbour);

        return index < 0 || index >= Gates.Count ? Hub.Position : Gates[index];
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

        Vector2 min = Blocks[0].Corners[0];
        Vector2 max = min;
        float farthest = 0f;

        foreach (Block block in Blocks)
        {
            foreach (Vector2 corner in block.Corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
                farthest = Mathf.Max(farthest, (corner - Hub.Position).sqrMagnitude);
            }
        }

        Min = min;
        Max = max;
        Radius = Mathf.Sqrt(farthest);
    }
}

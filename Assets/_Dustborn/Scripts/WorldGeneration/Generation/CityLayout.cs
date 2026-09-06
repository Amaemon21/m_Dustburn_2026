using System.Collections.Generic;
using UnityEngine;

public class Lot
{
    public Vector2 Center { get; }
    public float Width { get; }
    public float Depth { get; }
    public Vector2 Forward { get; }
    public DistrictType District { get; }
    public PoiDefinition Reserved { get; }

    public Lot(Vector2 center, float width, float depth, Vector2 forward, DistrictType district, PoiDefinition reserved)
    {
        Center = center;
        Width = width;
        Depth = depth;
        Forward = forward;
        District = district;
        Reserved = reserved;
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

public class CityLayout
{
    private readonly float _shapeJitter;
    private readonly float _phaseA;
    private readonly float _phaseB;
    private readonly float _industrialAngle;

    public Hub Hub { get; }
    public float Angle { get; }
    public float Radius { get; }
    public SettlementProfile Profile { get; }
    public SettlementComposition Composition { get; }

    public SettlementTier Tier => Hub.Tier;

    public List<Road> Streets { get; } = new();
    public List<Road> Frontage { get; } = new();
    public List<Lot> Lots { get; } = new();

    public CityLayout(Hub hub, float angle, float radius, SettlementProfile profile, SettlementComposition composition,
        float shapeJitter, float phaseA, float phaseB, float industrialAngle)
    {
        Hub = hub;
        Angle = angle;
        Radius = radius;
        Profile = profile;
        Composition = composition;

        _shapeJitter = shapeJitter;
        _phaseA = phaseA;
        _phaseB = phaseB;
        _industrialAngle = industrialAngle;
    }

    public bool Contains(Vector2 world)
    {
        Vector2 local = world - Hub.Position;
        float boundary = BoundaryRadius(local);

        return boundary >= 0f && local.sqrMagnitude <= boundary * boundary;
    }

    public float BoundaryRadius(Vector2 local)
    {
        float theta = Mathf.Atan2(local.y, local.x);
        float wobble = Mathf.Sin(theta * 2f + _phaseA) * 0.6f + Mathf.Sin(theta * 3f + _phaseB) * 0.4f;

        return Radius * (1f + _shapeJitter * wobble);
    }

    public float NormalizedDistance(Vector2 world)
    {
        return (world - Hub.Position).magnitude / Radius;
    }

    public DistrictType DistrictAt(Vector2 world)
    {
        Vector2 local = world - Hub.Position;
        float distance = local.magnitude / Radius;

        if (distance < Profile.DowntownFraction)
            return DistrictType.Downtown;

        if (distance < Profile.ResidentialFraction)
            return DistrictType.Residential;

        float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        float delta = Mathf.Abs(Mathf.DeltaAngle(angle, _industrialAngle * Mathf.Rad2Deg));

        return delta <= Profile.IndustrialSpan * 0.5f ? DistrictType.Industrial : Profile.OuterDistrict;
    }

    public float BlockSizeAt(Vector2 world)
    {
        float distance = Mathf.Clamp01((world - Hub.Position).magnitude / Radius);

        return Mathf.Lerp(Profile.CoreBlockSize, Profile.OuterBlockSize, distance);
    }
}

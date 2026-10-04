using System.Collections.Generic;
using UnityEngine;

public readonly struct RoadKindProfile
{
    private const float COINCIDENT = 0.05f;

    public RoadKind Kind { get; }
    public float HalfWidth { get; }
    public float Shoulder { get; }
    public float MaxFill { get; }
    public float MaxCut { get; }
    public int ProfileSmoothing { get; }
    public float MinCurveRadius { get; }
    public float MaxGrade { get; }
    public float MaxCrossSlope { get; }
    public int CarveOrder { get; }

    public RoadKindProfile(RoadKind kind, float halfWidth, float shoulder, float maxFill, float maxCut, int profileSmoothing,
        float minCurveRadius, float maxGrade, float maxCrossSlope, int carveOrder)
    {
        Kind = kind;
        HalfWidth = halfWidth;
        Shoulder = shoulder;
        MaxFill = maxFill;
        MaxCut = maxCut;
        ProfileSmoothing = profileSmoothing;
        MinCurveRadius = minCurveRadius;
        MaxGrade = maxGrade;
        MaxCrossSlope = maxCrossSlope;
        CarveOrder = carveOrder;
    }

    public float Width => HalfWidth * 2f;

    public bool IsRegional => Kind == RoadKind.Highway || Kind == RoadKind.DirtAccess;

    public static RoadKindProfile For(WorldGenerationConfig config, RoadKind kind)
    {
        return kind switch
        {
            RoadKind.Highway => new RoadKindProfile(kind, config.RoadHalfWidth, config.RoadShoulder, config.MaxRoadFill, config.MaxRoadCut,
                config.RoadProfileSmoothing, config.HighwayMinCurveRadius, config.RoadMaxGrade, config.HighwayMaxCrossSlope, 2),
            RoadKind.Arterial => new RoadKindProfile(kind, config.ArterialHalfWidth, config.ArterialShoulder, config.MaxStreetFill, config.MaxStreetCut,
                config.StreetProfileSmoothing, config.StreetCornerRadius, config.MaxStreetSlope, float.MaxValue, 0),
            RoadKind.LocalStreet => new RoadKindProfile(kind, config.StreetHalfWidth, config.StreetShoulder, config.MaxStreetFill, config.MaxStreetCut,
                config.StreetProfileSmoothing, config.StreetCornerRadius, config.MaxStreetSlope, float.MaxValue, 1),
            _ => new RoadKindProfile(kind, config.DirtHalfWidth, config.DirtShoulder, config.MaxDirtFill, config.MaxDirtCut,
                config.DirtProfileSmoothing, config.DirtMinCurveRadius, config.DirtMaxGrade, config.DirtMaxCrossSlope, 3)
        };
    }

    public static Road Create(WorldGenerationConfig config, Vector2[] points, RoadKind kind)
    {
        return new Road(Distinct(points), For(config, kind).Width, kind);
    }

    private static Vector2[] Distinct(Vector2[] points)
    {
        var kept = new List<Vector2>(points.Length);

        foreach (Vector2 point in points)
        {
            if (kept.Count > 0 && (point - kept[^1]).sqrMagnitude <= COINCIDENT * COINCIDENT)
            {
                kept[^1] = point;
                continue;
            }

            kept.Add(point);
        }

        return kept.Count == points.Length ? points : kept.ToArray();
    }

    public static float SampleSpacing(RoadKind kind)
    {
        return kind == RoadKind.Highway ? 12f : kind == RoadKind.DirtAccess ? 8f : 4f;
    }
}

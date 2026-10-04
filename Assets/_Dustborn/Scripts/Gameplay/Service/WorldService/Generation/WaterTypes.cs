using System;
using System.Collections.Generic;
using UnityEngine;

public enum WaterKind : byte
{
    None,
    Sea,
    River,
    Lake,
    Pond
}

public struct WaterSample
{
    public WaterKind Kind;
    public float Surface;
    public float Width;
    public Vector2 Flow;
    public int Body;

    public bool IsWater => Kind != WaterKind.None;
}

[Serializable]
public struct WaterBody
{
    public WaterKind Kind;
    public float Surface;
    public float Area;
    public float Depth;
    public Vector2 Center;
    public float SeedRadius;
}

[Serializable]
public struct RiverPoint
{
    public Vector2 Position;
    public float Surface;
    public float Bed;
    public float Width;
    public float Flow;
    public bool Submerged;
}

public enum RiverSource : byte
{
    Unknown,
    Headwater,
    LakeOutlet,
    Boundary,
    Distributary
}

public enum RiverTerminal : byte
{
    Unknown,
    Sea,
    Lake,
    Junction,
    Border
}

public sealed class RiverPath
{
    public readonly List<RiverPoint> Points = new();
    public RiverSource Source;
    public RiverTerminal Terminal;
    public float SourceArea;
    public float SourceDonor;
    public int Parent = -1;

    public float Length
    {
        get
        {
            float length = 0f;

            for (int i = 1; i < Points.Count; i++)
                length += Vector2.Distance(Points[i - 1].Position, Points[i].Position);

            return length;
        }
    }
}

[Serializable]
public struct WaterCrossing
{
    public Vector2 Position;
    public Vector2 Direction;
    public Vector2 Flow;
    public float RiverWidth;
    public float WaterHeight;
    public RoadKind RoadKind;
    public bool Bridge;
    public Vector2 Center;
    public float Span;
    public float DeckHeight;
    public float RoadWidth;
}

using System.Collections.Generic;
using UnityEngine;

public sealed class WaterStampTrack
{
    public int[] Placement;
    public Vector2[] Stamp;

    public WaterStampTrack(int count)
    {
        Placement = new int[count];
        Stamp = new Vector2[count];

        for (int i = 0; i < count; i++)
            Placement[i] = -1;
    }

    public bool Stamped(int point)
    {
        return point >= 0 && point < Placement.Length && Placement[point] >= 0;
    }
}

public sealed class WaterStampLayout
{
    public List<WaterStampPlacement> Placements { get; } = new();
    public List<WaterStampRejection> Rejections { get; } = new();
    public List<Vector2[]> MacroRivers { get; } = new();
    public List<WaterStampTrack> Tracks { get; } = new();
    public List<Vector2> FallbackPoints { get; } = new();

    public int RiverPlacements;
    public int LakePlacements;
    public int PondPlacements;
    public int TributaryJoins;
    public int Forks;
    public int FallbackChunks;
    public int FailedChunks;
    public int ProceduralBodies;
    public int Candidates;
    public long LakeCells;
    public long CorridorCells;

    public void Reject(bool record, Vector2 position, string name, string reason)
    {
        if (!record)
            return;

        lock (Rejections)
            Rejections.Add(new WaterStampRejection { Position = position, Name = name, Reason = reason });
    }

    public int Add(WaterStampPlacement placement)
    {
        Placements.Add(placement);

        switch (placement.Kind)
        {
            case WaterStampKind.River:
                RiverPlacements++;
                break;
            case WaterStampKind.Lake:
                LakePlacements++;
                break;
            default:
                PondPlacements++;
                break;
        }

        return Placements.Count - 1;
    }

    public string Summary()
    {
        return $"water stamps: {RiverPlacements} river, {LakePlacements} lake, {PondPlacements} pond placements; {TributaryJoins} tributary joins, {Forks} forks; "
            + $"{FallbackChunks} procedural river chunks ({FailedChunks} with no fitting stamp), {ProceduralBodies} procedural lakes and ponds; "
            + $"{Candidates} candidates tried; {LakeCells} basin and {CorridorCells} corridor height cells reshaped";
    }
}

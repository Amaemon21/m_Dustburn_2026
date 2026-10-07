using System.Collections.Generic;
using UnityEngine;

public sealed class DrainageValley
{
    public int Id;
    public int Parent;
    public bool Mouth;
    public bool Main;
    public bool Terminal;
    public float TerminalRadius;
    public readonly List<Vector2> Points = new();
    public readonly List<float> Floor = new();
    public readonly List<float> Ground = new();
    public readonly List<float> Discharge = new();
}

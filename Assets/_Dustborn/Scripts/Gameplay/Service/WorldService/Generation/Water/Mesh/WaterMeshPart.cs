using System.Collections.Generic;
using UnityEngine;

public sealed class WaterMeshPart
{
    public string Name;
    public WaterKind Kind;
    public bool Frozen;
    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector2> Uv = new();
    public readonly List<int> Triangles = new();
    public readonly List<int> Sources = new();
}

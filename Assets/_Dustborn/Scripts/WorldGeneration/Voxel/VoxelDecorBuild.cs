using System.Collections.Generic;
using UnityEngine;

public class VoxelDecorBuild
{
    public Transform Parent { get; }
    public Vector2 Origin { get; }
    public float Span { get; }
    public int Lod { get; }

    public bool Done { get; internal set; }

    internal List<DecorInstance> Instances { get; } = new();

    internal int Layer { get; set; }
    internal int Cursor { get; set; }
    internal bool Placed { get; set; }

    public VoxelDecorBuild(Vector2 origin, float span, Transform parent, int lod)
    {
        Origin = origin;
        Span = span;
        Parent = parent;
        Lod = lod;
    }

    public bool IsOrphaned => Parent == null;

    public bool Owns(GameObject column)
    {
        return Parent != null && Parent.gameObject == column;
    }

    internal void NextLayer()
    {
        Layer++;
        Cursor = 0;
        Placed = false;

        Instances.Clear();
    }
}

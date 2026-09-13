using System.Collections.Generic;
using UnityEngine;

public class VoxelDecorBuild
{
    public Transform Parent { get; }
    public Vector2 Origin { get; }
    public float Span { get; }
    public int Lod { get; }
    public DecorSurface Frame { get; }
    public DecorScope Scope { get; }

    public bool Done { get; internal set; }

    internal List<DecorInstance> Instances { get; } = new();

    internal int Layer { get; set; }
    internal int Cursor { get; set; }
    internal bool Placed { get; set; }

    public VoxelDecorBuild(Vector2 origin, float span, Transform parent, int lod, DecorSurface frame = default,
        DecorScope scope = DecorScope.All)
    {
        Origin = origin;
        Span = span;
        Parent = parent;
        Lod = lod;
        Frame = frame;
        Scope = scope;
    }

    public bool IsOrphaned => Parent == null;

    public bool Owns(GameObject column)
    {
        return Parent != null && Parent.gameObject == column;
    }

    public bool Wants(DecorKind kind)
    {
        return Scope switch
        {
            DecorScope.Grass => kind == DecorKind.Grass,
            DecorScope.Trees => kind == DecorKind.Tree,
            DecorScope.Rocks => kind == DecorKind.Rock,
            _ => true
        };
    }

    internal void NextLayer()
    {
        Layer++;
        Cursor = 0;
        Placed = false;

        Instances.Clear();
    }
}

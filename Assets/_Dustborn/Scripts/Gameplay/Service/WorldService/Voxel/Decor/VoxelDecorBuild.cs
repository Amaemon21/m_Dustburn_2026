using System.Collections.Generic;
using System.Threading.Tasks;
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

    public Task Preparation { get; internal set; }
    public bool Preparing => Preparation != null && !Preparation.IsCompleted || CardMesh != null && !CardMesh.IsCompleted;
    internal GrassCardMeshBuild CardMesh { get; set; }

    internal void ReleaseMesh()
    {
        CardMesh?.Dispose();
        CardMesh = null;
    }

    internal List<DecorInstance> Instances { get; private set; } = new();
    internal List<DecorInstance>[] Prepared { get; set; }
    internal List<DecorInstance> Batch { get; } = new();

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

    internal void Adopt(List<DecorInstance> instances)
    {
        Instances = instances;
    }

    internal void NextLayer()
    {
        Layer++;
        Cursor = 0;
        Placed = false;

        Instances.Clear();
        Batch.Clear();
    }
}

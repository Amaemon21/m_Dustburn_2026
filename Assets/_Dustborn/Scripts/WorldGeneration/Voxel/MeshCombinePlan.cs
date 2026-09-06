using UnityEngine;

public enum CombineVerdict
{
    Combine,
    Instance,
    Instantiate,
    Skip
}

public struct PrefabProfile
{
    public bool HasLodGroup;
    public bool HasMesh;
    public bool HasCollider;
    public bool Instanceable;
    public bool Readable;
    public int Vertices;

    public static PrefabProfile Empty => new() { HasMesh = false };
}

public static class MeshCombinePlan
{
    public const int MIN_INSTANCES = 4;

    public const int DUPLICATE_CEILING = 400000;

    // A mesh this small is cheaper to duplicate than to re-upload: an instanced draw hands its whole
    // matrix array to the GPU every frame, while a combined one is a renderer the engine culls and
    // batches for free. Grass cards are eight vertices.
    public const int TINY_MESH = 64;

    public static CombineVerdict Decide(PrefabProfile profile, int instances, int vertexBudget, bool keepColliders, bool distanceCulled, out string reason)
    {
        if (!profile.HasMesh || profile.Vertices <= 0)
        {
            reason = "prefab has no mesh to bake";
            return CombineVerdict.Skip;
        }

        if (instances <= 0)
        {
            reason = "nothing placed";
            return CombineVerdict.Skip;
        }

        if (profile.HasLodGroup)
        {
            reason = "prefab has a LODGroup, baking it would freeze the highest LOD at any distance";
            return CombineVerdict.Instantiate;
        }

        if (profile.HasCollider && keepColliders)
        {
            reason = "prefab carries a collider and decor colliders are switched on, it has to stay a separate object";
            return CombineVerdict.Instantiate;
        }

        if (instances < MIN_INSTANCES)
        {
            reason = $"only {instances} of them, a combined mesh saves nothing";
            return CombineVerdict.Instantiate;
        }

        if (profile.Vertices > vertexBudget)
        {
            reason = $"one copy is {profile.Vertices} vertices, over the {vertexBudget} budget of a batch";
            return CombineVerdict.Instantiate;
        }

        if (!profile.Readable)
        {
            reason = "the mesh is not marked Read/Write, CombineMeshes cannot read it";
            return profile.Instanceable ? CombineVerdict.Instance : CombineVerdict.Instantiate;
        }

        if (profile.Instanceable && distanceCulled && profile.Vertices > TINY_MESH)
        {
            reason = "the layer is cut off by its own draw distance, and only the instanced renderer can do that per frame";
            return CombineVerdict.Instance;
        }

        if (profile.Instanceable && (long)profile.Vertices * instances > DUPLICATE_CEILING)
        {
            reason = $"baking {instances} copies would duplicate {(long)profile.Vertices * instances / 1000} thousand vertices, instancing draws them without copying geometry";
            return CombineVerdict.Instance;
        }

        reason = string.Empty;

        return CombineVerdict.Combine;
    }

    public static int BatchSize(PrefabProfile profile, int vertexBudget)
    {
        return Mathf.Max(1, vertexBudget / Mathf.Max(1, profile.Vertices));
    }

    public static int BatchCount(PrefabProfile profile, int instances, int vertexBudget)
    {
        return Mathf.CeilToInt(instances / (float)BatchSize(profile, vertexBudget));
    }
}

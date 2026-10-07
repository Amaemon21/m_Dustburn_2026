using UnityEngine;

public struct VoxelBudget
{
    public long Triangles;
    public long Vertices;
    public long MeshBytes;
    public long ColliderBytes;

    public long TotalBytes => MeshBytes + ColliderBytes;

    public float Megabytes => TotalBytes / 1048576f;

    public const float TRIANGLES_PER_METRE = 2.73f;

    public const float VERTICES_PER_TRIANGLE = 0.537f;

    public static VoxelBudget Estimate(float voxelSize, float areaMetres, bool colliders)
    {
        double triangles = areaMetres * TRIANGLES_PER_METRE / (voxelSize * voxelSize);
        double vertices = triangles * VERTICES_PER_TRIANGLE;

        long mesh = (long)(vertices * 32.0 + triangles * 12.0);

        return new VoxelBudget
        {
            Triangles = (long)triangles,
            Vertices = (long)vertices,
            MeshBytes = mesh,
            ColliderBytes = colliders ? mesh : 0L
        };
    }

    public static VoxelBudget operator +(VoxelBudget first, VoxelBudget second)
    {
        return new VoxelBudget
        {
            Triangles = first.Triangles + second.Triangles,
            Vertices = first.Vertices + second.Vertices,
            MeshBytes = first.MeshBytes + second.MeshBytes,
            ColliderBytes = first.ColliderBytes + second.ColliderBytes
        };
    }

    public string Describe()
    {
        return $"{Triangles / 1e6:0.0} M triangles, {Vertices / 1e6:0.0} M vertices, {Megabytes:0} MB"
            + (ColliderBytes > 0 ? " with colliders" : " without colliders");
    }

    public static string Advise(float voxelSize, float areaMetres, float budgetMegabytes, bool colliders)
    {
        for (float coarser = voxelSize * 2f; coarser <= 16f; coarser *= 2f)
        {
            if (Estimate(coarser, areaMetres, colliders).Megabytes <= budgetMegabytes)
                return $"VoxelSize {coarser:0.#} would fit";
        }

        float side = Mathf.Sqrt(areaMetres);
        float allowed = side * Mathf.Sqrt(budgetMegabytes / Mathf.Max(0.001f, Estimate(voxelSize, areaMetres, colliders).Megabytes));

        return $"at this VoxelSize the region has to shrink to about {allowed:0} m a side";
    }
}

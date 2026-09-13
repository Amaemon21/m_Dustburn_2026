using System;
using Unity.Collections;
using UnityEngine;

public class VoxelMesh : IDisposable
{
    public NativeList<Vector3> Vertices;
    public NativeList<Vector3> Normals;
    public NativeList<Vector2> Uv;
    public NativeList<int> Triangles;

    public VoxelMesh(int capacity = 4096)
    {
        Vertices = new NativeList<Vector3>(capacity, Allocator.Persistent);
        Normals = new NativeList<Vector3>(capacity, Allocator.Persistent);
        Uv = new NativeList<Vector2>(capacity, Allocator.Persistent);
        Triangles = new NativeList<int>(capacity * 3, Allocator.Persistent);
    }

    public bool IsEmpty => Triangles.Length == 0;

    public int VertexCount => Vertices.Length;

    public int TriangleCount => Triangles.Length / 3;

    public void Dispose()
    {
        if (Vertices.IsCreated)
            Vertices.Dispose();

        if (Normals.IsCreated)
            Normals.Dispose();

        if (Uv.IsCreated)
            Uv.Dispose();

        if (Triangles.IsCreated)
            Triangles.Dispose();
    }
}

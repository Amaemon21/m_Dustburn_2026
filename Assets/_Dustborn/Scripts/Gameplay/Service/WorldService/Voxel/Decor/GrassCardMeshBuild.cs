using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class GrassCardMeshBuild : IDisposable
{
    private static readonly VertexAttributeDescriptor[] Layout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
        new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3)
    };

    private NativeArray<DecorInstance> _instances;
    private NativeArray<GrassMeshVertex> _vertices;
    private NativeArray<int> _indices;
    private JobHandle _handle;
    private bool _disposed;

    public Vector3 Origin { get; }
    public int Count { get; }
    public bool IsCompleted => _handle.IsCompleted;
    public void Complete() => _handle.Complete();

    public GrassCardMeshBuild(IReadOnlyList<DecorInstance> instances, int start, int count, DecorTint tint)
    {
        Count = count;
        Origin = instances[start].Position;

        try
        {
            _instances = new NativeArray<DecorInstance>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _vertices = new NativeArray<GrassMeshVertex>(count * GrassTuft.VERTICES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _indices = new NativeArray<int>(count * GrassTuft.PLANES * 6, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < count; i++)
                _instances[i] = instances[start + i];

            _handle = new GrassMeshJob
            {
                Instances = _instances, Vertices = _vertices, Indices = _indices, Origin = Origin, Tint = tint.Color
            }.Schedule(count, 32);

            JobHandle.ScheduleBatchedJobs();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Mesh Upload(string name)
    {
        if (_disposed || !IsCompleted)
            throw new InvalidOperationException("Grass geometry must finish before it is uploaded.");

        _handle.Complete();
        var mesh = new Mesh { name = name };

        try
        {
            const MeshUpdateFlags FLAGS = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.SetVertexBufferParams(_vertices.Length, Layout);
            mesh.SetVertexBufferData(_vertices, 0, 0, _vertices.Length, 0, FLAGS);
            mesh.SetIndexBufferParams(_indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(_indices, 0, 0, _indices.Length, FLAGS);
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(2f * GrassCardFactory.SWAY_REACH);
            mesh.bounds = bounds;
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, _indices.Length, MeshTopology.Triangles)
            {
                vertexCount = _vertices.Length,
                bounds = bounds
            }, FLAGS);
            return mesh;
        }
        catch
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mesh);
            else
                UnityEngine.Object.DestroyImmediate(mesh);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _handle.Complete();

        if (_instances.IsCreated)
            _instances.Dispose();
        if (_vertices.IsCreated)
            _vertices.Dispose();
        if (_indices.IsCreated)
            _indices.Dispose();
    }
}

using System;
using Unity.Collections;
using Unity.Jobs;

public class VoxelChunkMesher : IDisposable
{
    private readonly VoxelDensityField _field;

    private readonly int _size;
    private readonly float _baseVoxelSize;
    private readonly float _baseSkirtDepth;

    private NativeArray<float> _columns;
    private NativeArray<float> _density;
    private NativeArray<int> _vertexAt;

    private VoxelCellLayout _cells;
    private JobHandle _pending;

    public VoxelChunkMesher(VoxelConfig voxels, VoxelDensityField field)
    {
        _field = field;

        _size = voxels.ChunkSize;
        _baseVoxelSize = voxels.VoxelSize;
        _baseSkirtDepth = voxels.SkirtDepth;

        int samples = _size + 2;
        int slots = _size + 1;

        _columns = new NativeArray<float>(samples * samples, Allocator.Persistent);
        _density = new NativeArray<float>(samples * samples * samples, Allocator.Persistent);
        _vertexAt = new NativeArray<int>(slots * slots * slots, Allocator.Persistent);

        _cells = VoxelCellLayout.Create(Allocator.Persistent);
    }

    public void Mesh(int lod, int chunkX, int chunkY, int chunkZ, VoxelMesh output, int trim = 0)
    {
        Schedule(lod, chunkX, chunkY, chunkZ, output, trim).Complete();
    }

    public JobHandle Schedule(int lod, int chunkX, int chunkY, int chunkZ, VoxelMesh output, int trim = 0)
    {
        int scale = 1 << lod;

        var job = new VoxelMeshJob
        {
            Field = _field.Sampler,
            ChunkX = chunkX,
            ChunkY = chunkY,
            ChunkZ = chunkZ,
            Size = _size,
            VoxelSize = _baseVoxelSize * scale,
            SkirtDepth = lod == 0 ? 0f : _baseSkirtDepth * scale,
            Trim = trim,
            Columns = _columns,
            Density = _density,
            VertexAt = _vertexAt,
            Cells = _cells,
            Vertices = output.Vertices,
            Normals = output.Normals,
            Uv = output.Uv,
            Triangles = output.Triangles
        };

        _pending = job.Schedule(_pending);

        return _pending;
    }

    public void Dispose()
    {
        _pending.Complete();

        NativeBuffer.Release(ref _columns);
        NativeBuffer.Release(ref _density);
        NativeBuffer.Release(ref _vertexAt);

        _cells.Dispose();
    }
}

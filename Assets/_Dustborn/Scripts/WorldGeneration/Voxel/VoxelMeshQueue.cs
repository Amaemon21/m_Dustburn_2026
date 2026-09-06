using System;
using Unity.Jobs;
using UnityEngine;

public class VoxelMeshQueue : IDisposable
{
    private readonly VoxelConfig _voxels;
    private readonly VoxelDensityField _field;

    private readonly VoxelChunkMesher[] _meshers;
    private readonly VoxelMesh[] _meshes;
    private readonly JobHandle[] _handles;
    private readonly int[] _chunkY;

    private int _inFlight;

    public int Workers => _meshers.Length;

    public int InFlight => _inFlight;

    public int Free => _meshers.Length - _inFlight;

    public VoxelMeshQueue(VoxelConfig voxels, VoxelDensityField field, int workers)
    {
        _voxels = voxels;
        _field = field;

        int count = Mathf.Max(1, workers);

        _meshers = new VoxelChunkMesher[count];
        _meshes = new VoxelMesh[count];
        _handles = new JobHandle[count];
        _chunkY = new int[count];
    }

    public bool TrySchedule(int lod, int chunkX, int chunkY, int chunkZ, int trim)
    {
        if (_inFlight >= _meshers.Length)
            return false;

        int slot = _inFlight++;

        _meshers[slot] ??= new VoxelChunkMesher(_voxels, _field);
        _meshes[slot] ??= new VoxelMesh();
        _chunkY[slot] = chunkY;

        _handles[slot] = _meshers[slot].Schedule(lod, chunkX, chunkY, chunkZ, _meshes[slot], trim);

        return true;
    }

    public void Kick()
    {
        if (_inFlight > 0)
            JobHandle.ScheduleBatchedJobs();
    }

    public int Drain()
    {
        for (int slot = 0; slot < _inFlight; slot++)
            _handles[slot].Complete();

        return _inFlight;
    }

    public VoxelMesh Result(int slot, out int chunkY)
    {
        chunkY = _chunkY[slot];

        return _meshes[slot];
    }

    public void Reset()
    {
        _inFlight = 0;
    }

    public void Dispose()
    {
        Drain();

        for (int slot = 0; slot < _meshers.Length; slot++)
        {
            _meshers[slot]?.Dispose();
            _meshes[slot]?.Dispose();
        }

        _inFlight = 0;
    }
}

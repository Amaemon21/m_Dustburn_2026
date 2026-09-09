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

    private readonly int[] _work;
    private readonly long[] _keys;

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
        _work = new int[count];
        _keys = new long[count];
    }

    public bool TrySchedule(int lod, int chunkX, int chunkY, int chunkZ, int seams, int morph)
    {
        if (_inFlight >= _meshers.Length)
            return false;

        int slot = _inFlight++;

        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.VoxelMeshSchedule);

        _meshers[slot] ??= new VoxelChunkMesher(_voxels, _field);
        _meshes[slot] ??= new VoxelMesh();
        _chunkY[slot] = chunkY;
        _work[slot] = WorldGenProbe.NextWorkId();
        _keys[slot] = WorldGenProbe.ColumnKey(lod, chunkX, chunkY, chunkZ, seams, morph);

        WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Requested, _work[slot], _keys[slot], _inFlight);

        _handles[slot] = _meshers[slot].Schedule(lod, chunkX, chunkY, chunkZ, _meshes[slot], seams, morph);

        WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Scheduled, _work[slot], _keys[slot], _inFlight);

        return true;
    }

    public void Kick()
    {
        if (_inFlight > 0)
            JobHandle.ScheduleBatchedJobs();
    }

    public int Drain()
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.VoxelMeshDrain);

        for (int slot = 0; slot < _inFlight; slot++)
        {
            using (WorldGenProbe.Measure(WorldGenStage.VoxelMeshBlocking))
                _handles[slot].Complete();

            WorldGenProbe.Queue(WorldGenQueueKind.TerrainChunk, WorldGenQueuePhase.Observed, _work[slot], _keys[slot], _inFlight - slot);
        }

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

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class VoxelColliderQueue : IDisposable
{
    public const MeshColliderCookingOptions COOKING =
        MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.UseFastMidphase;

    private readonly struct Pending
    {
        public GameObject Holder { get; }
        public Mesh Mesh { get; }

        public Pending(GameObject holder, Mesh mesh)
        {
            Holder = holder;
            Mesh = mesh;
        }
    }

    private struct BakeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<EntityId> Ids;

        public void Execute(int index)
        {
            Physics.BakeMesh(Ids[index], false, COOKING);
        }
    }

    private readonly Queue<Pending> _queue = new();
    private readonly List<Pending> _baking = new();

    private readonly int _batch;

    private NativeArray<EntityId> _ids;
    private JobHandle _handle;
    private bool _inFlight;

    public int Waiting => _queue.Count;

    public VoxelColliderQueue(int batch)
    {
        _batch = Mathf.Max(1, batch);
        _ids = new NativeArray<EntityId>(_batch, Allocator.Persistent);
    }

    public void Add(GameObject holder, Mesh mesh)
    {
        _queue.Enqueue(new Pending(holder, mesh));
    }

    public void Collect()
    {
        if (!_inFlight)
            return;

        _handle.Complete();

        foreach (Pending pending in _baking)
        {
            if (pending.Holder == null || pending.Mesh == null)
                continue;

            var collider = pending.Holder.AddComponent<MeshCollider>();

            collider.cookingOptions = COOKING;
            collider.sharedMesh = pending.Mesh;
        }

        _baking.Clear();

        _inFlight = false;
    }

    public void Dispatch()
    {
        if (_inFlight || _queue.Count == 0)
            return;

        int count = 0;

        while (count < _batch && _queue.Count > 0)
        {
            Pending pending = _queue.Dequeue();

            if (pending.Holder == null || pending.Mesh == null)
                continue;

            _ids[count] = pending.Mesh.GetEntityId();
            _baking.Add(pending);

            count++;
        }

        if (count == 0)
            return;

        _handle = new BakeJob { Ids = _ids.GetSubArray(0, count) }.Schedule(count, 1);
        _inFlight = true;

        JobHandle.ScheduleBatchedJobs();
    }

    public void Dispose()
    {
        if (_inFlight)
            _handle.Complete();

        _inFlight = false;

        _baking.Clear();
        _queue.Clear();

        if (_ids.IsCreated)
            _ids.Dispose();
    }
}

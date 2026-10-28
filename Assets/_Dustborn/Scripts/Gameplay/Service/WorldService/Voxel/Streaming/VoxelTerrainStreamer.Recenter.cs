using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class VoxelTerrainStreamer
{
    private const float RECENTER_SHARE = 0.5f;
    private const int SWAP_COLUMNS_PER_FRAME = 8;

    private sealed class Replan
    {
        public VoxelStreamPlan Plan { get; }
        public List<VoxelColumnKey> Keys { get; }
        public Vector2 Center { get; }
        public List<VoxelColumnSwap> Swaps { get; }

        public Replan(VoxelStreamPlan plan, List<VoxelColumnKey> keys, Vector2 center, List<VoxelColumnSwap> swaps)
        {
            Plan = plan;
            Keys = keys;
            Center = center;
            Swaps = swaps;
        }
    }

    private readonly List<VoxelColumnKey> _obsolete = new();
    private readonly List<VoxelColumnKey> _built = new();
    private readonly List<Vector2Int> _changed = new();
    private WaterMap _waterMap;
    private List<Vector3> _shoreSamples;
    private Task<Replan> _replan;
    private Replan _staging;
    private readonly System.Diagnostics.Stopwatch _swapClock = new();
    private int _swapIndex;
    private int _changedTotal;

    public event Action<IReadOnlyList<Vector2Int>> GroundChanged;

    public bool Recentering => _replan != null || _staging != null;
    public Vector2 GroundCenter => _groundCenter;

    private void Recenter(Vector2 ground)
    {
        if (!_ready)
            return;

        if (_staging != null)
        {
            TrySwap();
            return;
        }

        if (_replan != null)
        {
            if (!_replan.IsCompleted)
                return;

            Task<Replan> finished = _replan;
            _replan = null;

            if (finished.IsFaulted)
            {
                Debug.LogException(finished.Exception?.GetBaseException(), this);
                return;
            }

            Stage(finished.Result);
            return;
        }

        if (DistanceUtility.WithinRadius(ground, _groundCenter, _plan.Distance(0) * RECENTER_SHARE))
            return;

        VoxelDensitySampler sampler = _field.Sampler;
        VoxelConfig voxels = _voxels;
        int lodCount = _lodCount;
        float near = _nearDistance;
        float worldSize = _config.WorldSize;
        WaterMap water = _waterMap;
        List<Vector3> samples = _shoreSamples;
        var previous = new List<VoxelColumnKey>(_loaded.Keys);

        _replan = Task.Run(() => Replanned(sampler, voxels, lodCount, near, worldSize, water, samples, ground, previous));
    }

    private static Replan Replanned(VoxelDensitySampler sampler, VoxelConfig voxels, int lodCount, float near, float worldSize,
        WaterMap water, List<Vector3> samples, Vector2 center, List<VoxelColumnKey> previous)
    {
        var plan = new VoxelStreamPlan(voxels, lodCount, near, worldSize, water);
        VoxelShorePlan.Refine(sampler, plan, center, voxels.ChunkSize, samples);

        var keys = new List<VoxelColumnKey>();
        plan.Around(center, keys);
        var before = new HashSet<VoxelColumnKey>(previous);
        var after = new HashSet<VoxelColumnKey>(keys);
        List<VoxelColumnKey> added = keys.FindAll(key => !before.Contains(key));
        List<VoxelColumnKey> removed = previous.FindAll(key => !after.Contains(key));
        return new Replan(plan, keys, center, VoxelColumnSwap.Plan(added, removed));
    }

    private void Stage(Replan replan)
    {
        var wanted = new HashSet<VoxelColumnKey>(replan.Keys);

        _obsolete.Clear();
        foreach (VoxelColumnKey key in _loaded.Keys)
            if (!wanted.Contains(key))
                _obsolete.Add(key);

        _built.Clear();
        foreach (VoxelColumnKey key in replan.Keys)
            if (!_loaded.ContainsKey(key))
                _built.Add(key);

        if (_built.Count == 0 && _obsolete.Count == 0)
        {
            _groundCenter = replan.Center;
            _plan = replan.Plan;
            return;
        }

        _staging = replan;
        _swapIndex = 0;
        _changedTotal = 0;
        _sortOrigin = replan.Center;
        _built.Sort(_byDistance);

        foreach (VoxelColumnKey key in _built)
            _pending.Enqueue(key);
    }

    private void TrySwap()
    {
        if (_pending.Count > 0 || _hasActive || _queue.InFlight > 0 || _paving.Count > 0 || _colliders.Waiting > 0)
            return;

        _changed.Clear();
        _swapClock.Restart();
        int columns = 0;

        using (WorldGenProbe.Measure(WorldGenStage.RecenterSwap))
        {
            while (_swapIndex < _staging.Swaps.Count && columns < SWAP_COLUMNS_PER_FRAME
                && _swapClock.Elapsed.TotalMilliseconds < _recenterBudget)
            {
                VoxelColumnSwap swap = _staging.Swaps[_swapIndex++];
                ApplySwap(swap);
                columns += swap.Added.Count + swap.Removed.Count;
            }
        }

        if (_changed.Count > 0)
        {
            _changedTotal += _changed.Count;
            RefreshDecor();
            Physics.SyncTransforms();
            GroundChanged?.Invoke(_changed);
        }

        if (_swapIndex < _staging.Swaps.Count)
            return;

        _plan = _staging.Plan;
        _groundCenter = _staging.Center;
        _wanted.Clear();
        _wanted.AddRange(_staging.Keys);

        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
            "Voxel terrain: rings moved to ({0:0}, {1:0}), built {2} columns, retired {3}, {4} fine cells changed their ground, {5} decor patches to regrow",
            _groundCenter.x, _groundCenter.y, _built.Count, _obsolete.Count, _changedTotal, _regrow.Count);

        _staging = null;
        _built.Clear();
        _obsolete.Clear();
    }

    private void ApplySwap(VoxelColumnSwap swap)
    {
        foreach (VoxelColumnKey key in swap.Added)
        {
            if (!_loaded.TryGetValue(key, out Column column))
                continue;

            column.Staged = false;
            Show(column);
            Cover(key);
            Changed(key);
        }

        foreach (VoxelColumnKey key in swap.Removed)
        {
            if (!_loaded.Remove(key, out Column column) || column.Root == null)
                continue;

            column.Root.SetActive(false);
            _demolishing.Enqueue(new VoxelDecorDemolition(column.Root));
        }
    }

    private void Changed(VoxelColumnKey key)
    {
        int scale = 1 << key.Lod;

        for (int z = 0; z < scale; z++)
            for (int x = 0; x < scale; x++)
                _changed.Add(new Vector2Int(key.X * scale + x, key.Z * scale + z));
    }

    private void RefreshDecor()
    {
        foreach (DecorRing ring in _rings)
        {
            foreach (Vector2Int cell in _changed)
            {
                if (ring.Patches.ContainsKey(cell) && ring.Stale.Add(cell))
                    _regrow.Enqueue((ring, cell));
            }
        }
    }

    private void StopRecentering()
    {
        if (_replan != null && !_replan.IsCompleted)
        {
            try
            {
                _replan.Wait();
            }
            catch (AggregateException)
            {
            }
        }

        _replan = null;
        _staging = null;
        _built.Clear();
        _obsolete.Clear();
        _swapIndex = 0;
        _changedTotal = 0;
    }
}

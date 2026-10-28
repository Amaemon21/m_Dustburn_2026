using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public class VoxelDecorSpawner : IDisposable
{
    private const int SCAN_COST = 24;
    private const int CELLS_PER_UNIT = 128;
    private const int INSTANCES_PER_UNIT = 64;
    private const int MAX_INSTANCE_BATCH = 256;
    private const float FAR_DRAW_DISTANCE = 3000f;

    private static readonly ProfilerMarker WorkerPlace = new("WorldGen.Decor.DecorPlaceWorker");

    private VoxelDecorPlacer _placer;
    private readonly VoxelConfig _voxels;
    private readonly MeshCombiner[] _combiners;
    private readonly MeshCombiner[] _far;
    private readonly GrassCardFactory _cards = new();

    private readonly int _vertexBudget;
    private readonly bool _keepColliders;
    private VoxelDecorPool _pool;

    public int Placed { get; private set; }
    public int Combined { get; private set; }
    public int Instanced { get; private set; }
    public int Instantiated { get; private set; }
    public int Reused { get; private set; }
    public int Batches { get; private set; }
    public bool FarDecor { get; set; }
    public IDecorObjectHook Hook { get; set; }

    public VoxelDecorSpawner(VoxelDecorPlacer placer, VoxelConfig voxels, int vertexBudget, bool keepColliders)
    {
        _placer = placer;
        _voxels = voxels;
        _vertexBudget = vertexBudget;
        _keepColliders = keepColliders;

        _cards.Distance = voxels.GrassDistance;

        _combiners = new MeshCombiner[placer.Layers.Count];
        _far = new MeshCombiner[placer.Layers.Count];

        for (int i = 0; i < _combiners.Length; i++)
            _combiners[i] = Build(placer.Layers[i]);
    }

    public bool Use(VoxelDecorPlacer placer)
    {
        if (placer.Layers.Count != _placer.Layers.Count)
            return false;

        for (int i = 0; i < placer.Layers.Count; i++)
        {
            if (placer.Layers[i].Prefab != _placer.Layers[i].Prefab || placer.Layers[i].Card != _placer.Layers[i].Card)
                return false;
        }

        _placer = placer;

        return true;
    }

    public void EnablePooling(Transform parent)
    {
        _pool ??= new VoxelDecorPool(parent, _combiners.Length);
    }

    public bool Recycle(GameObject copy)
    {
        if (_pool == null || !_pool.Owns(copy))
            return false;

        Hook?.Despawned(copy);
        _pool.Return(copy);
        return true;
    }

    public void ClearPool() => _pool?.Clear();

    public void Spawn(Vector2 origin, float span, Transform parent, int lod = 0, DecorSurface frame = default,
        DecorScope scope = DecorScope.All)
    {
        Spawn(new VoxelDecorBuild(origin, span, parent, lod, frame, scope));
    }

    public void Spawn(VoxelDecorBuild build)
    {
        while (!build.Done)
        {
            Step(build, int.MaxValue);
            build.CardMesh?.Complete();
        }
    }

    public void Prepare(IReadOnlyList<VoxelDecorBuild> builds)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.DecorPlace);

        Parallel.For(0, builds.Count, index => Place(builds[index]));
    }

    public void PrepareInBackground(List<VoxelDecorBuild> builds)
    {
        if (builds.Count == 0)
            return;

        VoxelDecorBuild[] batch = builds.ToArray();

        Task preparation = Task.Run(() =>
        {
            using (WorkerPlace.Auto())
                Parallel.For(0, batch.Length, index => Place(batch[index]));
        });

        foreach (VoxelDecorBuild build in batch)
            build.Preparation = preparation;
    }

    private void Place(VoxelDecorBuild build)
    {
        VoxelDecorPlacer placer = _placer;
        var prepared = new List<DecorInstance>[_combiners.Length];

        for (int layer = 0; layer < prepared.Length && !build.Done; layer++)
        {
            if (!Reaches(build, placer.Layers[layer]))
                continue;

            prepared[layer] = new List<DecorInstance>();
            placer.Place(layer, build.Origin, build.Span, prepared[layer], build.Frame);
        }

        build.Prepared = prepared;
    }

    public int Step(VoxelDecorBuild build, int budget)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.DecorStep);

        int spent = 0;

        while (!build.Done)
        {
            if (build.Preparing)
                break;

            if (build.IsOrphaned || build.Layer >= _combiners.Length)
            {
                build.Done = true;

                break;
            }

            spent += Advance(build);

            if (spent >= budget)
                break;
        }

        return spent;
    }

    public string Describe()
    {
        var text = new StringBuilder();

        for (int layer = 0; layer < _combiners.Length; layer++)
        {
            MeshCombiner combiner = _combiners[layer];
            VoxelDecorLayer decor = _placer.Layers[layer];

            CombineVerdict verdict = MeshCombinePlan.Decide(combiner.Profile, MeshCombinePlan.MIN_INSTANCES, _vertexBudget,
                _keepColliders, DrawDistance(decor) > 0f, out string reason);

            text.AppendLine($"  {decor.Kind,-5} {decor.Name,-34} {combiner.Profile.Vertices,6} vertices, "
                + $"{MeshCombinePlan.BatchSize(combiner.Profile, _vertexBudget),5} per batch -> {verdict}"
                + (reason.Length == 0 ? string.Empty : $" ({reason})"));
        }

        return text.ToString();
    }

    public void Dispose()
    {
        _pool?.Dispose();
        _pool = null;

        foreach (MeshCombiner combiner in _combiners)
            combiner.Dispose();

        foreach (MeshCombiner far in _far)
            far?.Dispose();

        _cards.Dispose();
    }

    private int Advance(VoxelDecorBuild build)
    {
        VoxelDecorLayer decor = _placer.Layers[build.Layer];

        if (!Reaches(build, decor))
        {
            build.NextLayer();

            return 0;
        }

        if (!build.Placed)
        {
            List<DecorInstance> prepared = build.Prepared?[build.Layer];

            if (prepared != null)
            {
                build.Adopt(prepared);
                build.Prepared[build.Layer] = null;
                build.Placed = true;
                Placed += build.Instances.Count;

                return 0;
            }

            int cells;

            using (WorldGenProbe.Measure(WorldGenStage.DecorPlace))
                cells = _placer.Place(build.Layer, build.Origin, build.Span, build.Instances, build.Frame);

            build.Placed = true;

            Placed += build.Instances.Count;

            return Mathf.Max(SCAN_COST, cells / CELLS_PER_UNIT);
        }

        if (build.Instances.Count == 0)
        {
            build.NextLayer();

            return 0;
        }

        if (Far(build, decor))
        {
            MeshCombiner far = _far[build.Layer] ??= MeshCombiner.Far(decor.Prefab, DecorTint.Of(decor));
            int count = build.Instances.Count;

            using (WorldGenProbe.Measure(WorldGenStage.DecorInstance))
                Batches += far.InstanceParts(build.Instances, build.Parent, decor.Name, ShadowCastingMode.Off, FAR_DRAW_DISTANCE);

            Instanced += count;
            build.NextLayer();

            return Mathf.Max(1, count / INSTANCES_PER_UNIT);
        }

        MeshCombiner combiner = _combiners[build.Layer];

        CombineVerdict verdict = MeshCombinePlan.Decide(combiner.Profile, build.Instances.Count, _vertexBudget,
            _keepColliders, DrawDistance(decor) > 0f, out _);

        if (verdict == CombineVerdict.Instantiate && decor.Prefab == null)
            verdict = CombineVerdict.Combine;

        return verdict == CombineVerdict.Instantiate
            ? Populate(build, decor)
            : Finish(build, combiner, decor, verdict);
    }

    private bool Reaches(VoxelDecorBuild build, VoxelDecorLayer decor)
    {
        if (!build.Wants(decor.Kind))
            return false;

        return build.Scope != DecorScope.All || build.Lod <= _voxels.MaxLod(decor.Kind) || FarDecor && decor.Kind != DecorKind.Grass;
    }

    private bool Far(VoxelDecorBuild build, VoxelDecorLayer decor)
    {
        return FarDecor && build.Scope == DecorScope.All && decor.Kind != DecorKind.Grass && build.Lod > _voxels.MaxLod(decor.Kind);
    }

    private int Finish(VoxelDecorBuild build, MeshCombiner combiner, VoxelDecorLayer decor, CombineVerdict verdict)
    {
        if (verdict == CombineVerdict.Skip)
        {
            build.NextLayer();
            return 1;
        }

        if (_pool != null && verdict == CombineVerdict.Combine && combiner.IsCard)
            return BuildCards(build, combiner, decor);

        int batchSize = verdict == CombineVerdict.Combine
            ? MeshCombinePlan.BatchSize(combiner.Profile, _vertexBudget)
            : MAX_INSTANCE_BATCH;

        int count = Mathf.Min(batchSize, build.Instances.Count - build.Cursor);
        build.Batch.Clear();

        for (int i = 0; i < count; i++)
            build.Batch.Add(build.Instances[build.Cursor + i]);

        switch (verdict)
        {
            case CombineVerdict.Combine:
                using (WorldGenProbe.Measure(WorldGenStage.DecorCombine))
                    Batches += combiner.Combine(build.Batch, _vertexBudget, build.Parent, decor.Name, Shadows(decor));

                Combined += count;
                break;

            case CombineVerdict.Instance:
                using (WorldGenProbe.Measure(WorldGenStage.DecorInstance))
                    Batches += combiner.Instance(build.Batch, build.Parent, decor.Name, Shadows(decor), DrawDistance(decor));

                Instanced += count;

                break;
        }

        build.Cursor += count;

        if (build.Cursor >= build.Instances.Count)
            build.NextLayer();

        return Mathf.Max(1, count);
    }

    private int BuildCards(VoxelDecorBuild build, MeshCombiner combiner, VoxelDecorLayer decor)
    {
        if (build.CardMesh == null)
        {
            int count = Mathf.Min(MeshCombinePlan.BatchSize(combiner.Profile, _vertexBudget), build.Instances.Count - build.Cursor);

            using (WorldGenProbe.Measure(WorldGenStage.GrassMeshSchedule))
                build.CardMesh = combiner.ScheduleCards(build.Instances, build.Cursor, count);

            return 1;
        }

        if (!build.CardMesh.IsCompleted)
            return 1;

        int built = build.CardMesh.Count;

        try
        {
            using (WorldGenProbe.Measure(WorldGenStage.GrassMeshUpload))
                combiner.AttachCards(build.CardMesh, build.Parent, decor.Name, Shadows(decor));
        }
        finally
        {
            build.ReleaseMesh();
        }

        Combined += built;
        Batches++;
        build.Cursor += built;

        if (build.Cursor >= build.Instances.Count)
            build.NextLayer();

        return built;
    }

    private int Populate(VoxelDecorBuild build, VoxelDecorLayer decor)
    {
        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.DecorInstantiate);

        DecorInstance instance = build.Instances[build.Cursor];
        DecorKey key = new(build.Layer, instance.CellX, instance.CellY);
        MeshCombiner combiner = _combiners[build.Layer];
        int layer = build.Layer;

        build.Cursor++;

        if (build.Cursor >= build.Instances.Count)
            build.NextLayer();

        if (Hook != null && Hook.IsRemoved(key))
            return 1;

        bool reused = false;
        GameObject copy = _pool == null
            ? UnityEngine.Object.Instantiate(decor.Prefab, instance.Position, MeshCombiner.Orientation(instance), build.Parent)
            : _pool.Take(decor.Prefab, layer, build.Parent, out reused);

        if (_pool != null)
            copy.transform.SetPositionAndRotation(instance.Position, MeshCombiner.Orientation(instance));

        copy.transform.localScale = new Vector3(instance.Scale, instance.Height, instance.Scale);

        combiner.Prepare(copy, instance.Shade, !reused);

        if (reused)
            Reused++;
        else
            Instantiated++;

        Batches++;

        Hook?.Spawned(copy, key);

        if (_pool != null)
            copy.SetActive(decor.Prefab.activeSelf);

        return 1;
    }

    private MeshCombiner Build(VoxelDecorLayer layer)
    {
        DecorTint tint = DecorTint.Of(layer);

        return layer.Card == null
            ? new MeshCombiner(layer.Prefab, tint)
            : new MeshCombiner(_cards.Mesh, _cards.Material(layer.Style), tint);
    }

    private float DrawDistance(VoxelDecorLayer layer)
    {
        return layer.Kind == DecorKind.Grass ? _voxels.GrassDistance : 0f;
    }

    private ShadowCastingMode Shadows(VoxelDecorLayer layer)
    {
        return layer.Kind == DecorKind.Grass ? ShadowCastingMode.Off : ShadowCastingMode.On;
    }
}

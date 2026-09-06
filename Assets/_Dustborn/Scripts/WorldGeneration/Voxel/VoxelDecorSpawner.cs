using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public class VoxelDecorSpawner : IDisposable
{
    private const int SCAN_COST = 24;
    private const int CELLS_PER_UNIT = 128;
    private const int INSTANCES_PER_UNIT = 64;

    private readonly VoxelDecorPlacer _placer;
    private readonly VoxelConfig _voxels;
    private readonly MeshCombiner[] _combiners;
    private readonly GrassCardFactory _cards = new();

    private readonly int _vertexBudget;
    private readonly bool _keepColliders;

    public int Placed { get; private set; }
    public int Combined { get; private set; }
    public int Instanced { get; private set; }
    public int Instantiated { get; private set; }
    public int Batches { get; private set; }

    public VoxelDecorSpawner(VoxelDecorPlacer placer, VoxelConfig voxels, int vertexBudget, bool keepColliders)
    {
        _placer = placer;
        _voxels = voxels;
        _vertexBudget = vertexBudget;
        _keepColliders = keepColliders;

        _cards.Distance = voxels.GrassDistance;

        _combiners = new MeshCombiner[placer.Layers.Count];

        for (int i = 0; i < _combiners.Length; i++)
            _combiners[i] = Build(placer.Layers[i]);
    }

    public void Spawn(Vector2 origin, float span, Transform parent, int lod = 0)
    {
        var build = new VoxelDecorBuild(origin, span, parent, lod);

        while (!build.Done)
            Step(build, int.MaxValue);
    }

    public int Step(VoxelDecorBuild build, int budget)
    {
        int spent = 0;

        while (!build.Done)
        {
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
        _cards.Dispose();
    }

    private int Advance(VoxelDecorBuild build)
    {
        VoxelDecorLayer decor = _placer.Layers[build.Layer];

        if (build.Lod > _voxels.MaxLod(decor.Kind))
        {
            build.NextLayer();

            return 1;
        }

        if (!build.Placed)
        {
            int cells = _placer.Place(build.Layer, build.Origin, build.Span, build.Instances);

            build.Placed = true;

            Placed += build.Instances.Count;

            return Mathf.Max(SCAN_COST, cells / CELLS_PER_UNIT);
        }

        if (build.Instances.Count == 0)
        {
            build.NextLayer();

            return 1;
        }

        MeshCombiner combiner = _combiners[build.Layer];

        CombineVerdict verdict = MeshCombinePlan.Decide(combiner.Profile, build.Instances.Count, _vertexBudget,
            _keepColliders, DrawDistance(decor) > 0f, out _);

        if (verdict == CombineVerdict.Instantiate && decor.Prefab == null)
            verdict = CombineVerdict.Instance;

        return verdict == CombineVerdict.Instantiate
            ? Populate(build, decor)
            : Finish(build, combiner, decor, verdict);
    }

    private int Finish(VoxelDecorBuild build, MeshCombiner combiner, VoxelDecorLayer decor, CombineVerdict verdict)
    {
        int count = build.Instances.Count;

        switch (verdict)
        {
            case CombineVerdict.Combine:
                Batches += combiner.Combine(build.Instances, _vertexBudget, build.Parent, decor.Name, Shadows(decor));
                Combined += count;
                break;

            case CombineVerdict.Instance:
                Batches += combiner.Instance(build.Instances, build.Parent, decor.Name, Shadows(decor), DrawDistance(decor));
                Instanced += count;

                build.NextLayer();

                return Mathf.Max(1, count / INSTANCES_PER_UNIT);
        }

        build.NextLayer();

        return count;
    }

    private int Populate(VoxelDecorBuild build, VoxelDecorLayer decor)
    {
        DecorInstance instance = build.Instances[build.Cursor];

        GameObject copy = UnityEngine.Object.Instantiate(decor.Prefab, instance.Position,
            Quaternion.Euler(0f, instance.Rotation, 0f), build.Parent);

        copy.transform.localScale = new Vector3(instance.Scale, instance.Height, instance.Scale);

        build.Cursor++;

        Instantiated++;
        Batches++;

        if (build.Cursor >= build.Instances.Count)
            build.NextLayer();

        return 1;
    }

    private MeshCombiner Build(VoxelDecorLayer layer)
    {
        return layer.Card == null
            ? new MeshCombiner(layer.Prefab)
            : new MeshCombiner(_cards.Mesh, _cards.Material(layer.Card, layer.Tint));
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

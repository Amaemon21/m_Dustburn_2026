using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class WorldRuntimeLoader : IDisposable
{
    private readonly GameObject _root;
    private readonly VoxelTerrainStreamer _terrain;
    private readonly PoiInstanceBuilder _pois;
    private readonly int _poisPerFrame;
    private readonly PropInstanceBuilder _props;
    private readonly int _propsPerFrame;
    private readonly float _cellSize;
    private readonly float _propRegroundBudget;
    private readonly List<Vector2Int> _groundChanges = new();
    private readonly HashSet<Vector2Int> _groundChangeSet = new();
    private float _progress;

    public bool Ready => _terrain.Ready && _pois.Ready && _props.Ready && _props.RegroundPending == 0;
    public float Progress => Ready ? 1f : _progress;
    public string Stage => !_terrain.Ready ? "Загрузка ландшафта и растительности" : !_pois.Ready ? "Размещение зданий" : "Машины, хлам и тайники";

    public event Action<IReadOnlyList<Vector2Int>, float> GroundChanged;

    public WorldRuntimeLoader(WorldBuildSettings settings, WorldData world, Transform viewer, Transform parent,
        IDecorObjectHook decorHook = null, IWorldObjectHook objectHook = null)
    {
        if (settings == null || settings.Voxels == null || world == null || viewer == null)
            throw new InvalidOperationException("World loading requires a world, voxel settings and a viewer.");

        WaterMap water = world.Water;
        WorldBuildBudget.Validate(settings, world.Config, new Vector2(viewer.position.x, viewer.position.z), true, water);

        _root = new GameObject("World Runtime");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);

        try
        {
            _terrain = _root.AddComponent<VoxelTerrainStreamer>();
            _terrain.DecorHook = decorHook;
            _terrain.Configure(settings, world, viewer);
            _terrain.hideFlags = HideFlags.HideInInspector;

            using (WorldGenProbe.Measure(WorldGenStage.RuntimeWater))
            {
                WaterSurfaceBuilder.Build(water, settings.WaterMaterial, settings.IceMaterial, _root.transform,
                    WorldEdgeBuilder.OffshoreReach(world.Config));
                WorldEdgeBuilder.Build(world.Config, world.Material, _root.transform);
                WaterCrossingBuilder.Build(water, settings.BridgeMaterial, settings.CulvertPrefab, _root.transform);
            }

            var buildings = new GameObject("POI");
            buildings.transform.SetParent(_root.transform, false);
            _pois = new PoiInstanceBuilder(world.Placement.Placements, buildings.transform, objectHook);
            _poisPerFrame = Mathf.Max(1, settings.PoisPerFrame);

            var props = new GameObject("Props");
            props.transform.SetParent(_root.transform, false);
            float cull = settings.Props == null ? 0f : settings.Props.CullScreenHeight;
            _props = new PropInstanceBuilder(world.Placement.Props, props.transform, _terrain.transform, cull, settings.Voxels.ChunkMetres, objectHook);
            _terrain.GroundChanged += Reground;
            _propsPerFrame = Mathf.Max(1, settings.PropsPerFrame);
            _cellSize = settings.Voxels.ChunkMetres;
            _propRegroundBudget = settings.PropRegroundBudget;

            _root.SetActive(true);
            world.ReleaseHeights();

            if (!_terrain.Initialized)
                throw new InvalidOperationException("The voxel terrain could not start loading.");

            _terrain.ValidateBudget(settings.MemoryBudget);
        }
        catch
        {
            _root.SetActive(false);
            GeneratedMesh.Destroy(_root);
            throw;
        }
    }

    public void Tick()
    {
        if (_terrain.Ready && !_pois.Ready)
            _pois.BuildNext(_poisPerFrame);
        else if (_terrain.Ready && !_props.Ready)
            _props.BuildNext(_propsPerFrame);

        _props.RegroundStep(_propRegroundBudget);
        NotifyGroundChanged();

        float progress = _terrain.Ready ? 0.85f + (_pois.Progress + _props.Progress) * 0.075f : Mathf.Min(0.99f, _terrain.Progress) * 0.85f;
        _progress = Mathf.Max(_progress, progress);
    }

    private void Reground(IReadOnlyList<Vector2Int> cells)
    {
        _props.QueueReground(cells);

        foreach (Vector2Int cell in cells)
        {
            if (_groundChangeSet.Add(cell))
                _groundChanges.Add(cell);
        }
    }

    private void NotifyGroundChanged()
    {
        if (_groundChanges.Count == 0 || _terrain.Recentering || _props.RegroundPending > 0)
            return;

        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.PropReground);
        Physics.SyncTransforms();
        GroundChanged?.Invoke(_groundChanges, _cellSize);
        _groundChanges.Clear();
        _groundChangeSet.Clear();
    }

    public void Dispose()
    {
        if (_terrain != null)
            _terrain.GroundChanged -= Reground;

        if (_root == null)
            return;

        _root.SetActive(false);
        GeneratedMesh.Destroy(_root);
    }
}

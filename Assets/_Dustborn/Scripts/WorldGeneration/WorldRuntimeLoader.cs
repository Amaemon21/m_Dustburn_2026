using System;
using UnityEngine;

public sealed class WorldRuntimeLoader : IDisposable
{
    private readonly GameObject _root;
    private readonly VoxelTerrainStreamer _terrain;
    private readonly PoiInstanceBuilder _pois;
    private readonly int _poisPerFrame;
    private float _progress;

    public bool Ready => _terrain.Ready && _pois.Ready;
    public float Progress => Ready ? 1f : _progress;
    public string Stage => _terrain.Ready ? "Размещение зданий" : "Загрузка ландшафта и растительности";

    public WorldRuntimeLoader(WorldBuildSettings settings, BakedWorld world, Transform viewer, Transform parent)
    {
        if (settings == null || settings.Voxels == null || world == null || !world.IsValid || viewer == null)
            throw new InvalidOperationException("World loading requires a complete bake, voxel settings and a viewer.");

        WorldBuildBudget.Validate(settings, world.Config, new Vector2(viewer.position.x, viewer.position.z), true);

        _root = new GameObject("World Runtime");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);

        try
        {
            _terrain = _root.AddComponent<VoxelTerrainStreamer>();
            _terrain.Configure(settings, world, viewer);
            _terrain.hideFlags = HideFlags.HideInInspector;

            var buildings = new GameObject("POI");
            buildings.transform.SetParent(_root.transform, false);
            _pois = new PoiInstanceBuilder(world.Placement.Placements, buildings.transform);
            _poisPerFrame = Mathf.Max(1, settings.PoisPerFrame);

            _root.SetActive(true);

            if (!_terrain.Initialized)
                throw new InvalidOperationException("The voxel terrain could not start loading.");
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

        float progress = _terrain.Ready ? 0.85f + _pois.Progress * 0.15f : Mathf.Min(0.99f, _terrain.Progress) * 0.85f;
        _progress = Mathf.Max(_progress, progress);
    }

    public void Dispose()
    {
        if (_root == null)
            return;

        _root.SetActive(false);
        GeneratedMesh.Destroy(_root);
    }
}

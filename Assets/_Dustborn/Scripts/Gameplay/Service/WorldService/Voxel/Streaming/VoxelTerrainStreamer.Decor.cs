using System;
using System.Collections.Generic;
using UnityEngine;

public partial class VoxelTerrainStreamer
{
    private class DecorRing
    {
        public DecorScope Scope { get; }
        public string Label { get; }
        public float Reach { get; }

        public Dictionary<Vector2Int, GameObject> Patches { get; } = new();
        public HashSet<Vector2Int> Stale { get; } = new();

        public Vector2 Sown { get; set; }
        public bool Planted { get; set; }

        public DecorRing(DecorScope scope, float reach)
        {
            Scope = scope;
            Label = scope.ToString();
            Reach = reach;
        }
    }

    private const int REGROW_PER_FRAME = 24;

    private float DecorFrameBudget => _preload && !_ready ? _preloadBudget : _decorBudget;

    private readonly Dictionary<GameObject, GameObject> _replacing = new();
    private readonly Queue<(DecorRing Ring, Vector2Int Cell)> _regrow = new();
    private readonly List<VoxelDecorBuild> _sown = new();

    private int Patches()
    {
        int patches = 0;

        foreach (DecorRing ring in _rings)
            patches += ring.Patches.Count;

        return patches;
    }

    private VoxelDecorSpawner CreateDecor(WaterMap water)
    {
        if (!_spawnDecor || _biomes == null || !_biomes.IsValid() || _biomeMap == null)
            return null;

        BiomeMap biomeMap = BiomeMapTexture.Read(_biomeMap, _biomes, _config.WorldSize);

        if (biomeMap == null)
            return null;

        var weights = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendRadius);

        float[] roadMask = _roadMask == null ? null : MaskTexture.Read(_roadMask);
        int roadResolution = roadMask == null ? 0 : _roadMask.width;

        var filter = new DecorFilter(_config, weights, _biomes.Count, roadMask, roadResolution,
            _poiPlacement == null ? null : _poiPlacement.Placements, WidestFootprint());

        filter.Water = water;

        var placer = new VoxelDecorPlacer(_config, _biomes, _field, filter, _grassDensity);

        placer.Edits = DecorEditIndex.For(_decorEdits, _config.Seed, placer.Layers);

        if (placer.Layers.Count == 0)
            return null;

        var spawner = new VoxelDecorSpawner(placer, _voxels, _batchVertexBudget, false) { Hook = DecorHook };
        spawner.EnablePooling(transform);
        return spawner;
    }

    private float WidestFootprint()
    {
        float widest = 0f;

        for (int biome = 0; biome < _biomes.Count; biome++)
        {
            BiomeDefinition definition = _biomes.Get(biome);

            foreach (ScatterLayer layer in definition.Trees)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.EffectiveFootprint);

            foreach (ScatterLayer layer in definition.Rocks)
                widest = Mathf.Max(widest, layer == null ? 0f : layer.Footprint);
        }

        return widest;
    }

    private void Demolish(GameObject patch)
    {
        if (patch == null)
            return;

        if (_replacing.Remove(patch, out GameObject replaced))
            Demolish(replaced);

        foreach (VoxelDecorBuild build in _decorQueue)
        {
            if (build.Owns(patch))
                build.Done = true;
        }

        patch.SetActive(false);

        _demolishing.Enqueue(new VoxelDecorDemolition(patch));
    }

    private void Demolish()
    {
        int budget = _demolishPerFrame;
        double deadline = Math.Min(DecorFrameBudget, _decorClock.Elapsed.TotalMilliseconds + DecorFrameBudget * 0.25);

        while (budget > 0 && _demolishing.Count > 0 && _decorClock.Elapsed.TotalMilliseconds < deadline)
        {
            VoxelDecorDemolition demolition = _demolishing.Peek();
            GameObject patch = demolition.Root;

            if (patch == null)
            {
                _demolishing.Dequeue();

                continue;
            }

            while (budget > 0 && !demolition.Complete && _decorClock.Elapsed.TotalMilliseconds < deadline)
            {
                if (!demolition.TryTake(out GameObject child))
                    break;

                if (_decor == null || !_decor.Recycle(child))
                    GeneratedMesh.Destroy(child);

                budget--;
            }

            if (!demolition.Complete)
                break;

            _demolishing.Dequeue();

            GeneratedMesh.Destroy(patch);
            budget--;
        }
    }

    private void Decorate()
    {
        if (_decor == null || _decorQueue.Count == 0)
            return;

        while (_decorQueue.Count > 0 && _decorClock.Elapsed.TotalMilliseconds < DecorFrameBudget)
        {
            VoxelDecorBuild build = _decorQueue.Peek();

            if (build.Preparing)
                break;

            if (!build.Done)
                _decor.Step(build, DECOR_SLICE);

            if (build.Done)
            {
                _decorQueue.Dequeue();
                build.ReleaseMesh();
                Reveal(build);
            }
        }
    }

    private void Sow(Vector2 ground)
    {
        if (_decor == null || !_planned)
            return;

        foreach (DecorRing ring in _rings)
            Sow(ring, ground);

        Regrow();

        _decor.PrepareInBackground(_sown);
        _sown.Clear();
    }

    private void Regrow()
    {
        float size = _plan.ChunkMetres(0);

        for (int grown = 0; grown < REGROW_PER_FRAME && _regrow.Count > 0; grown++)
        {
            (DecorRing ring, Vector2Int cell) = _regrow.Dequeue();

            ring.Stale.Remove(cell);

            if (!ring.Patches.TryGetValue(cell, out GameObject patch))
                continue;

            ring.Patches.Remove(cell);
            Grow(ring, cell, size, patch);
        }
    }

    private void ForgetRegrowth()
    {
        _regrow.Clear();
        _sown.Clear();

        foreach (DecorRing ring in _rings)
            ring.Stale.Clear();
    }

    private void Sow(DecorRing ring, Vector2 ground)
    {
        if (ring.Planted && DistanceUtility.WithinRadius(ground, ring.Sown, _decorStep))
            return;

        ring.Sown = ground;
        ring.Planted = true;

        float size = _plan.ChunkMetres(0);

        _faded.Clear();

        foreach (KeyValuePair<Vector2Int, GameObject> patch in ring.Patches)
        {
            if (SqrRange(patch.Key, size, ground) > (ring.Reach + size) * (ring.Reach + size))
                _faded.Add(patch.Key);
        }

        foreach (Vector2Int cell in _faded)
        {
            Demolish(ring.Patches[cell]);

            ring.Patches.Remove(cell);
        }

        int minX = Mathf.FloorToInt((ground.x - ring.Reach) / size);
        int maxX = Mathf.FloorToInt((ground.x + ring.Reach) / size);
        int minZ = Mathf.FloorToInt((ground.y - ring.Reach) / size);
        int maxZ = Mathf.FloorToInt((ground.y + ring.Reach) / size);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var cell = new Vector2Int(x, z);

                if (ring.Patches.ContainsKey(cell))
                    continue;

                if (SqrRange(cell, size, ground) > ring.Reach * ring.Reach)
                    continue;

                Grow(ring, cell, size);
            }
        }
    }

    private void Reveal(VoxelDecorBuild build)
    {
        if (build.IsOrphaned || !_replacing.Remove(build.Parent.gameObject, out GameObject replaced))
            return;

        build.Parent.gameObject.SetActive(true);

        Demolish(replaced);
    }

    private void Grow(DecorRing ring, Vector2Int cell, float size, GameObject replaced = null)
    {
        if (!_cover.TryGetValue(cell, out VoxelColumnKey column))
        {
            Demolish(replaced);
            return;
        }

        var patch = new GameObject($"{ring.Label} {cell.x.ToString()} {cell.y.ToString()}");

        patch.transform.SetParent(transform, false);

        if (replaced != null)
        {
            patch.SetActive(false);
            _replacing[patch] = replaced;
        }

        ring.Patches[cell] = patch;

        var build = new VoxelDecorBuild(new Vector2(cell.x * size, cell.y * size), size, patch.transform,
            column.Lod, Frame(column), ring.Scope);

        _sown.Add(build);
        _decorQueue.Enqueue(build);
    }

    private void StopPreparing()
    {
        foreach (VoxelDecorBuild build in _decorQueue)
        {
            build.Done = true;

            try
            {
                build.Preparation?.Wait();
            }
            catch (AggregateException)
            {
            }

            build.ReleaseMesh();
        }
    }

    private void Rings()
    {
        _rings.Clear();

        if (_decor == null)
            return;

        _rings.Add(new DecorRing(DecorScope.Grass, _voxels.GrassDistance <= 0f ? _plan.Distance(0) : _voxels.GrassDistance));
        _rings.Add(new DecorRing(DecorScope.Rocks, _plan.Distance(Mathf.Min(_voxels.RockMaxLod, _plan.LodCount - 1))));
        _rings.Add(new DecorRing(DecorScope.Trees, _plan.Distance(Mathf.Min(_voxels.TreeMaxLod, _plan.LodCount - 1))));
    }

    private DecorSurface Frame(VoxelColumnKey column)
    {
        float span = _plan.ChunkMetres(column.Lod);

        return new DecorSurface(new Vector2(column.X * span, column.Z * span), _voxels.ChunkSize, _plan.VoxelSize(column.Lod), column.Morph);
    }
}

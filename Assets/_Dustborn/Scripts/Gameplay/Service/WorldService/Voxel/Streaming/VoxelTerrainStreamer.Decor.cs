using System.Collections.Generic;
using UnityEngine;

public partial class VoxelTerrainStreamer
{
    private class DecorRing
    {
        public DecorScope Scope { get; }
        public float Reach { get; }

        public Dictionary<Vector2Int, GameObject> Patches { get; } = new();

        public Vector2 Sown { get; set; }
        public bool Planted { get; set; }

        public DecorRing(DecorScope scope, float reach)
        {
            Scope = scope;
            Reach = reach;
        }
    }

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

        return placer.Layers.Count == 0 ? null : new VoxelDecorSpawner(placer, _voxels, _batchVertexBudget, false) { Hook = DecorHook };
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

        foreach (VoxelDecorBuild build in _decorQueue)
        {
            if (build.Owns(patch))
                build.Done = true;
        }

        patch.SetActive(false);

        _demolishing.Enqueue(patch);
    }

    private void Demolish()
    {
        int budget = _demolishPerFrame;

        while (budget > 0 && _demolishing.Count > 0)
        {
            GameObject patch = _demolishing.Peek();

            if (patch == null)
            {
                _demolishing.Dequeue();

                continue;
            }

            Transform root = patch.transform;

            while (budget > 0 && root.childCount > 0)
            {
                Transform child = root.GetChild(root.childCount - 1);

                child.SetParent(null, false);

                GeneratedMesh.Destroy(child.gameObject);

                budget--;
            }

            if (root.childCount > 0)
                break;

            _demolishing.Dequeue();

            GeneratedMesh.Destroy(patch);
        }
    }

    private void Decorate()
    {
        if (_decor == null || _decorQueue.Count == 0)
            return;

        float budget = _preload && !_ready ? _preloadBudget : _decorBudget;

        _clock.Restart();

        while (_decorQueue.Count > 0)
        {
            VoxelDecorBuild build = _decorQueue.Peek();

            _decor.Step(build, DECOR_SLICE);

            if (build.Done)
                _decorQueue.Dequeue();

            if (_clock.Elapsed.TotalMilliseconds >= budget)
                break;
        }
    }

    private void Sow(Vector2 ground)
    {
        if (_decor == null || !_planned)
            return;

        foreach (DecorRing ring in _rings)
            Sow(ring, ground);
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

    private void Grow(DecorRing ring, Vector2Int cell, float size)
    {
        if (!_cover.TryGetValue(cell, out VoxelColumnKey column))
            return;

        var patch = new GameObject($"{ring.Scope} {cell.x} {cell.y}");

        patch.transform.SetParent(transform, false);

        ring.Patches[cell] = patch;

        _decorQueue.Enqueue(new VoxelDecorBuild(new Vector2(cell.x * size, cell.y * size), size, patch.transform,
            column.Lod, Frame(column), ring.Scope));
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

        return new DecorSurface(new Vector2(column.X * span, column.Z * span),
            VoxelDensitySampler.MorphSpan(_voxels.ChunkSize, _plan.VoxelSize(column.Lod)),
            _plan.VoxelSize(column.Lod), column.Morph);
    }
}

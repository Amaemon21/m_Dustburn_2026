using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class VoxelTerrainBuilder
{
    private string SpawnDecor(VoxelDensityField field, VoxelStreamPlan plan, System.Action<string, float> progress)
    {
        VoxelDecorPlacer placer = CreatePlacer(field, out string problem);

        if (placer == null)
            return problem;

        var spawner = new VoxelDecorSpawner(placer, _voxels, _batchVertexBudget, _decorColliders) { FarDecor = _decorEverywhere };
        var clock = Stopwatch.StartNew();

        _decor = spawner;
        var builds = new List<VoxelDecorBuild>(_columns.Count);

        foreach (VoxelColumnKey key in _columns)
        {
            float size = plan.ChunkMetres(key.Lod);

            if (!_roots.TryGetValue(key, out Transform column))
                continue;

            var bucket = new GameObject($"Decor {key}");

            bucket.transform.SetParent(column, false);

            var origin = new Vector2(key.X * size, key.Z * size);
            var frame = new DecorSurface(origin, _voxels.ChunkSize, plan.VoxelSize(key.Lod), key.Morph);

            bucket.AddComponent<VoxelDecorBucket>().Setup(origin, size, key.Lod, frame);

            builds.Add(new VoxelDecorBuild(origin, size, bucket.transform, key.Lod, frame));
        }

        progress?.Invoke("Трава, деревья и камни", 0.7f);
        spawner.Prepare(builds);

        for (int i = 0; i < builds.Count; i++)
        {
            progress?.Invoke("Трава, деревья и камни", 0.7f + 0.3f * i / Mathf.Max(1, builds.Count));

            spawner.Spawn(builds[i]);
        }

        clock.Stop();

        Debug.Log($"Decor layers of the voxel terrain:\n{spawner.Describe()}", this);

        return $"decor: {spawner.Placed} placed, {spawner.Combined} combined into {spawner.Batches} batches, "
            + $"{spawner.Instanced} instanced, {spawner.Instantiated} left as objects, {clock.ElapsedMilliseconds} ms";
    }

    private VoxelDecorPlacer CreatePlacer(VoxelDensityField field, out string problem)
    {
        problem = null;

        if (!_spawnDecor)
        {
            problem = "no decor";
            return null;
        }

        if (_biomes == null || !_biomes.IsValid() || _biomeMap == null)
        {
            problem = "no decor: assign the BiomeDatabase and the BiomeMap texture";
            return null;
        }

        BiomeMap biomeMap = BiomeMapTexture.Read(_biomeMap, _biomes, _config.WorldSize);

        if (biomeMap == null)
        {
            problem = "no decor";
            return null;
        }

        if (_poiPlacement == null)
            Debug.LogWarning("Grass and trees will grow through the houses: PoiPlacement.asset is not assigned", this);

        var weightField = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendRadius);

        float[] roadMask = _roadMask == null ? null : MaskTexture.Read(_roadMask);
        int roadMaskResolution = roadMask == null ? 0 : _roadMask.width;

        var filter = new DecorFilter(_config, weightField, _biomes.Count, roadMask, roadMaskResolution,
            _poiPlacement == null ? null : _poiPlacement.Placements, WidestFootprint());

        filter.Water = _waterMap ??= WaterMapFormat.Load(_water);

        var placer = new VoxelDecorPlacer(_config, _biomes, field, filter, _grassDensity);

        if (placer.Layers.Count == 0)
        {
            problem = "no decor: no biome has trees, rocks or grass with a prefab";
            return null;
        }

        placer.Edits = DecorEditIndex.For(_decorEdits, _config.Seed, placer.Layers);

        return placer;
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

    private string PaintSplatmap(HeightMap map)
    {
        if (!_paintSplatmap)
            return "no splatmap";

        var request = new VoxelGroundMaterial.Request
        {
            Config = _config,
            Biomes = _biomes,
            BiomeMap = _biomeMap,
            Roads = _roads == null ? null : _roads.Paved(),
            Map = map,
            ControlResolution = _controlResolution,
            UseRepetitionless = _useRepetitionless,
            MaterialPath = _repetitionlessMaterialPath,
            Material = _material
        };

        string status = VoxelGroundMaterial.Bake(request, this);

        _material = request.Material;

        foreach (MeshFilter chunk in _chunks)
        {
            if (chunk != null && chunk.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = _material;
        }

        return status;
    }
}

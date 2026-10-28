using UnityEngine;

public partial class VoxelTerrainBuilder
{
    public DecorEditsAsset DecorEdits => _decorEdits;

    public void UseDecorEdits(DecorEditsAsset edits)
    {
        _decorEdits = edits;
    }

    public DecorEditSession BeginDecorEdit(out string problem)
    {
        problem = null;

        if (!Validate())
        {
            problem = "the preview is missing its config, voxel settings or height map";
            return null;
        }

        HeightMap map = HeightMap.FromRaw16(_heightMap.bytes, _config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);
        var field = new VoxelDensityField(map, _voxels, Lowest(map));

        VoxelDecorPlacer placer = CreatePlacer(field, out problem);

        if (placer != null)
            return new DecorEditSession(field, placer, _config.Seed);

        field.Dispose();

        return null;
    }

    public int RefreshDecor(DecorEditSession session, Vector2 min, Vector2 max)
    {
        if (_decor == null)
            _decor = new VoxelDecorSpawner(session.Placer, _voxels, _batchVertexBudget, _decorColliders) { FarDecor = _decorEverywhere };
        else if (!_decor.Use(session.Placer))
            return -1;

        int rebuilt = 0;

        foreach (VoxelDecorBucket bucket in GetComponentsInChildren<VoxelDecorBucket>(true))
        {
            if (!bucket.Overlaps(min, max))
                continue;

            Transform root = bucket.transform;

            for (int i = root.childCount - 1; i >= 0; i--)
                GeneratedMesh.Destroy(root.GetChild(i).gameObject);

            _decor.Spawn(new VoxelDecorBuild(bucket.Origin, bucket.Span, root, bucket.Lod, bucket.Frame));

            rebuilt++;
        }

        return rebuilt;
    }
}

using System;
using UnityEditor;
using UnityEngine;

public static class WorldSurfaceBake
{
    private const string SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";

    [MenuItem("Мир/Перепечь землю мира")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Rebake the surface outside play mode.");

        WorldBuildSettings settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(SETTINGS_PATH);

        if (settings == null || settings.Biomes == null || settings.Config == null)
            throw new InvalidOperationException("Assign generation settings and biomes before baking the surface.");

        BakedWorld world = AssetDatabase.LoadAssetAtPath<BakedWorld>(settings.BakedWorldPath);

        if (world == null || !world.IsValid)
            throw new InvalidOperationException("Bake a complete world before rebuilding its surface.");

        var request = new VoxelGroundMaterial.Request
        {
            Config = world.Config,
            Biomes = settings.Biomes,
            BiomeMap = world.BiomeMap,
            Roads = world.Roads.Paved(),
            Map = HeightMap.FromRaw16(world.HeightMap.bytes, world.Config.HeightMapResolution,
                world.Config.WorldSize, world.Config.MaxHeight),
            ControlResolution = settings.ControlResolution,
            MaterialPath = settings.MaterialPath,
            Material = world.Material,
            UseRepetitionless = true
        };

        string result = VoxelGroundMaterial.Bake(request, world);

        if (request.Material == null || !result.StartsWith("splatmap:", StringComparison.Ordinal))
            throw new InvalidOperationException($"The surface could not be rebuilt: {result}");

        world.EditorSetup(world.Config, settings.Biomes, world.HeightMap, world.BiomeMap,
            world.RoadMask, world.Roads, world.Placement, request.Material);
        EditorUtility.SetDirty(world);
        AssetDatabase.SaveAssetIfDirty(world);
        SceneView.RepaintAll();
        Debug.Log($"World surface rebuilt: {result}", world);
    }
}

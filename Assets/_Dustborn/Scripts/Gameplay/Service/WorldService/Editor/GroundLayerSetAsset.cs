using System;
using UnityEditor;
using UnityEngine;

public static class GroundLayerSetAsset
{
    public static void Save(WorldBuildSettings settings, VoxelGroundMaterial.Request request)
    {
        if (request.Material == null || request.Layers == null)
            throw new InvalidOperationException("The ground was baked without a material or a layer list to record");

        string path = settings.GroundLayersPath;
        var set = AssetDatabase.LoadAssetAtPath<GroundLayerSet>(path);

        if (set == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException($"An incompatible asset already exists at {path}.");

            set = ScriptableObject.CreateInstance<GroundLayerSet>();
            AssetDatabase.CreateAsset(set, path);
        }

        set.EditorSetup(request.Material, request.Layers, request.ControlResolution);
        EditorUtility.SetDirty(set);

        if (settings.Ground != set)
        {
            settings.EditorSetGround(set);
            EditorUtility.SetDirty(settings);
        }

        AssetDatabase.SaveAssetIfDirty(set);
        AssetDatabase.SaveAssetIfDirty(settings);
    }
}

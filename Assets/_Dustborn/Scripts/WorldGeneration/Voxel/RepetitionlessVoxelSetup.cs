#if UNITY_EDITOR
using System.IO;
using Repetitionless.Editor.Data;
using Repetitionless.Editor.Materials;
using UnityEditor;
using UnityEngine;

public static class RepetitionlessVoxelSetup
{
    private const string TERRAIN_DATA_ASSET = "TerrainData.asset";
    private const string PROPERTIES_ASSET = "Properties.asset";

    private const string LAYERED_LIT = "RepetitionlessLayeredLit";

    private const float WORLD_UV_SCALE = 1000f;

    public static Material Apply(TerrainLayer[] layers, string materialPath)
    {
        if (layers == null || layers.Length == 0)
            return null;

        Material material = LoadOrCreateMaterial(materialPath);

        if (material == null)
            return null;

        return SyncLayers(material, layers) ? material : null;
    }

    private static Material LoadOrCreateMaterial(string materialPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (existing != null)
            return SwitchToLayeredLit(existing) ? existing : null;

        string folder = Path.GetDirectoryName(materialPath).Replace('\\', '/');

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        MaterialDataObjects created = RepetitionlessMaterialCreator.CreateTerrainMaterial(folder, Path.GetFileName(materialPath), ping: false);

        if (created.Material == null)
        {
            Debug.LogError($"Repetitionless did not create a material at {materialPath}: no layered shader was found for the current render pipeline");
            return null;
        }

        if (!SwitchToLayeredLit(created.Material))
            return null;

        return created.Material;
    }

    private static bool SwitchToLayeredLit(Material material)
    {
        if (material.shader != null && material.shader.name.EndsWith(LAYERED_LIT))
            return true;

        string[] parts = material.shader.name.Split('/');

        if (parts.Length < 2)
        {
            Debug.LogError($"Unexpected Repetitionless shader name {material.shader.name}, cannot pick the layered lit variant", material);
            return false;
        }

        string wanted = $"Repetitionless/{parts[1]}/{LAYERED_LIT}";
        Shader shader = Shader.Find(wanted);

        if (shader == null)
        {
            Debug.LogError($"Shader {wanted} was not found. In the Repetitionless package it may still sit in the hidden Shaders/URP~ folder: open the package settings once so it merges into Shaders/URP", material);
            return false;
        }

        material.shader = shader;

        return true;
    }

    private static bool SyncLayers(Material material, TerrainLayer[] layers)
    {
        var manager = new MaterialDataManager(material);
        var terrainData = manager.LoadAsset<RepetitionlessTerrainDataSO>(TERRAIN_DATA_ASSET);

        if (terrainData == null)
        {
            Debug.LogError($"There is no {TERRAIN_DATA_ASSET} next to {material.name}: the material was not made through Repetitionless. Delete it and build the voxel terrain again", material);
            return false;
        }

        var properties = manager.LoadAsset<RepetitionlessMaterialDataSO>(PROPERTIES_ASSET);

        int capacity = properties != null ? properties.Data.Length : layers.Length;

        if (layers.Length > capacity)
            Debug.LogWarning($"There are {layers.Length} ground layers but material {material.name} holds {capacity}: the rest will render white. Lower MaxTerrainLayers to {capacity}, or delete {material.name} together with its *_RepetitionlessData folder so a fresh material picks up the capacity of the installed Repetitionless edition", material);

        terrainData.UpdateTerrainLayers(layers);

        for (int i = 0; i < layers.Length && i < capacity; i++)
            terrainData.UpdateLayerMaterialData(i, forceUpdate: true);

        ApplyWorldTiling(properties, material, layers, capacity);

        terrainData.AutoSyncLayers = false;

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        return true;
    }

    private static void ApplyWorldTiling(RepetitionlessMaterialDataSO properties, Material material, TerrainLayer[] layers, int capacity)
    {
        if (properties == null)
        {
            Debug.LogWarning($"There is no {PROPERTIES_ASSET} next to {material.name}: layer tiling stays inverted and the textures stretch over hundreds of metres", material);
            return;
        }

        int count = Mathf.Min(layers.Length, capacity);

        for (int i = 0; i < count; i++)
        {
            Vector2 size = layers[i].tileSize;
            Vector2 offset = layers[i].tileOffset;

            float x = size.x > 0f ? WORLD_UV_SCALE / size.x : 1f;
            float y = size.y > 0f ? WORLD_UV_SCALE / size.y : 1f;

            properties.Data[i].BaseMaterialData.TilingOffset = new Vector4(x, y, offset.x, offset.y);

            properties.UpdateMaterialTexture(material, i);
        }

        properties.Save();
    }
}
#endif

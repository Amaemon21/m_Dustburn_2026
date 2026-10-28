using System;
using Repetitionless.Editor.Data;
using Repetitionless.Editor.Materials;
using Repetitionless.Runtime;
using Repetitionless.Runtime.Variables;
using UnityEditor;
using UnityEngine;

public static class MainMenuTerrainMaterial
{
    public static Material Create(string folder, TerrainData data)
    {
        TerrainLayer[] layers = data.terrainLayers;
        MaterialDataObjects created = RepetitionlessMaterialCreator.CreateTerrainMaterial(folder, "MainMenuGround.mat", ping: false);
        Material material = created.Material;
        if (material == null || material.shader == null || !material.shader.name.EndsWith("/RepetitionlessLayeredTerrain", StringComparison.Ordinal))
            throw new InvalidOperationException("Repetitionless could not create a terrain material for the active render pipeline.");

        var manager = new MaterialDataManager(material);
        var terrain = manager.LoadAsset<RepetitionlessTerrainDataSO>("TerrainData.asset");
        var properties = manager.LoadAsset<RepetitionlessMaterialDataSO>("Properties.asset");
        if (terrain == null || properties == null || properties.Data.Length < layers.Length)
            throw new InvalidOperationException("The installed Repetitionless edition cannot hold the selected terrain layers.");

        terrain.UpdateTerrainLayers(layers);
        for (int i = 0; i < layers.Length; i++)
        {
            MainMenuTerrainGenerator.Progress("Packing Repetitionless layers", 0.35f + 0.2f * i / layers.Length);
            terrain.UpdateLayerMaterialData(i, forceUpdate: true);
            Vector2 size = layers[i].tileSize;
            Vector2 offset = layers[i].tileOffset;
            properties.Data[i].BaseMaterialData.NoiseEnabled = true;
            properties.Data[i].BaseMaterialData.RandomiseNoiseScaling = true;
            properties.Data[i].BaseMaterialData.RandomiseNoiseRotation = true;
            properties.Data[i].BaseMaterialData.TilingOffset = new Vector4(1000f / size.x, 1000f / size.y, offset.x / size.x, offset.y / size.y);
            properties.Data[i].BaseMaterialData.NoiseScale = Mathf.Max(size.x, size.y) / 24f;
            properties.UpdateMaterialTexture(material, i);
        }

        terrain.AutoSyncLayers = false;
        terrain.Save();
        properties.SetNoiseQuality(ENoiseQuality.High);
        properties.Save();
        material.enableInstancing = true;
        FinalizeMaterial(material, data);
        EditorUtility.SetDirty(material);
        EditorApplication.delayCall += () =>
        {
            if (material == null || data == null || !AssetDatabase.Contains(material))
                return;
            FinalizeMaterial(material, data);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        };
        return material;
    }

    public static void Attach(Terrain terrain, Material material)
    {
        if (!terrain.TryGetComponent(out RepetitionlessTerrain repetitionless))
            repetitionless = Undo.AddComponent<RepetitionlessTerrain>(terrain.gameObject);
        else
            Undo.RecordObject(repetitionless, "Configure Main Menu Repetitionless");
        if (repetitionless.ParentTerrain != null)
            repetitionless.UpdateParentCallback(null);
        repetitionless.ParentTerrain = null;
        repetitionless.enabled = true;
        repetitionless.AutoSaveTextures = false;
        FinalizeMaterial(material, terrain.terrainData);
        repetitionless.UpdateTerrainMaterial(material);
        repetitionless.UpdateLayersCount();
        repetitionless.UpdateControlTextures();
        FinalizeMaterial(repetitionless.MaterialInstance, terrain.terrainData);
        EditorUtility.SetDirty(repetitionless);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
    }

    private static void FinalizeMaterial(Material material, TerrainData data)
    {
        material.DisableKeyword("_REPETITIONLESS_NOISE_TEXTURE");
        material.SetInt("_REPETITIONLESS_NOISE_TEXTURE", 0);
        material.SetTexture("_NoiseTexture", null);
        BindTerrain(material, data);
    }

    private static void BindTerrain(Material material, TerrainData data)
    {
        int capacity = (data.alphamapLayers + 3) / 4 * 4;
        for (int count = 4; count <= 32; count += 4)
        {
            string keyword = $"_MAX_LAYERS_{count}";
            if (count == capacity)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        Texture2D[] controls = data.alphamapTextures;
        if (controls.Length != capacity / 4)
            throw new InvalidOperationException("Terrain control texture count does not match its paint layers.");
        material.SetFloat("_LayersCount", data.alphamapLayers);
        for (int i = 0; i < 8; i++)
            material.SetTexture($"_Control{i}", i < controls.Length ? controls[i] : null);
        material.SetTexture("_TerrainHolesTexture", data.holesTexture);
        for (int i = 0; i < controls.Length; i++)
        {
            if (controls[i] == null || material.GetTexture($"_Control{i}") != controls[i])
                throw new InvalidOperationException($"Repetitionless is missing terrain control texture {i}.");
        }
        if (!material.IsKeywordEnabled($"_MAX_LAYERS_{capacity}"))
            throw new InvalidOperationException("Repetitionless did not enable the required terrain layer shader variant.");
    }
}

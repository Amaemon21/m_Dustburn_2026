using System;
using UnityEditor;
using UnityEngine;

public static class MainMenuTerrainPaint
{
    public static TerrainData ApplyAndSave(TerrainData data, float[,,] weights)
    {
        string path = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("Create the TerrainData asset before painting it.");
        MainMenuTerrainGenerator.Progress("Applying and saving terrain paint", 0.95f);
        data.SetAlphamaps(0, 0, weights);
        data.DirtyTextureRegion(TerrainData.AlphamapTextureName, new RectInt(0, 0, data.alphamapWidth, data.alphamapHeight), false);
        data.SyncTexture(TerrainData.AlphamapTextureName);
        foreach (Texture2D control in data.alphamapTextures)
            EditorUtility.SetDirty(control);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        TerrainData saved = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (saved == null)
            throw new InvalidOperationException("The painted TerrainData could not be reloaded.");
        ValidateSavedPaint(saved, weights);
        return saved;
    }

    private static void ValidateSavedPaint(TerrainData data, float[,,] expected)
    {
        if (data.alphamapLayers != expected.GetLength(2) || data.alphamapWidth != expected.GetLength(1) || data.alphamapHeight != expected.GetLength(0))
            throw new InvalidOperationException("Saved terrain paint dimensions do not match the generated map.");
        float[,,] actual = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        int step = Mathf.Max(1, data.alphamapWidth / 64);
        for (int z = 0; z < data.alphamapHeight; z += step)
        for (int x = 0; x < data.alphamapWidth; x += step)
        for (int layer = 0; layer < data.alphamapLayers; layer++)
        {
            if (Mathf.Abs(actual[z, x, layer] - expected[z, x, layer]) > 2f / 255f)
                throw new InvalidOperationException($"Terrain paint did not survive saving at ({x}, {z}), layer {layer}. The terrain was not placed in the scene.");
        }
    }
}

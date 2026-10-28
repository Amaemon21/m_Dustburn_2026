using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MainMenuTerrainRepair
{
    private const string MENU = "Tools/Dustborn/Repair Selected Main Menu Terrain";

    [MenuItem(MENU, true)]
    private static bool CanRepairSelected()
    {
        return IsValid(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Terrain>() : null);
    }

    [MenuItem(MENU)]
    private static void RepairSelected()
    {
        Repair(Selection.activeGameObject.GetComponent<Terrain>());
    }

    public static bool IsValid(Terrain terrain)
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode && terrain != null && terrain.terrainData != null && terrain.gameObject.scene.path == MainMenuTerrainGenerator.SCENE_PATH;
    }

    public static void Repair(Terrain terrain, Camera camera = null, float roadWidth = 6f)
    {
        if (!IsValid(terrain))
            return;
        string folder = null;
        int undoGroup = -1;
        bool complete = false;
        try
        {
            ValidateLayers(terrain.terrainData.terrainLayers);
            camera = camera != null ? camera : FindCamera(terrain);
            if (camera == null || camera.gameObject.scene != terrain.gameObject.scene)
                throw new InvalidOperationException("A composition camera from MainMenu is required to restore the road.");
            Vector3 forward = camera.transform.forward;
            if (forward.x * forward.x + forward.z * forward.z < 0.001f)
                throw new InvalidOperationException("The composition camera must face toward the horizon.");
            TerrainData source = terrain.terrainData;
            Vector3 position = camera.transform.position;
            Vector3 origin = terrain.transform.position;
            int seed = ReadSeed(source);
            var layout = new MainMenuTerrainLayout(seed, source.size.x, 36f, roadWidth, new float2(position.x, position.z), new float2(forward.x, forward.z)).WithOrigin(new float2(origin.x, origin.z));
            folder = MainMenuTerrainGenerator.CreateOutputFolder($"Terrain_{seed} Restored");
            TerrainData restored = UnityEngine.Object.Instantiate(source);
            restored.name = source.name;
            AssetDatabase.CreateAsset(restored, folder + "/MainMenuTerrain.asset");
            using var maps = new MainMenuTerrainMaps(source.GetHeights(0, 0, source.heightmapResolution, source.heightmapResolution));
            Material material = MainMenuTerrainMaterial.Create(folder, restored);
            int removed = MainMenuTerrainGrass.RemoveBurnt(restored);
            restored = MainMenuTerrainPaint.ApplyAndSave(restored, maps.Paint(layout, restored.alphamapResolution));
            MainMenuTerrainGenerator.SaveOutput(folder);
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Repair Main Menu Terrain Material");
            Undo.RecordObject(terrain, "Repair Main Menu Terrain Material");
            terrain.terrainData = restored;
            if (terrain.TryGetComponent(out TerrainCollider collider))
            {
                Undo.RecordObject(collider, "Repair Main Menu Terrain Material");
                collider.terrainData = restored;
            }
            MainMenuTerrainMaterial.Attach(terrain, material);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            complete = true;
            Debug.Log($"Main menu road and ground paint restored on {terrain.name}, {removed} burnt grass types removed. Height and remaining grass maps were preserved in a separate TerrainData asset. Save the scene after reviewing the result.", terrain);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Main menu terrain material repair cancelled.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!complete)
            {
                if (undoGroup >= 0)
                    Undo.RevertAllDownToGroup(undoGroup);
                if (folder != null && AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
        }
    }

    private static void ValidateLayers(TerrainLayer[] layers)
    {
        if (layers.Length != MainMenuTerrainLayout.LAYER_COUNT)
            throw new InvalidOperationException("Select a generated Main Menu Terrain with its complete ground layer set.");
        for (int i = 0; i < layers.Length; i++)
        {
            TerrainLayer layer = layers[i];
            if (layer == null || layer.diffuseTexture == null || layer.normalMapTexture == null || layer.tileSize.x <= 0f || layer.tileSize.y <= 0f)
                throw new InvalidOperationException("Every terrain layer needs diffuse and normal textures with positive tiling.");
            string path = AssetDatabase.GetAssetPath(layer);
            if (layer.name != MainMenuTerrainGenerator.LayerNames[i] || !path.StartsWith("Assets/_Dustborn/Content/TerrainLayers/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Only the project Forest, Burnt and Road layers are allowed: {path}");
        }
    }

    private static Camera FindCamera(Terrain terrain)
    {
        foreach (GameObject root in terrain.gameObject.scene.GetRootGameObjects())
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
        {
            if (camera.CompareTag("MainCamera"))
                return camera;
        }
        return null;
    }

    private static int ReadSeed(TerrainData source)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)));
        if (!string.IsNullOrEmpty(folder) && folder.StartsWith("Terrain_", StringComparison.Ordinal))
        {
            string value = folder.Substring("Terrain_".Length).Split(' ')[0];
            if (int.TryParse(value, out int seed))
                return seed;
        }
        return 2026;
    }
}

using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MainMenuTerrainGenerator
{
    public const string SCENE_PATH = "Assets/_Dustborn/Scenes/MainMenu.unity";
    private const string OUTPUT_ROOT = "Assets/_Dustborn/Generated/MainMenu";
    private const string LAYERS_PATH = "Assets/_Dustborn/Content/TerrainLayers/";
    private const string BIOMES_PATH = "Assets/_Dustborn/Content/Configs/World/Biomes/";
    private const string UNDO_NAME = "Generate Main Menu Terrain";
    internal static readonly string[] LayerNames =
    {
        "Forest_Base", "Forest_Moss", "Forest_Shade", "Forest_Soil", "Forest_Stones",
        "Burnt_Base", "Burnt_Ash", "Burnt_Charcoal", "Road_Asphalt", "Road_Dirt", "Road_Verge"
    };

    public static void Generate(Camera camera, int seed, int size, float hills, float roadWidth, float cameraHeight, float grassDensity, bool hidePrevious, float grassSize = 1.2f, float surfaceRelief = 1.2f)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SCENE_PATH || camera == null || camera.gameObject.scene != scene)
            throw new InvalidOperationException("Generation requires an active MainMenu scene and a camera from that scene in edit mode.");
        if ((size != 256 && size != 512 && size != 1024) || hills < 12f || hills > 64f || roadWidth < 4f || roadWidth > 9f || cameraHeight < 1f || cameraHeight > 8f || grassDensity < 0f || grassDensity > 4f || grassSize < 0.5f || grassSize > 2f || surfaceRelief < 0f || surfaceRelief > 3f)
            throw new ArgumentOutOfRangeException(nameof(size), "Terrain settings are outside the supported range.");

        TerrainLayer[] layers = LoadLayers();
        BiomeDefinition forest = LoadBiome("Biome_PineForest");
        Vector3 view = camera.transform.forward;
        if (view.x * view.x + view.z * view.z < 0.001f)
            throw new InvalidOperationException("The composition camera must face toward the horizon.");
        Vector3 position = camera.transform.position;
        var layout = new MainMenuTerrainLayout(seed, size, hills, roadWidth, new float2(position.x, position.z), new float2(view.x, view.z), surfaceRelief);
        string folder = null;
        TerrainData data = null;
        int undoGroup = -1;
        bool complete = false;
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var stageTimer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Progress("Preparing output", 0f);
            folder = CreateOutputFolder($"Terrain_{seed}");

            stageTimer.Restart();
            using var maps = new MainMenuTerrainMaps(layout, size * 2 + 1);
            long heightMs = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            data = BuildTerrain(layout, layers, size, maps, folder);
            long uploadMs = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            Material material = MainMenuTerrainMaterial.Create(folder, data);
            long materialMs = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            int grassCount = MainMenuTerrainGrass.Plant(data, layout, maps, folder, forest, seed, grassDensity, grassSize);
            long grassMs = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            data = MainMenuTerrainPaint.ApplyAndSave(data, maps.Paint(layout, data.alphamapResolution));
            long paintMs = stageTimer.ElapsedMilliseconds;
            SaveOutput(folder);
            Progress("Placing the terrain", 0.98f);

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UNDO_NAME);
            if (hidePrevious)
                HideTerrains(scene);

            var root = new GameObject($"Main Menu Landscape {seed}");
            Undo.RegisterCreatedObjectUndo(root, UNDO_NAME);
            SceneManager.MoveGameObjectToScene(root, scene);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            Undo.RegisterCreatedObjectUndo(terrainObject, UNDO_NAME);
            SceneManager.MoveGameObjectToScene(terrainObject, scene);
            terrainObject.name = "Main Menu Terrain";
            terrainObject.transform.SetParent(root.transform, false);
            terrainObject.transform.position = new Vector3(layout.Origin.x, position.y - cameraHeight - MainMenuTerrainLayout.BASE_HEIGHT, layout.Origin.y);
            Configure(terrainObject.GetComponent<Terrain>(), material);

            var house = new GameObject("House Site (28 x 24 m)");
            Undo.RegisterCreatedObjectUndo(house, UNDO_NAME);
            SceneManager.MoveGameObjectToScene(house, scene);
            house.transform.SetParent(root.transform, false);
            float2 site = layout.FromLocal(layout.HouseCenter);
            house.transform.position = new Vector3(site.x, position.y - cameraHeight, site.y);
            house.transform.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = terrainObject;
            complete = true;
            Debug.Log($"Main menu terrain created: {size} x {size} m, {layers.Length} ground layers, {data.detailPrototypes.Length} grass types, {grassCount} tufts. Assets: {folder}. Save the MainMenu scene after reviewing the result.", terrainObject);
            Debug.Log($"Main menu generation timings: heights {heightMs} ms, Terrain upload {uploadMs} ms, paint and save validation {paintMs} ms, Repetitionless {materialMs} ms, grass and texture imports {grassMs} ms, total {totalTimer.ElapsedMilliseconds} ms.", terrainObject);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!complete)
            {
                if (undoGroup >= 0)
                    Undo.RevertAllDownToGroup(undoGroup);
                if (data != null && !AssetDatabase.Contains(data))
                    Object.DestroyImmediate(data);
                if (folder != null && AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
        }
    }

    public static void Progress(string message, float progress)
    {
        if (EditorUtility.DisplayCancelableProgressBar("Main Menu Terrain", message, progress))
            throw new OperationCanceledException();
    }

    private static TerrainData BuildTerrain(MainMenuTerrainLayout layout, TerrainLayer[] layers, int size, MainMenuTerrainMaps maps, string folder)
    {
        var data = new TerrainData
        {
            name = "Main Menu Terrain",
            heightmapResolution = size * 2 + 1,
            alphamapResolution = 1024,
            baseMapResolution = 1024,
            size = new Vector3(size, MainMenuTerrainLayout.HEIGHT_RANGE, size),
            terrainLayers = layers
        };
        try
        {
            AssetDatabase.CreateAsset(data, folder + "/MainMenuTerrain.asset");
            data.SetHeights(0, 0, maps.CopyHeights());
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            return data;
        }
        catch
        {
            if (!AssetDatabase.Contains(data))
                Object.DestroyImmediate(data);
            throw;
        }
    }

    private static void Configure(Terrain terrain, Material material)
    {
        terrain.materialTemplate = material;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 1f;
        terrain.basemapDistance = 2000f;
        terrain.detailObjectDistance = 150f;
        terrain.detailObjectDensity = 1f;
        terrain.ignoreQualitySettings = true;
        terrain.drawTreesAndFoliage = true;
        terrain.shadowCastingMode = ShadowCastingMode.On;
        terrain.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        terrain.allowAutoConnect = false;
        terrain.groupingID = 32760;
        MainMenuTerrainMaterial.Attach(terrain, material);
        terrain.Flush();
    }

    private static void HideTerrains(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                Undo.RecordObject(terrain, UNDO_NAME);
                terrain.enabled = false;
                if (!terrain.TryGetComponent(out TerrainCollider collider))
                    continue;
                Undo.RecordObject(collider, UNDO_NAME);
                collider.enabled = false;
            }
        }
    }

    private static TerrainLayer[] LoadLayers()
    {
        var layers = new TerrainLayer[MainMenuTerrainLayout.LAYER_COUNT];
        for (int i = 0; i < layers.Length; i++)
        {
            string path = LAYERS_PATH + LayerNames[i] + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null || layer.diffuseTexture == null || layer.normalMapTexture == null || layer.tileSize.x <= 0f || layer.tileSize.y <= 0f)
                throw new InvalidOperationException($"Terrain layer requires diffuse, normal and positive tiling: {path}");
            layers[i] = layer;
        }
        return layers;
    }

    private static BiomeDefinition LoadBiome(string name)
    {
        BiomeDefinition biome = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(BIOMES_PATH + name + ".asset");
        if (biome == null)
            throw new InvalidOperationException($"Missing main generator biome: {name}");
        return biome;
    }

    internal static string CreateOutputFolder(string name)
    {
        EnsureFolder(OUTPUT_ROOT);
        string folder = AssetDatabase.GenerateUniqueAssetPath($"{OUTPUT_ROOT}/{name}");
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(OUTPUT_ROOT, Path.GetFileName(folder))))
            throw new IOException("Could not create the main menu terrain output folder.");
        return folder;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException($"Could not create asset folder: {path}");
    }

    internal static void SaveOutput(string folder)
    {
        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
                continue;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}

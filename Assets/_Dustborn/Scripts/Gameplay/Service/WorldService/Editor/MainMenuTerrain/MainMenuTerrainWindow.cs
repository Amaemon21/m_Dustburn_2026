using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MainMenuTerrainWindow : EditorWindow
{
    [SerializeField] private Camera _camera;
    [SerializeField] private int _seed = 2026;
    [SerializeField] private int _size = 512;
    [SerializeField] private float _hillHeight = 36f;
    [SerializeField] private float _roadWidth = 6f;
    [SerializeField] private float _cameraHeight = 3f;
    [SerializeField] private float _grassDensity = 1.4f;
    [SerializeField] private float _grassSize = 1.2f;
    [SerializeField] private float _surfaceRelief = 1.2f;
    [SerializeField] private int _settingsVersion;
    [SerializeField] private bool _hidePreviousTerrain = true;
    [SerializeField] private Terrain _repairTerrain;
    private Vector2 _scroll;

    [MenuItem("Tools/Dustborn/Main Menu Terrain")]
    public static void Open()
    {
        GetWindow<MainMenuTerrainWindow>("Main Menu Terrain");
    }

    private void OnEnable()
    {
        minSize = new Vector2(410f, 560f);
        if (_settingsVersion < 1)
        {
            _grassDensity = 1.4f;
            _grassSize = 1.2f;
            _surfaceRelief = 1.2f;
            _settingsVersion = 1;
        }
        FindCamera();
    }

    private void OnGUI()
    {
        using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
        _scroll = scroll.scrollPosition;
        Scene scene = SceneManager.GetActiveScene();
        bool mainMenu = scene.path == MainMenuTerrainGenerator.SCENE_PATH;
        EditorGUILayout.HelpBox("Creates a new painted Unity Terrain with Repetitionless, a dirt road, edge hills and biome grass. The 28 x 24 m house site sits 36 m ahead and 20 m left of the camera. Generation runs only when you press the button.", MessageType.Info);
        if (!mainMenu)
            EditorGUILayout.HelpBox("Open the MainMenu scene and make it active before generating.", MessageType.Warning);

        _camera = (Camera)EditorGUILayout.ObjectField("Composition Camera", _camera, typeof(Camera), true);
        if (GUILayout.Button("Use MainMenu Camera"))
            FindCamera();

        _seed = EditorGUILayout.IntField("Seed", _seed);
        _size = EditorGUILayout.IntPopup("Terrain Size (m)", _size, new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
        _hillHeight = EditorGUILayout.Slider("Edge Hills (m)", _hillHeight, 12f, 64f);
        _surfaceRelief = EditorGUILayout.Slider("Surface Unevenness (m)", _surfaceRelief, 0f, 3f);
        _roadWidth = EditorGUILayout.Slider("Road Width (m)", _roadWidth, 4f, 9f);
        _cameraHeight = EditorGUILayout.Slider("Camera Above Ground (m)", _cameraHeight, 1f, 8f);
        _grassDensity = EditorGUILayout.Slider("Grass Density Multiplier", _grassDensity, 0f, 4f);
        _grassSize = EditorGUILayout.Slider("Grass Size Multiplier", _grassSize, 0.5f, 2f);
        _hidePreviousTerrain = EditorGUILayout.Toggle("Hide Previous Terrains", _hidePreviousTerrain);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Heightmap", $"{_size * 2 + 1} x {_size * 2 + 1} (0.5 m)");
        EditorGUILayout.LabelField("Alphamap / Basemap", "1024 / 1024");
        EditorGUILayout.LabelField("Terrain Details", "512, 16 per patch, 150 m distance");
        EditorGUILayout.HelpBox("Existing assets stay intact. Each generation creates its own output folder. Scene changes support Undo. Save the scene after inspecting the result; the house marker is an empty placement guide.", MessageType.None);

        bool validCamera = _camera != null && _camera.gameObject.scene == scene;
        using (new EditorGUI.DisabledScope(!mainMenu || !validCamera || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generate New Terrain", GUILayout.Height(34f)))
                Generate();
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Existing Terrain", EditorStyles.boldLabel);
        _repairTerrain = (Terrain)EditorGUILayout.ObjectField("Terrain To Repair", _repairTerrain, typeof(Terrain), true);
        if (GUILayout.Button("Use Selected Terrain"))
            _repairTerrain = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Terrain>() : null;
        using (new EditorGUI.DisabledScope(!MainMenuTerrainRepair.IsValid(_repairTerrain)))
        {
            if (GUILayout.Button("Restore Road And Remove Burnt Grass"))
                MainMenuTerrainRepair.Repair(_repairTerrain, _camera, _roadWidth);
        }
    }

    private void FindCamera()
    {
        _camera = null;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMenuTerrainGenerator.SCENE_PATH)
            return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.CompareTag("MainCamera"))
                    continue;
                _camera = camera;
                return;
            }
        }
    }

    private void Generate()
    {
        try
        {
            MainMenuTerrainGenerator.Generate(_camera, _seed, _size, _hillHeight, _roadWidth, _cameraHeight, _grassDensity, _hidePreviousTerrain, _grassSize, _surfaceRelief);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Main menu terrain generation cancelled.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}

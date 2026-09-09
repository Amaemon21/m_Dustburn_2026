#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldGenBenchFixtureBuilder
{
    public const string FIXTURE_PATH = "Assets/_Dustborn/Benchmarks/Resources/WorldGenBenchFixture.asset";
    public const string SCENE_PATH = "Assets/_Dustborn/Benchmarks/WorldGenPerf.unity";

    [MenuItem("Tools/World Gen Perf/Build Fixture")]
    public static void Build()
    {
        WorldGenBenchFixture fixture = BuildFixture();
        Selection.activeObject = fixture;
        Debug.Log("World gen benchmark fixture: " + fixture.Describe());
    }

    [MenuItem("Tools/World Gen Perf/Build Benchmark Scene")]
    public static void BuildScene()
    {
        string path = CreateScene();
        Debug.Log("World gen benchmark scene written to " + path);
    }

    public static WorldGenBenchFixture BuildFixture()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FIXTURE_PATH)));

        var fixture = AssetDatabase.LoadAssetAtPath<WorldGenBenchFixture>(FIXTURE_PATH);

        if (fixture == null)
        {
            fixture = ScriptableObject.CreateInstance<WorldGenBenchFixture>();
            AssetDatabase.CreateAsset(fixture, FIXTURE_PATH);
        }

        var settings = First<WorldBuildSettings>();

        fixture.EditorSetup(
            settings != null && settings.Config != null ? settings.Config : First<WorldGenerationConfig>(),
            settings != null && settings.Biomes != null ? settings.Biomes : First<BiomeDatabase>(),
            settings != null && settings.Pois != null ? settings.Pois : First<PoiDatabase>(),
            settings != null && settings.Voxels != null ? settings.Voxels : First<VoxelConfig>(),
            settings,
            First<BakedWorld>());

        EditorUtility.SetDirty(fixture);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WorldGenBenchFixtures.Forget();
        return fixture;
    }

    public static string CreateScene()
    {
        WorldGenBenchFixture fixture = BuildFixture();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var holder = new GameObject("WorldGenPerf");
        var keeper = holder.AddComponent<WorldGenBenchSceneReferences>();
        keeper.EditorSetup(fixture);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SCENE_PATH)));
        EditorSceneManager.SaveScene(scene, SCENE_PATH);

        Include(SCENE_PATH);
        AssetDatabase.SaveAssets();
        return SCENE_PATH;
    }

    private static void Include(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene existing in scenes)
        {
            if (existing.path == path)
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static T First<T>() where T : UnityEngine.Object
    {
        string[] found = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        T best = null;

        foreach (string guid in found)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!path.StartsWith("Assets/_Dustborn/", StringComparison.Ordinal))
                continue;

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
                continue;

            if (best == null || path.Contains("/Content/World/") || path.Contains("/Generated/"))
                best = asset;
        }

        return best;
    }
}
#endif

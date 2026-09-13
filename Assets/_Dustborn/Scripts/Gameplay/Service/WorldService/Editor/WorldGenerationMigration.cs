#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class WorldGenerationMigration
{
    public static void Apply(WorldGenerator owner, WorldBuildSettings settings)
    {
        Transform scope = owner.transform.parent != null ? owner.transform.parent : owner.transform;

        foreach (VoxelTerrainStreamer streamer in scope.GetComponentsInChildren<VoxelTerrainStreamer>(true))
        {
            var source = new SerializedObject(streamer);

            if (source.FindProperty("_config").objectReferenceValue != settings.Config)
                continue;

            if (owner.Viewer == null)
                WorldGenerationEditorRun.Assign(owner, "_viewer", source.FindProperty("_viewer").objectReferenceValue);

            RemoveLegacyComponent(streamer);
        }

        foreach (VoxelTerrainBuilder builder in scope.GetComponentsInChildren<VoxelTerrainBuilder>(true))
        {
            if (builder.transform.IsChildOf(owner.transform))
                continue;

            var source = new SerializedObject(builder);

            if (source.FindProperty("_config").objectReferenceValue != settings.Config)
                continue;

            builder.Clear();
            RemoveLegacyComponent(builder);
        }

        foreach (PoiSpawner spawner in scope.GetComponentsInChildren<PoiSpawner>(true))
        {
            var source = new SerializedObject(spawner);
            Object placement = source.FindProperty("_placement").objectReferenceValue;

            if (AssetDatabase.GetAssetPath(placement) != settings.Config.PoiPlacementAssetPath)
                continue;

            for (int index = spawner.transform.childCount - 1; index >= 0; index--)
                Undo.DestroyObjectImmediate(spawner.transform.GetChild(index).gameObject);

            RemoveLegacyComponent(spawner);
        }

        ConfigurePlayerPause(owner);
    }

    public static void ConfigurePlayerPause(WorldGenerator owner)
    {
        if (owner.Viewer == null)
            return;

        var serialized = new SerializedObject(owner);
        SerializedProperty paused = serialized.FindProperty("_pauseWhileLoading");

        if (paused.arraySize != 0)
            return;

        foreach (Behaviour behaviour in owner.Viewer.GetComponentsInParent<Behaviour>(true))
        {
            if (behaviour.GetType().Name != "PlayerController")
                continue;

            paused.InsertArrayElementAtIndex(paused.arraySize);
            paused.GetArrayElementAtIndex(paused.arraySize - 1).objectReferenceValue = behaviour;
        }

        serialized.ApplyModifiedProperties();
    }

    private static void RemoveLegacyComponent(Component component)
    {
        GameObject holder = component.gameObject;
        Undo.DestroyObjectImmediate(component);

        if (holder.transform.childCount == 0 && holder.GetComponents<Component>().Length == 1)
            Undo.DestroyObjectImmediate(holder);
    }
}
#endif

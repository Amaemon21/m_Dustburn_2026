using System;
using System.Collections.Generic;
using UnityEngine;

public static class WorldGenBenchFixtures
{
    private static WorldGenBenchFixture _fixture;
    private static readonly Dictionary<string, WorldGenBenchFrozen> FROZEN = new(StringComparer.OrdinalIgnoreCase);

    public static WorldGenBenchFixture Resolve()
    {
        if (_fixture != null)
            return _fixture;

        _fixture = Resources.Load<WorldGenBenchFixture>(WorldGenBenchFixture.RESOURCES_PATH);

#if UNITY_EDITOR
        if (_fixture == null)
            _fixture = LoadFromProject();
#endif

        return _fixture;
    }

    public static void Forget()
    {
        _fixture = null;
        Release();
    }

    public static WorldGenBenchFrozen Frozen(WorldGenBenchProfile profile, string stage)
    {
        string key = profile.Id + "/" + profile.Fingerprint + "/" + stage;

        if (FROZEN.TryGetValue(key, out WorldGenBenchFrozen cached))
            return cached;

        var frozen = new WorldGenBenchFrozen(profile, stage);
        FROZEN[key] = frozen;
        return frozen;
    }

    public static void Release()
    {
        FROZEN.Clear();
    }

#if UNITY_EDITOR
    private static WorldGenBenchFixture LoadFromProject()
    {
        string[] found = UnityEditor.AssetDatabase.FindAssets("t:WorldGenBenchFixture");

        if (found.Length == 0)
            return null;

        return UnityEditor.AssetDatabase.LoadAssetAtPath<WorldGenBenchFixture>(
            UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
    }
#endif
}

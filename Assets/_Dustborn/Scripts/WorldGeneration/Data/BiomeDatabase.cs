using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDatabase", menuName = "World/Biome Database")]
public class BiomeDatabase : ScriptableObject
{
    public const string RESOURCES_PATH = "BiomeDatabase";

    [SerializeField, HorizontalLine] private List<BiomeDefinition> _biomes = new();

    public int Count => _biomes.Count;

    public IReadOnlyList<BiomeDefinition> Biomes => _biomes;

    public BiomeDefinition Get(int index)
    {
        return _biomes[index];
    }

    public int IndexOf(BiomeType type)
    {
        for (int i = 0; i < _biomes.Count; i++)
        {
            if (_biomes[i] != null && _biomes[i].Type == type)
                return i;
        }

        return -1;
    }

#if UNITY_EDITOR
    private const float LAST_LOD_FADE = 0.3f;
    private const float LOD_FADE = 0.15f;

    [Button("Fade Scatter LOD Switches")]
    private void FadeScatterLods()
    {
        var prefabs = new HashSet<GameObject>();

        foreach (BiomeDefinition biome in _biomes)
        {
            if (biome == null)
                continue;

            Collect(biome.Trees, prefabs);
            Collect(biome.Rocks, prefabs);
        }

        int changed = 0;

        foreach (GameObject prefab in prefabs)
        {
            if (Fade(prefab))
                changed++;
        }

        Debug.Log($"Scatter LOD: {changed} of {prefabs.Count} prefabs switched to cross-fading. Without it a tree is swapped or culled "
            + "in one frame and blinks as the player walks past the switch distance", this);
    }

    private static void Collect(List<ScatterLayer> layers, HashSet<GameObject> prefabs)
    {
        foreach (ScatterLayer layer in layers)
        {
            if (layer != null && layer.Prefab != null)
                prefabs.Add(layer.Prefab);
        }
    }

    private static bool Fade(GameObject prefab)
    {
        var group = prefab.GetComponent<LODGroup>();

        if (group == null)
            return false;

        LOD[] levels = group.GetLODs();

        if (levels.Length == 0)
            return false;

        for (int i = 0; i < levels.Length; i++)
            levels[i].fadeTransitionWidth = i == levels.Length - 1 ? LAST_LOD_FADE : LOD_FADE;

        group.fadeMode = LODFadeMode.CrossFade;
        group.animateCrossFading = false;
        group.SetLODs(levels);

        UnityEditor.EditorUtility.SetDirty(prefab);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(prefab);

        return true;
    }

    [Button("Create 7DTD Biome Set")]
    private void CreateDefaultBiomes()
    {
        string databasePath = UnityEditor.AssetDatabase.GetAssetPath(this);

        if (string.IsNullOrEmpty(databasePath))
        {
            Debug.LogError("Save the BiomeDatabase asset first", this);
            return;
        }

        string folder = System.IO.Path.GetDirectoryName(databasePath);

        _biomes.Clear();

        Add(folder, BiomeType.PineForest, new Color32(0, 64, 0, 255), 1.2f, 0.30f, 0.18f, 0.06f, 0f, 0.020f);
        Add(folder, BiomeType.BurntForest, new Color32(186, 0, 255, 255), 1f, 0.30f, 0.15f, 0.04f, 0f, 0.020f);
        Add(folder, BiomeType.Desert, new Color32(255, 228, 119, 255), 1f, 0.26f, 0.04f, 0f, 0.09f, 0.010f);
        Add(folder, BiomeType.Snow, new Color32(255, 255, 255, 255), 1f, 0.34f, 0.10f, 0.42f, 0f, 0.030f);
        Add(folder, BiomeType.Wasteland, new Color32(255, 168, 0, 255), 1f, 0.26f, 0.05f, 0.02f, 0f, 0.030f);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    private void Add(string folder, BiomeType type, Color32 mapColor, float regionWeight, float baseHeight, float hills, float ridge, float dune, float detail)
    {
        string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/Biome_{type}.asset");

        var biome = CreateInstance<BiomeDefinition>();
        biome.EditorSetup(type, mapColor, regionWeight, baseHeight, hills, ridge, dune, detail);

        UnityEditor.AssetDatabase.CreateAsset(biome, path);

        _biomes.Add(biome);
    }
#endif

    public bool IsValid()
    {
        if (_biomes.Count == 0)
            return false;

        foreach (BiomeDefinition biome in _biomes)
        {
            if (biome == null)
                return false;
        }

        return true;
    }
}

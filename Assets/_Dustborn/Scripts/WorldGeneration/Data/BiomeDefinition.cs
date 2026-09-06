using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDefinition", menuName = "World/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [field: SerializeField, BoxGroup("Biome map")] public BiomeType Type { get; private set; }
    [field: SerializeField, BoxGroup("Biome map")] public Color32 MapColor { get; private set; } = new(0, 64, 0, 255);
    [field: SerializeField, BoxGroup("Biome map"), MinValue(0.05f)] public float RegionWeight { get; private set; } = 1f;

    [field: SerializeField, Foldout("Ground")]
    [field: Tooltip("Grounds of this biome. Weights are normalised within the biome, so the list sets proportions: a base layer over the whole area, then noise patches and shelves keyed to slope and height. The world-wide layer budget is MaxTerrainLayers in the config.")]
    public List<GroundLayer> Ground { get; private set; } = new();

    [field: SerializeField, Foldout("Grass")]
    [field: Tooltip("Density in clumps per square metre. An empty list means a biome with no grass.")]
    public List<GrassLayer> Grass { get; private set; } = new();

    [field: SerializeField, Foldout("Trees")]
    [field: Tooltip("Density in items per hectare. Spacing is the scatter grid step and doubles as the minimum gap between trunks.")]
    public List<ScatterLayer> Trees { get; private set; } = new();

    [field: SerializeField, Foldout("Rocks")]
    [field: Tooltip("Same rules as Trees, kept apart so boulders and scree can be tuned or switched off on their own.")]
    public List<ScatterLayer> Rocks { get; private set; } = new();

    [field: SerializeField, Foldout("Terrain profile"), Range(0f, 1f)] public float BaseHeight { get; private set; } = 0.30f;
    [field: SerializeField, Foldout("Terrain profile"), Range(0f, 1f)] public float HillAmplitude { get; private set; } = 0.18f;
    [field: SerializeField, Foldout("Terrain profile"), Range(0f, 1f)] public float RidgeAmplitude { get; private set; } = 0.06f;
    [field: SerializeField, Foldout("Terrain profile"), Range(0f, 1f)] public float DuneAmplitude { get; private set; }
    [field: SerializeField, Foldout("Terrain profile"), Range(0f, 0.2f)] public float DetailAmplitude { get; private set; } = 0.02f;

    private void OnValidate()
    {
        foreach (GroundLayer ground in Ground)
        {
            if (ground != null && ground.IsUnset)
                ground.ApplyDefaults();
        }

        foreach (GrassLayer grass in Grass)
        {
            if (grass != null && grass.IsUnset)
                grass.ApplyDefaults();
        }

        foreach (ScatterLayer scatter in Trees)
        {
            if (scatter != null && scatter.IsUnset)
                scatter.ApplyDefaults();
        }

        foreach (ScatterLayer scatter in Rocks)
        {
            if (scatter != null && scatter.IsUnset)
                scatter.ApplyDefaults();
        }
    }

#if UNITY_EDITOR
    [Button("Measure Scatter Footprints")]
    private void MeasureScatterFootprints()
    {
        int measured = Measure(Trees) + Measure(Rocks);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"{name}: measured {measured} scatter footprints from prefab meshes. Footprint is the base radius in metres and keeps props out of house pads and off the roadway", this);
    }

    private int Measure(List<ScatterLayer> layers)
    {
        int measured = 0;

        foreach (ScatterLayer layer in layers)
        {
            if (layer == null || layer.Prefab == null)
                continue;

            if (!TryMeasureRadius(layer.Prefab, out float radius))
            {
                Debug.LogWarning($"{name}: prefab {layer.Prefab.name} has no MeshFilter with a mesh, its Footprint is left as is", this);
                continue;
            }

            layer.EditorSetFootprint(radius);
            measured++;
        }

        return measured;
    }

    private static bool TryMeasureRadius(GameObject prefab, out float radius)
    {
        radius = 0f;

        bool any = false;
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;

        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled)
                continue;

            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 point = matrix.MultiplyPoint3x4(offset);

                radius = Mathf.Max(radius, new Vector2(point.x, point.z).magnitude);
                any = true;
            }
        }

        return any;
    }

    public void EditorSetup(BiomeType type, Color32 mapColor, float regionWeight, float baseHeight, float hills, float ridge, float dune, float detail)
    {
        Type = type;
        MapColor = mapColor;
        RegionWeight = regionWeight;
        BaseHeight = baseHeight;
        HillAmplitude = hills;
        RidgeAmplitude = ridge;
        DuneAmplitude = dune;
        DetailAmplitude = detail;
    }
#endif
}

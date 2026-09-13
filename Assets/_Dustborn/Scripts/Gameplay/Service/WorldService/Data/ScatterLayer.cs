using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class ScatterLayer
{
    [field: SerializeField]
    [field: Tooltip("Trees and rocks work the same way. The root must carry a MeshRenderer or an LODGroup: a prefab with an LODGroup keeps it and stays a separate object, one without gets combined or instanced.")]
    public GameObject Prefab { get; private set; }

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Items per hectare before patch noise cuts in. Spacing caps it: one item per grid cell, so 7 metres holds at most 204 per hectare.")]
    public float PerHectare { get; private set; } = 120f;

    [field: SerializeField, MinValue(1f)]
    [field: Tooltip("Scatter grid step in metres, which doubles as the minimum gap between two items of this layer.")]
    public float Spacing { get; private set; } = 8f;

    [field: SerializeField, MinValue(1f)]
    [field: Tooltip("Metres per noise period. This is what turns an even sprinkle into groves and clearings.")]
    public float PatchFrequency { get; private set; } = 180f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Noise level below which nothing grows. Zero scatters evenly and skips the noise entirely.")]
    public float PatchThreshold { get; private set; } = 0.35f;

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Steepest slope in degrees the layer will sit on. Rocks can go far higher than trees.")]
    public float MaxSlope { get; private set; } = 30f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer starts, normalised against MaxHeight from the config.")]
    public float MinHeight { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer ends, normalised against MaxHeight from the config.")]
    public float MaxHeight { get; private set; } = 1f;

    [field: SerializeField, MinValue(0.05f)] public float MinScale { get; private set; } = 0.85f;
    [field: SerializeField, MinValue(0.05f)] public float MaxScale { get; private set; } = 1.25f;

    [field: SerializeField, Range(0f, 0.5f)]
    [field: Tooltip("How much height may drift away from width, so the row does not read as one model copied over and over.")]
    public float Squash { get; private set; } = 0.12f;

    [field: SerializeField]
    [field: Tooltip("Multiplied into the prefab material, so black kills the item and white leaves it alone.")]
    public Color Tint { get; private set; } = Color.white;

    [field: SerializeField, Range(0f, 0.5f)] public float TintVariance { get; private set; } = 0.08f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Wind bending. Only tree shaders that read it respond, so Synty prefabs ignore this.")]
    public float BendFactor { get; private set; }

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Radius of the base in metres. Keeps bulky props out of house pads and off the roadway: a boulder placed by its centre still overlaps a wall three metres away.")]
    public float Footprint { get; private set; } = 1.5f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Extra gap from the road surface. Zero lets the layer grow right up to the shoulder.")]
    public float RoadClearance { get; private set; } = 3f;

    public bool IsValid => Prefab != null;

    public bool IsUnset => PerHectare <= 0f && Spacing <= 0f && MaxSlope <= 0f && MaxScale <= 0f && PatchFrequency <= 0f && Footprint <= 0f;

    public bool IsMute => PerHectare <= 0f || Spacing <= 0f || MaxSlope <= 0f || MaxScale <= 0f || PatchThreshold >= 1f;

    public float Chance => Mathf.Clamp01(PerHectare / 10000f * Spacing * Spacing);

    public void ApplyDefaults()
    {
        PerHectare = 120f;
        Spacing = 8f;
        PatchFrequency = 180f;
        PatchThreshold = 0.35f;
        MaxSlope = 30f;
        MinHeight = 0f;
        MaxHeight = 1f;
        MinScale = 0.85f;
        MaxScale = 1.25f;
        Squash = 0.12f;
        Tint = Color.white;
        TintVariance = 0.08f;
        BendFactor = 0f;
        Footprint = 1.5f;
        RoadClearance = 3f;
    }

#if UNITY_EDITOR
    public void EditorSetFootprint(float footprint)
    {
        Footprint = footprint;
    }
#endif

}

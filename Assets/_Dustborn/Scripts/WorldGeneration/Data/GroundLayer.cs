using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class GroundLayer
{
    [field: SerializeField]
    [field: Tooltip("One ground of the biome. The first entry is normally the base: higher Weight, PatchThreshold zero, slope and height ranges wide open. The rest are patches laid over it, and a pixel that matches no rule falls back to the first entry.")]
    public TerrainLayer Layer { get; private set; }

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Share of the biome. Weights are normalised across the biome, so only the ratio matters: base 1, patches 1.4 to 1.9, slope shelves 2.4 and up so they win where they apply.")]
    public float Weight { get; private set; } = 1f;

    [field: SerializeField, MinValue(1f)]
    [field: Tooltip("Metres per noise period. Small values give freckles, large ones give whole fields.")]
    public float PatchFrequency { get; private set; } = 140f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Noise level below which the layer is absent. Zero means the layer is everywhere and the noise is not sampled at all, which is what makes a base layer cheap.")]
    public float PatchThreshold { get; private set; }

    [field: SerializeField, Range(0.01f, 1f)]
    [field: Tooltip("How far above the threshold the patch reaches full strength. Wider means softer edges.")]
    public float PatchFade { get; private set; } = 0.12f;

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Slope in degrees where the layer starts. Zero skips the test.")]
    public float MinSlope { get; private set; }

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Slope in degrees where the layer ends.")]
    public float MaxSlope { get; private set; } = 90f;

    [field: SerializeField, Range(0.5f, 45f)]
    [field: Tooltip("Degrees of blend on both ends of the slope band.")]
    public float SlopeFade { get; private set; } = 8f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer starts, normalised against MaxHeight from the config.")]
    public float MinHeight { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer ends, normalised against MaxHeight from the config.")]
    public float MaxHeight { get; private set; } = 1f;

    [field: SerializeField, Range(0.001f, 0.5f)]
    [field: Tooltip("Normalised height of the blend on both ends of the height band.")]
    public float HeightFade { get; private set; } = 0.05f;

    public bool IsValid => Layer != null;

    public bool IsUnset => Weight <= 0f && PatchFrequency <= 0f && MaxSlope <= 0f && MaxHeight <= 0f && SlopeFade <= 0f;

    public bool IsMute => Weight <= 0f || MaxSlope <= MinSlope || MaxHeight <= MinHeight || PatchThreshold >= 1f;

    public void ApplyDefaults()
    {
        Weight = 1f;
        PatchFrequency = 140f;
        PatchThreshold = 0f;
        PatchFade = 0.12f;
        MinSlope = 0f;
        MaxSlope = 90f;
        SlopeFade = 8f;
        MinHeight = 0f;
        MaxHeight = 1f;
        HeightFade = 0.05f;
    }
}

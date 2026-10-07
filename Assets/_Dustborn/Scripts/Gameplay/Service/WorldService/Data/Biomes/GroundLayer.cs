using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class GroundLayer
{
    [field: SerializeField]
    [field: Tooltip("One ground of the biome. Grounds are stacked in list order: the first entry is the base and takes whatever the entries above it leave uncovered, every later entry is laid over all the ones before it wherever its patch, slope and height rules pass.")]
    public TerrainLayer Layer { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("How much of the grounds below this one it hides where its rules pass. One paints it solid, a half lets the ground underneath show through. Ignored on the first entry, which is the base.")]
    public float Opacity { get; private set; } = 1f;

    [field: SerializeField, MinValue(1f)]
    [field: Tooltip("Metres per noise period. Small values give freckles, large ones give whole fields.")]
    public float PatchFrequency { get; private set; } = 140f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Noise level below which the layer is absent. Zero lays it everywhere its slope and height allow and skips the noise and the terrain response, which is what makes a slope shelf cheap. Around 0.5 covers half the biome, 0.65 leaves rare islands.")]
    public float PatchThreshold { get; private set; }

    [field: SerializeField, Range(0.01f, 1f)]
    [field: Tooltip("Noise range over which a patch edge fades in. About 0.04 gives a crisp stylised edge a couple of metres wide, 0.15 and up a soft blend over tens of metres.")]
    public float PatchFade { get; private set; } = 0.06f;

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Slope in degrees where the layer starts. Zero skips the test.")]
    public float MinSlope { get; private set; }

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Slope in degrees where the layer ends.")]
    public float MaxSlope { get; private set; } = 90f;

    [field: SerializeField, Range(0.5f, 45f)]
    [field: Tooltip("Degrees of blend on both ends of the slope band.")]
    public float SlopeFade { get; private set; } = 6f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer starts, normalised against MaxHeight from the config.")]
    public float MinHeight { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Terrain height where the layer ends, normalised against MaxHeight from the config.")]
    public float MaxHeight { get; private set; } = 1f;

    [field: SerializeField, Range(0.001f, 0.5f)]
    [field: Tooltip("Normalised height of the blend on both ends of the height band.")]
    public float HeightFade { get; private set; } = 0.05f;

    [field: SerializeField, Header("Terrain response"), Range(-0.5f, 0.5f)]
    [field: Tooltip("Shift of the patch noise by the shared macro field. Positive grows the layer in the macro field's high regions, negative in its low ones; the grounds of one biome that disagree in sign split the large regions between them.")]
    public float MacroBias { get; private set; }

    [field: SerializeField, Range(-0.5f, 0.5f)]
    [field: Tooltip("Shift of the patch noise by slope. Positive gathers the layer on slopes steeper than GroundSlopeMid, negative on flats. Soft, unlike the slope band.")]
    public float SlopeBias { get; private set; }

    [field: SerializeField, Range(-0.5f, 0.5f)]
    [field: Tooltip("Shift of the patch noise by relief. Positive gathers the layer on ridges and convex ground, negative in hollows and valley floors.")]
    public float ReliefBias { get; private set; }

    [field: SerializeField, Range(-0.5f, 0.5f)]
    [field: Tooltip("Shift of the patch noise near the biome's own border. Positive makes this ground part of the transition to the neighbouring biome.")]
    public float EdgeBias { get; private set; }

    public bool IsValid => Layer != null;

    public bool IsUnset => Opacity <= 0f && PatchFrequency <= 0f && MaxSlope <= 0f && MaxHeight <= 0f && SlopeFade <= 0f;

    public bool IsMute => Opacity <= 0f || MaxSlope <= MinSlope || MaxHeight <= MinHeight || PatchThreshold >= 1f;

    public void ApplyDefaults()
    {
        Opacity = 1f;
        PatchFrequency = 140f;
        PatchThreshold = 0f;
        PatchFade = 0.06f;
        MinSlope = 0f;
        MaxSlope = 90f;
        SlopeFade = 6f;
        MinHeight = 0f;
        MaxHeight = 1f;
        HeightFade = 0.05f;
        MacroBias = 0f;
        SlopeBias = 0f;
        ReliefBias = 0f;
        EdgeBias = 0f;
    }
}

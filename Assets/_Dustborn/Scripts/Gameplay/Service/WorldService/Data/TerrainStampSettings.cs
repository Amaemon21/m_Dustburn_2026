using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class TerrainStampSettings
{
    [field: SerializeField] public bool Enabled { get; private set; } = true;

    [field: SerializeField] public TerrainStampDatabase Database { get; private set; }

    [field: SerializeField, Range(0, 64)]
    [field: Tooltip("Stamps placed over the world. They go on before erosion, so water and weather wear them into the procedural relief.")]
    public int Count { get; private set; } = 14;

    [field: SerializeField, Range(0f, 2f)]
    [field: Tooltip("Multiplier on every stamp's RecommendedAmplitude. The pack is authored for a taller world than MaxHeight 384, so this sits well under one.")]
    public float AmplitudeScale { get; private set; } = 0.45f;

    [field: SerializeField, Range(0f, 0.6f)] public float ScaleVariation { get; private set; } = 0.25f;

    [field: SerializeField, Range(0f, 0.4f)]
    [field: Tooltip("Independent stretch of X against Z, so no two copies of a stamp share a silhouette.")]
    public float Stretch { get; private set; } = 0.15f;

    [field: SerializeField, Range(0f, 0.6f)] public float AmplitudeVariation { get; private set; } = 0.2f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres between the centres of two stamps.")]
    public float MinSpacing { get; private set; } = 900f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Largest share of the smaller footprint two stamps may cover together.")]
    public float MaxOverlap { get; private set; } = 0.2f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres kept between a stamp's footprint and the world border.")]
    public float EdgeMargin { get; private set; } = 192f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Chance weight of a stamp outside its preferred biomes, against 1 inside them.")]
    public float OffBiomeWeight { get; private set; } = 0.12f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Where stamps of one kind overlap, the strongest counts fully and the rest by this share, so two mountains do not stack into one twice as tall.")]
    public float OverlapBlend { get; private set; } = 0.35f;

    [field: SerializeField, Range(0.5f, 1f)]
    [field: Tooltip("Share of MaxHeight above which added height is compressed smoothly instead of clipped, so peaks round off rather than grow flat tops.")]
    public float Ceiling { get; private set; } = 0.9f;

    [field: SerializeField, Range(0f, 0.25f)]
    [field: Tooltip("Extra fade toward the footprint edge in UV units. The maps already fall to zero at their borders, so this stays off unless a stamp's foot needs softening.")]
    public float EdgeSoftness { get; private set; }
}

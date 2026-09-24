using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class WaterStampSettings
{
    [field: SerializeField]
    [field: Tooltip("Shape rivers, lakes and ponds with the authored water stamps. Off, or with no database, the procedural water is generated exactly as before.")]
    public bool Enabled { get; private set; } = true;

    [field: SerializeField] public WaterStampDatabase Database { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("How much of a river stamp's floodplain and banks are carved into the ground around the channel. Zero keeps only the authored course, width and depth.")]
    public float RiverInfluence { get; private set; } = 1f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("How far a lake or pond stamp reshapes its basin: bed depth, dug shore and filled lake bed outside the authored outline.")]
    public float LakeInfluence { get; private set; } = 1f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Share of each stamp's manifest scale range tried around the scale that matches the local river width.")]
    public float ScaleVariation { get; private set; } = 0.35f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres on each side of a join between two river stamps over which the course is blended from one tangent to the next.")]
    public float BlendDistance { get; private set; } = 60f;

    [field: SerializeField, Range(0f, 90f)]
    [field: Tooltip("Largest change of direction in degrees a join between two river stamps may need.")]
    public float MaxTurnAdjustment { get; private set; } = 40f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres of extra trench a stamp's course may need over the drainage route it replaces. Higher lets meanders climb out of their valley.")]
    public float MaxClimb { get; private set; } = 3f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Smallest share of a basin's flooded area a lake stamp must cover to replace the basin outline.")]
    public float MinLakeCoverage { get; private set; } = 0.55f;

    [field: SerializeField, MinValue(0f)]
    [field: Tooltip("Metres a lake stamp may raise flooded ground outside its outline, or dig dry ground inside it.")]
    public float MaxLakeEarthworks { get; private set; } = 6f;

    [field: SerializeField]
    [field: Tooltip("Mirror stamps whose manifest allows it.")]
    public bool AllowMirroring { get; private set; } = true;

    [field: SerializeField]
    [field: Tooltip("Keep every rejected candidate with its reason for the debug maps.")]
    public bool RecordRejections { get; private set; }
}

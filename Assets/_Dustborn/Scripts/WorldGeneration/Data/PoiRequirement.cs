using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class PoiRequirement
{
    [field: SerializeField]
    [field: Tooltip("The building this settlement must have. Its own District decides which zone it goes to.")]
    public PoiDefinition Definition { get; private set; }

    [field: SerializeField, MinValue(1)]
    [field: Tooltip("How many of them the settlement must have.")]
    public int Count { get; private set; } = 1;

    [field: SerializeField, Range(0f, 1.5f)]
    [field: Tooltip("Nearest the centre it may stand, as a fraction of the settlement radius.")]
    public float MinRadius { get; private set; }

    [field: SerializeField, Range(0f, 1.5f)]
    [field: Tooltip("Farthest from the centre it may stand, as a fraction of the settlement radius.")]
    public float MaxRadius { get; private set; } = 1f;

    public PoiRequirement()
    {
    }

    public PoiRequirement(PoiDefinition definition, int count, float minRadius, float maxRadius)
    {
        Definition = definition;
        Count = count;
        MinRadius = minRadius;
        MaxRadius = maxRadius;
    }

    public bool IsValid => Definition != null && Definition.Prefab != null && Count > 0;

    public float Area => Definition == null ? 0f : Definition.FootprintArea;

    public bool Accepts(DistrictType district, float distance)
    {
        if (Definition == null || Definition.District != district)
            return false;

        return distance >= MinRadius && distance <= MaxRadius;
    }
}

using System;
using UnityEngine;
using NaughtyAttributes;

[Serializable]
public class InteractProperty
{
    [field: SerializeField, Header("Raycast"), HorizontalLine(2f, EColor.Blue)] public LayerMask HitScanMask { get; private set; } = ~0;
    [field: SerializeField, Min(0.01f)] public float InteractRange { get; private set; } = 3f;
    [field: SerializeField, Space] public QueryTriggerInteraction TriggerInteraction { get; private set; } = QueryTriggerInteraction.Collide;
    [field: SerializeField, Header("Aim"), HorizontalLine(2f, EColor.Blue)] public Vector2 ViewportPoint { get; private set; } = new(0.5f, 0.5f);
}

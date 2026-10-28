using System;
using UnityEngine;

[Serializable]
public sealed class NavigationSettings
{
    [field: SerializeField] public Vector2 GraphSize { get; private set; } = new(320f, 320f);
    [field: SerializeField, Min(16f)] public float GraphHeight { get; private set; } = 320f;
    [field: SerializeField, Range(0.1f, 1f)] public float CellSize { get; private set; } = 0.4f;
    [field: SerializeField, Range(16, 256)] public int TileVoxels { get; private set; } = 64;
    [field: SerializeField, Min(0.1f)] public float CharacterRadius { get; private set; } = 0.5f;
    [field: SerializeField, Min(0.5f)] public float WalkableHeight { get; private set; } = 1.8f;
    [field: SerializeField, Min(0f)] public float WalkableClimb { get; private set; } = 0.5f;
    [field: SerializeField, Range(0f, 89f)] public float MaxSlope { get; private set; } = 40f;
    [field: SerializeField] public LayerMask Layers { get; private set; } = Physics.DefaultRaycastLayers;
}

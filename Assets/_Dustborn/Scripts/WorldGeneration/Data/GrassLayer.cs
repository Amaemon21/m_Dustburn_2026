using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class GrassLayer
{
    [field: SerializeField]
    [field: Tooltip("Cut out texture of one grass tuft, drawn as two crossed quads. Eight vertices instead of the hundreds a mesh bush costs, so this is what the near ring can afford. Set it and the prefab below is ignored.")]
    public Texture2D Card { get; private set; }

    [field: SerializeField]
    [field: Tooltip("Mesh of the grass, used only when no card texture is set. The voxel terrain draws it instanced, so the root must carry a MeshRenderer.")]
    public GameObject Prefab { get; private set; }

    [field: SerializeField, MinValue(0f)] public float Density { get; private set; } = 1.5f;
    [field: SerializeField, MinValue(0.01f)] public float PatchFrequency { get; private set; } = 24f;
    [field: SerializeField, Range(0f, 1f)] public float PatchThreshold { get; private set; } = 0.35f;
    [field: SerializeField, Range(0f, 90f)] public float MaxSlope { get; private set; } = 32f;

    [field: SerializeField, MinValue(0.05f)]
    [field: Tooltip("Width of the tuft: metres for a card, a multiplier of the prefab scale for a mesh.")]
    public float MinWidth { get; private set; } = 0.6f;
    [field: SerializeField, MinValue(0.05f)] public float MaxWidth { get; private set; } = 1.4f;
    [field: SerializeField, MinValue(0.05f)] public float MinHeight { get; private set; } = 0.5f;
    [field: SerializeField, MinValue(0.05f)] public float MaxHeight { get; private set; } = 1.2f;

    [field: SerializeField] public Color HealthyColor { get; private set; } = new(0.55f, 0.62f, 0.35f);
    [field: SerializeField] public Color DryColor { get; private set; } = new(0.62f, 0.55f, 0.3f);
    [field: SerializeField, MinValue(0.1f)] public float NoiseSpread { get; private set; } = 0.35f;

    public bool IsValid => Card != null || Prefab != null;

    public bool IsUnset => Density <= 0f && PatchFrequency <= 0f && MaxSlope <= 0f && MaxWidth <= 0f && MaxHeight <= 0f;

    public bool IsMute => Density <= 0f || MaxSlope <= 0f || MaxWidth <= 0f || MaxHeight <= 0f;

    public void ApplyDefaults()
    {
        Density = 1.5f;
        PatchFrequency = 24f;
        PatchThreshold = 0.35f;
        MaxSlope = 32f;
        MinWidth = 0.6f;
        MaxWidth = 1.4f;
        MinHeight = 0.5f;
        MaxHeight = 1.2f;
        HealthyColor = new Color(0.55f, 0.62f, 0.35f);
        DryColor = new Color(0.62f, 0.55f, 0.3f);
        NoiseSpread = 0.35f;
    }

}

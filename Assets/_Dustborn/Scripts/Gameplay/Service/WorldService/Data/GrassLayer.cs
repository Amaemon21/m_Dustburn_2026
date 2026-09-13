using System;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class GrassLayer
{
    [field: SerializeField]
    [field: Tooltip("White shaded plant texture with alpha, drawn on two crossed cards. Set it and the prefab below is ignored.")]
    public Texture2D Card { get; private set; }

    [field: SerializeField]
    [field: Tooltip("RGB recoloring mask paired with the card: red for leaves, green for stems and blue for flowers or seeds.")]
    public Texture2D Mask { get; private set; }

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

    [field: SerializeField] public Color LeavesColor { get; private set; } = new(0.42f, 0.5f, 0.24f);
    [field: SerializeField] public Color StemsColor { get; private set; } = new(0.32f, 0.27f, 0.15f);
    [field: SerializeField] public Color FlowersColor { get; private set; } = new(0.8f, 0.62f, 0.3f);

    [field: SerializeField]
    [field: Tooltip("Multiplier applied over the height of the card, strongest at the tip. White is neutral; anything below it darkens.")]
    public Color TipTint { get; private set; } = Color.white;

    [field: SerializeField, Range(0f, 1f)] public float SnowAmount { get; private set; }

    [field: SerializeField, Range(0f, 0.8f)]
    [field: Tooltip("Darkening of the lower part of the card, stems included.")]
    public float RootDarkening { get; private set; } = 0.15f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("How much the grey facets of the white texture multiply the recoloured plant. Zero gives flat colour, one gives the full texture.")]
    public float TextureShading { get; private set; } = 0.85f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Sun coming through the blade from behind.")]
    public float Translucency { get; private set; } = 0.18f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("Softness of the diffuse wrap, so a card never reaches full black.")]
    public float WrapLight { get; private set; } = 0.25f;

    [field: SerializeField, Range(0f, 0.5f)] public float WindStrength { get; private set; } = 0.08f;
    [field: SerializeField, Range(0f, 6f)] public float WindSpeed { get; private set; } = 1.4f;

    [field: SerializeField, Range(0f, 0.02f)]
    [field: Tooltip("Texture flutter in UV, the small trembling on top of the sway.")]
    public float Flutter { get; private set; } = 0.0025f;

    [field: SerializeField, Range(0f, 2f)]
    [field: Tooltip("How far the card normal is bowed away from the flat quad.")]
    public float Roundness { get; private set; } = 0.7f;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("How far the card normal is blended toward up. Higher makes the tuft read as ground cover rather than as a standing sheet.")]
    public float NormalUp { get; private set; } = 0.3f;

    [field: SerializeField, Range(0f, 0.35f)] public float ColorVariation { get; private set; } = 0.08f;

    public bool IsValid => Card != null || Prefab != null;

    public bool IsUnset => Density <= 0f && PatchFrequency <= 0f && MaxSlope <= 0f && MaxWidth <= 0f && MaxHeight <= 0f;

    public bool IsMute => Density <= 0f || MaxSlope <= 0f || MaxWidth <= 0f || MaxHeight <= 0f;

    public void ApplyDefaults(BiomeType biome = BiomeType.PineForest)
    {
        Density = 1.5f;
        PatchFrequency = 24f;
        PatchThreshold = 0.35f;
        MaxSlope = 32f;
        MinWidth = 0.6f;
        MaxWidth = 1.4f;
        MinHeight = 0.5f;
        MaxHeight = 1.2f;
        LeavesColor = new Color(0.42f, 0.5f, 0.24f);
        StemsColor = new Color(0.32f, 0.27f, 0.15f);
        FlowersColor = new Color(0.8f, 0.62f, 0.3f);
        TipTint = Color.white;
        SnowAmount = 0f;
        RootDarkening = 0.15f;
        TextureShading = 0.85f;
        Translucency = 0.18f;
        WrapLight = 0.25f;
        WindStrength = 0.08f;
        WindSpeed = 1.4f;
        Flutter = 0.0025f;
        Roundness = 0.7f;
        NormalUp = 0.3f;
        ColorVariation = 0.08f;

        switch (biome)
        {
            case BiomeType.BurntForest:
                LeavesColor = new Color(0.333333f, 0.290196f, 0.243137f);
                StemsColor = new Color(0.188235f, 0.168627f, 0.156863f);
                FlowersColor = new Color(0.545098f, 0.474510f, 0.380392f);
                RootDarkening = 0.32f;
                Translucency = 0.035f;
                TextureShading = 0.7f;
                break;
            case BiomeType.Desert:
                LeavesColor = new Color(0.717647f, 0.631373f, 0.454902f);
                StemsColor = new Color(0.486275f, 0.407843f, 0.286275f);
                FlowersColor = new Color(0.839216f, 0.741176f, 0.541176f);
                break;
            case BiomeType.Snow:
                LeavesColor = new Color(0.588235f, 0.615686f, 0.619608f);
                StemsColor = new Color(0.384314f, 0.415686f, 0.431373f);
                FlowersColor = new Color(0.760784f, 0.788235f, 0.8f);
                SnowAmount = 0.7f;
                Translucency = 0.08f;
                break;
            case BiomeType.Wasteland:
                LeavesColor = new Color(0.552941f, 0.470588f, 0.349020f);
                StemsColor = new Color(0.352941f, 0.298039f, 0.231373f);
                FlowersColor = new Color(0.682353f, 0.584314f, 0.423529f);
                break;
        }
    }

}

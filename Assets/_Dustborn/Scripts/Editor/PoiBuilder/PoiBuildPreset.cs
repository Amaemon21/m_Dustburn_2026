using System;
using UnityEngine;

public sealed class PoiBuildPreset : ScriptableObject
{
    [SerializeField] private PoiPresetSettings _settings = new();

    internal PoiPresetSettings CopySettings() => _settings.Copy();
    internal void SetSettings(PoiPresetSettings settings) => _settings = settings.Copy();
}

[Serializable]
internal sealed class PoiPresetSettings
{
    public PoiBuildingKind Kind;
    public PoiHouseSettings House = PoiHouseSettings.Example;
    public GameObject Cube;
    public GameObject Wall;
    public GameObject Doorway;
    public GameObject Window;
    public GameObject Slab;
    public GameObject Slope;
    public GameObject Gable;
    public Material FoundationMaterial;
    public Material FloorMaterial;
    public Material WallMaterial;
    public Material RoofMaterial;
    public Material GableMaterial;
    public Material CeilingMaterial;
    public Material InteriorWallMaterial;
    public Material TrimMaterial;
    public Material WainscotMaterial;
    public Material PorchMaterial;
    public Material PorchTimberMaterial;
    public Material GlazingMaterial;

    public PoiPresetSettings Copy() => (PoiPresetSettings)MemberwiseClone();

    public GameObject GetModel(PoiPart part) => part switch
    {
        PoiPart.Cube => Cube,
        PoiPart.Wall => Wall,
        PoiPart.Doorway => Doorway,
        PoiPart.Window => Window,
        PoiPart.Slab => Slab,
        PoiPart.Slope => Slope,
        PoiPart.Gable => Gable,
        _ => throw new ArgumentOutOfRangeException(nameof(part))
    };

    public Material GetMaterial(PoiSection section) => section switch
    {
        PoiSection.Foundation => FoundationMaterial,
        PoiSection.Floor or PoiSection.UpperFloor or PoiSection.Staircase => FloorMaterial,
        PoiSection.Walls => WallMaterial,
        PoiSection.Roof => RoofMaterial,
        PoiSection.Gables => GableMaterial,
        PoiSection.Ceiling => CeilingMaterial,
        PoiSection.WallInteriors or PoiSection.Partitions => InteriorWallMaterial,
        PoiSection.Trim => TrimMaterial,
        PoiSection.StairwellGuard => TrimMaterial,
        PoiSection.Wainscot => WainscotMaterial,
        PoiSection.Porch => PorchMaterial,
        PoiSection.PorchTimber => PorchTimberMaterial,
        PoiSection.Glazing => GlazingMaterial,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };
}

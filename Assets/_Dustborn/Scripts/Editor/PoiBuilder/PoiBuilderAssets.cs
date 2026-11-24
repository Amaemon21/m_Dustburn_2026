using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class PoiBuilderAssets
{
    public const string PRESETS = "Assets/_Dustborn/Content/Configs/World/PoiPresets";
    public const string PREFABS = "Assets/_Dustborn/Content/Prefabs/Pois";
    public const string EXAMPLE_NAME = "House_01";
    public const string STUDY_NAME = "Study_01";
    private const string MODELS = "Assets/_Dustborn/Content/Models/Blocks/";
    private const string POI_MATERIALS = "Assets/_Dustborn/Content/Models/POI/Materials/";
    private const string PACKED_MATERIALS = MODELS + "POI/Materials/";
    private const string APOCALYPSE_PROPS = "Assets/Assets/Synty/PolygonApocalypse/Prefabs/Props/";
    private const string GENERIC_PROPS = "Assets/Assets/Synty/PolygonGeneric/Prefabs/Props/";

    public static PoiPresetSettings CreateExample() => new()
    {
        Cube = LoadModel("Blocks"),
        Wall = LoadModel("Wall"),
        Doorway = LoadModel("Doorway"),
        Window = LoadModel("Window"),
        Slab = LoadModel("Slab"),
        Slope = LoadModel("RoofSlope45"),
        Gable = LoadModel("Gable45"),
        FoundationMaterial = LoadMaterial("foundation_concrete"),
        FloorMaterial = LoadMaterial("floorboards"),
        WallMaterial = LoadMaterial("siding_green"),
        RoofMaterial = LoadMaterial("roof_shingles"),
        GableMaterial = LoadMaterial("siding_green"),
        CeilingMaterial = LoadFinishMaterial(PoiSection.Ceiling),
        InteriorWallMaterial = LoadFinishMaterial(PoiSection.WallInteriors),
        TrimMaterial = LoadFinishMaterial(PoiSection.Trim),
        PorchMaterial = LoadFinishMaterial(PoiSection.Porch),
        PorchTimberMaterial = LoadFinishMaterial(PoiSection.PorchTimber),
        GlazingMaterial = LoadFinishMaterial(PoiSection.Glazing)
    };

    public static PoiPresetSettings CreateStudy()
    {
        PoiPresetSettings settings = CreateExample();
        settings.House.Depth = 6;
        settings.House.Wainscot = true;
        settings.House.StudyFurniture = true;
        settings.InteriorWallMaterial = LoadMaterial("study_plaster");
        settings.WainscotMaterial = LoadMaterial("study_wainscot");
        settings.FloorMaterial = LoadMaterial("study_floorboards");
        settings.CeilingMaterial = LoadMaterial("study_ceiling");
        return settings;
    }

    public static PoiBuildPreset[] FindPresets()
    {
        if (!AssetDatabase.IsValidFolder(PRESETS))
            return Array.Empty<PoiBuildPreset>();

        return AssetDatabase.FindAssets($"t:{nameof(PoiBuildPreset)}", new[] { PRESETS })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<PoiBuildPreset>)
            .Where(asset => asset != null)
            .OrderBy(asset => asset.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsValidName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length > 0 && !trimmed.EndsWith(".", StringComparison.Ordinal) &&
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static bool TryGetSavePath<T>(string folder, string name, string extension, out string path) where T : Object
    {
        EnsureFolder(folder);
        path = $"{folder}/{name.Trim()}.{extension}";
        Object existing = AssetDatabase.LoadMainAssetAtPath(path);
        bool exists = existing != null || File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path);

        if (!exists)
            return true;

        string title = PoiBuilderText.Get("nameConflict");
        string details = PoiBuilderText.Format("nameConflictBody", Path.GetFileName(path));
        bool replaceable = existing is T &&
            (existing is not GameObject prefab || PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Model);

        if (replaceable)
        {
            int action = EditorUtility.DisplayDialogComplex(title, details, PoiBuilderText.Get("saveCopy"),
                PoiBuilderText.Get("cancel"), PoiBuilderText.Get("replace"));

            if (action == 2)
                return true;

            if (action != 0)
                return false;
        }
        else if (!EditorUtility.DisplayDialog(title, details, PoiBuilderText.Get("saveCopy"), PoiBuilderText.Get("cancel")))
            return false;

        path = AssetDatabase.GenerateUniqueAssetPath(path);
        return true;
    }

    public static PoiBuildPreset SavePreset(PoiPresetSettings settings, string path)
    {
        var preset = AssetDatabase.LoadAssetAtPath<PoiBuildPreset>(path);

        if (preset == null)
        {
            preset = ScriptableObject.CreateInstance<PoiBuildPreset>();
            preset.SetSettings(settings);

            try
            {
                AssetDatabase.CreateAsset(preset, path);

                if (!EditorUtility.IsPersistent(preset))
                    throw new IOException($"Could not create the POI preset at '{path}'.");
            }
            catch
            {
                if (!EditorUtility.IsPersistent(preset))
                    Object.DestroyImmediate(preset);

                throw;
            }
        }
        else
        {
            Undo.RecordObject(preset, "Save POI Preset");
            preset.SetSettings(settings);
            EditorUtility.SetDirty(preset);
        }

        AssetDatabase.SaveAssetIfDirty(preset);
        return preset;
    }

    public static GameObject LoadModel(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(MODELS + name + ".fbx");

    public static GameObject LoadFurniture(PoiPart part) => AssetDatabase.LoadAssetAtPath<GameObject>(part switch
    {
        PoiPart.StudyDesk => APOCALYPSE_PROPS + "SM_Prop_Study_Desk_01.prefab",
        PoiPart.StudyChair => APOCALYPSE_PROPS + "SM_Prop_Study_Chair_01.prefab",
        PoiPart.StudyLamp => APOCALYPSE_PROPS + "SM_Prop_Study_Lamp_01.prefab",
        PoiPart.StudyClock => GENERIC_PROPS + "SM_Gen_Prop_Clock_01.prefab",
        _ => throw new ArgumentOutOfRangeException(nameof(part))
    });

    public static Material LoadFinishMaterial(PoiSection section) => LoadMaterial(section switch
    {
        PoiSection.Trim or PoiSection.StairwellGuard => "trim_cream",
        PoiSection.Foundation => "foundation_concrete",
        PoiSection.Porch => "floorboards",
        PoiSection.PorchTimber => "porch_timber",
        PoiSection.Glazing => "window_glass",
        PoiSection.Wainscot => "study_wainscot",
        _ => "plaster"
    });

    private static Material LoadMaterial(string name)
    {
        string file = "m_poi_house_" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(POI_MATERIALS + file);

        if (material != null)
            return material;

        return AssetDatabase.LoadAssetAtPath<Material>(PACKED_MATERIALS + file);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        string folder = path.Substring(parent.Length + 1);

        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, folder)) || !AssetDatabase.IsValidFolder(path))
            throw new IOException($"Could not create the POI asset folder '{path}'.");
    }
}

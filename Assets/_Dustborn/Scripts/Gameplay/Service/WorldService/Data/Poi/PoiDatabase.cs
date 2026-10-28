using System.Collections.Generic;
using System.Text;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PoiDatabase", menuName = "World/POI Database")]
public class PoiDatabase : ScriptableObject
{
    public const string RESOURCES_PATH = "PoiDatabase";

    [SerializeField, HorizontalLine] private List<PoiDefinition> _definitions = new();

    public int Count => _definitions.Count;

    public IReadOnlyList<PoiDefinition> Definitions => _definitions;

    [ShowNativeProperty] public string Coverage => DescribeCoverage();

    private void OnValidate()
    {
        _definitions ??= new List<PoiDefinition>();
        ListDuplicates.Clear(_definitions, definition => definition, index => _definitions[index] = null, this, "Definitions");
        ListDuplicates.Warn(_definitions, definition => definition.Prefab, this, "Definitions (by Prefab)");
    }

    public bool IsValid()
    {
        if (_definitions.Count == 0)
        {
            Debug.LogError("PoiDatabase is empty: add a PoiDefinition or the POI layer will place nothing", this);
            return false;
        }

        for (int i = 0; i < _definitions.Count; i++)
        {
            if (_definitions[i] == null)
            {
                Debug.LogError($"PoiDatabase: slot {i} is empty, assign a PoiDefinition or drop the row", this);
                return false;
            }

            if (_definitions[i].Prefab == null)
            {
                Debug.LogError($"PoiDefinition {_definitions[i].name}: Prefab is not assigned", _definitions[i]);
                return false;
            }
        }

        return true;
    }

#if UNITY_EDITOR
    [Button("Measure Footprints From Prefabs")]
    private void MeasureFootprints()
    {
        int measured = 0;
        int skipped = 0;

        foreach (PoiDefinition definition in _definitions)
        {
            if (definition == null || definition.Prefab == null)
                continue;

            if (!PrefabBounds.TryMeasure(definition.Prefab, out Bounds bounds))
            {
                Debug.LogWarning($"{definition.name}: prefab {definition.Prefab.name} has no MeshFilter with a mesh, its size is left as is", definition);
                skipped++;
                continue;
            }

            definition.EditorSetMeasurements(bounds.size.x, bounds.size.z, new Vector2(bounds.center.x, bounds.center.z), -bounds.min.y);

            UnityEditor.EditorUtility.SetDirty(definition);
            measured++;
        }

        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"Measured {measured} POI, skipped {skipped}. Footprint comes from the prefab meshes, PivotOffset is the offset of the footprint centre from the origin, GroundOffset lifts the bottom of the model onto the pad", this);
    }
#endif

    public bool HasDistrict(DistrictType district)
    {
        foreach (PoiDefinition definition in _definitions)
        {
            if (definition != null && definition.District == district)
                return true;
        }

        return false;
    }

    public float SmallestWidth(DistrictType district)
    {
        float smallest = float.MaxValue;

        foreach (PoiDefinition definition in _definitions)
        {
            if (definition == null || definition.District != district)
                continue;

            if (definition.FootprintWidth < smallest)
                smallest = definition.FootprintWidth;
        }

        return smallest;
    }

    public float SmallestDepth(DistrictType district)
    {
        float smallest = float.MaxValue;

        foreach (PoiDefinition definition in _definitions)
        {
            if (definition == null || definition.District != district)
                continue;

            if (definition.FootprintDepth < smallest)
                smallest = definition.FootprintDepth;
        }

        return smallest;
    }

    private string DescribeCoverage()
    {
        if (_definitions.Count == 0)
            return "empty, add a PoiDefinition";

        var counts = new Dictionary<DistrictType, int>();

        foreach (PoiDefinition definition in _definitions)
        {
            if (definition == null)
                continue;

            counts.TryGetValue(definition.District, out int count);
            counts[definition.District] = count + 1;
        }

        var text = new StringBuilder();

        foreach (DistrictType district in System.Enum.GetValues(typeof(DistrictType)))
        {
            counts.TryGetValue(district, out int count);

            if (text.Length > 0)
                text.Append(", ");

            text.Append(count > 0 ? $"{district}: {count}" : $"{district}: none");
        }

        return text.ToString();
    }
}

using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PoiPlacement", menuName = "World/POI Placement")]
public class PoiPlacementAsset : ScriptableObject
{
    [SerializeField] private List<PoiPlacement> _placements = new();

    public IReadOnlyList<PoiPlacement> Placements => _placements;

    [ShowNativeProperty] public string PlacementStatus => Describe();

#if UNITY_EDITOR
    public void EditorSetup(IEnumerable<PoiPlacement> placements)
    {
        _placements = new List<PoiPlacement>(placements);
    }
#endif

    private string Describe()
    {
        if (_placements.Count == 0)
            return "empty, run the POI generation";

        var counts = new Dictionary<DistrictType, int>();
        int missingPrefabs = 0;

        foreach (PoiPlacement placement in _placements)
        {
            counts.TryGetValue(placement.District, out int count);
            counts[placement.District] = count + 1;

            if (placement.Prefab == null)
                missingPrefabs++;
        }

        var text = new System.Text.StringBuilder($"{_placements.Count} items");

        foreach (KeyValuePair<DistrictType, int> pair in counts)
            text.Append($", {pair.Key}: {pair.Value}");

        if (missingPrefabs > 0)
            text.Append($". NO PREFAB: {missingPrefabs} — the PoiDefinition points at a scene object instead of a project prefab");

        return text.ToString();
    }
}

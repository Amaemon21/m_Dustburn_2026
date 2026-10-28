using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PoiPlacement", menuName = "World/POI Placement")]
public class PoiPlacementAsset : ScriptableObject
{
    [SerializeField] private List<PoiPlacement> _placements = new();
    [SerializeField] private List<PropPlacement> _props = new();

    public IReadOnlyList<PoiPlacement> Placements => _placements;
    public IReadOnlyList<PropPlacement> Props => _props ??= new List<PropPlacement>();

    [ShowNativeProperty] public string PlacementStatus => Describe();

    public static PoiPlacementAsset Create(IEnumerable<PoiPlacement> placements, IEnumerable<PropPlacement> props)
    {
        var asset = CreateInstance<PoiPlacementAsset>();
        asset._placements = new List<PoiPlacement>(placements);
        asset._props = new List<PropPlacement>(props ?? new List<PropPlacement>());
        return asset;
    }

#if UNITY_EDITOR
    public void EditorSetup(IEnumerable<PoiPlacement> placements, IEnumerable<PropPlacement> props)
    {
        _placements = new List<PoiPlacement>(placements);
        _props = new List<PropPlacement>(props ?? new List<PropPlacement>());
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

        var text = new System.Text.StringBuilder($"{_placements.Count} items, {Props.Count} props");

        foreach (KeyValuePair<DistrictType, int> pair in counts)
            text.Append($", {pair.Key}: {pair.Value}");

        if (missingPrefabs > 0)
            text.Append($". NO PREFAB: {missingPrefabs} — the PoiDefinition points at a scene object instead of a project prefab");

        return text.ToString();
    }
}

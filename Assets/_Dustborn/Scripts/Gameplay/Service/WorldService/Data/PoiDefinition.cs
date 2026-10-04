using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PoiDefinition", menuName = "World/POI Definition")]
public class PoiDefinition : ScriptableObject
{
    [field: SerializeField, BoxGroup("Prefab"), ShowAssetPreview] public GameObject Prefab { get; private set; }

    [field: SerializeField, BoxGroup("Prefab")] public float GroundOffset { get; private set; }

    [field: SerializeField, BoxGroup("Prefab")] public Vector2 PivotOffset { get; private set; }

    [field: SerializeField, BoxGroup("Placement")] public DistrictType District { get; private set; }

    [field: SerializeField, BoxGroup("Placement"), MinValue(2f)] public float FootprintWidth { get; private set; } = 14f;
    [field: SerializeField, BoxGroup("Placement"), MinValue(2f)] public float FootprintDepth { get; private set; } = 12f;
    [field: SerializeField, BoxGroup("Placement"), MinValue(0.01f)] public float Weight { get; private set; } = 1f;

    [field: SerializeField, BoxGroup("Limits"), MinValue(0)]
    [field: Tooltip("How many of these one settlement may hold. Zero means no cap. Weight only sets how often the building is drawn, this is what stops five churches on one street.")]
    public int MaxPerSettlement { get; private set; }

    [field: SerializeField, BoxGroup("Limits"), MinValue(0)]
    [field: Tooltip("How many of these the whole world may hold. Zero means no cap. Set it to one for a landmark that has to stay unique.")]
    public int MaxPerWorld { get; private set; }

    public Vector2 Footprint => new(FootprintWidth, FootprintDepth);

    [ShowNativeProperty] public float FootprintArea => FootprintWidth * FootprintDepth;

#if UNITY_EDITOR
    public void EditorSetup(GameObject prefab, DistrictType district, float width, float depth, float weight)
    {
        Prefab = prefab;
        District = district;
        FootprintWidth = width;
        FootprintDepth = depth;
        Weight = weight;
    }

    public void EditorSetMeasurements(float width, float depth, Vector2 pivotOffset, float groundOffset)
    {
        FootprintWidth = width;
        FootprintDepth = depth;
        PivotOffset = pivotOffset;
        GroundOffset = groundOffset;
    }
#endif
}
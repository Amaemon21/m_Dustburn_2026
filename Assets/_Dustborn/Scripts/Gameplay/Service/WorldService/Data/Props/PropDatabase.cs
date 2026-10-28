using System;
using System.Collections.Generic;
using System.Text;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "PropDatabase", menuName = "World/Prop Database")]
public class PropDatabase : ScriptableObject
{
    [SerializeField, HorizontalLine] private List<PropDefinition> _definitions = new();

    [field: SerializeField, BoxGroup("Roads"), Min(0f)] public float HighwayPer100m { get; private set; } = 2f;
    [field: SerializeField, BoxGroup("Roads"), Min(0f)] public float DirtPer100m { get; private set; } = 1f;
    [field: SerializeField, BoxGroup("Settlements"), Min(0f)] public float StreetPer100m { get; private set; } = 3f;
    [field: SerializeField, BoxGroup("Settlements"), Min(0)] public int YardMin { get; private set; } = 2;
    [field: SerializeField, BoxGroup("Settlements"), Min(0)] public int YardMax { get; private set; } = 5;
    [field: SerializeField, BoxGroup("Settlements"), Min(0f)] public float YardDepth { get; private set; } = 6f;
    [field: SerializeField, BoxGroup("Wild"), Min(0f)] public float WildPerKm2 { get; private set; } = 40f;
    [field: SerializeField, BoxGroup("Runtime"), Range(0.001f, 0.2f)] public float CullScreenHeight { get; private set; } = 0.012f;

    public IReadOnlyList<PropDefinition> Definitions => _definitions;

    [ShowNativeProperty] public string Coverage => DescribeCoverage();

    public float WidestRadius
    {
        get
        {
            float widest = 0f;
            foreach (PropDefinition definition in _definitions)
                if (definition != null && definition.Prefab != null)
                    widest = Mathf.Max(widest, definition.Radius);
            return widest;
        }
    }

    private void OnValidate()
    {
        _definitions ??= new List<PropDefinition>();
        YardMax = Mathf.Max(YardMin, YardMax);

        foreach (PropDefinition definition in _definitions)
        {
            if (definition == null)
                continue;
            if (definition.IsUnset)
                definition.ApplyDefaults();
            definition.Validate();
        }

        ListDuplicates.Clear(_definitions, definition => definition.Prefab, index => _definitions[index].ClearPrefab(), this, "Definitions");
    }

#if UNITY_EDITOR
    public bool EditorContains(GameObject prefab) => _definitions.Exists(definition => definition != null && definition.Prefab == prefab);

    public void EditorAdd(PropDefinition definition)
    {
        if (definition?.Prefab == null || EditorContains(definition.Prefab))
            return;

        _definitions.Add(definition);
    }

    [Button("Measure Props From Prefabs")]
    public void MeasurePrefabs()
    {
        int measured = 0;

        foreach (PropDefinition definition in _definitions)
        {
            if (definition == null || definition.Prefab == null || !PrefabBounds.TryMeasure(definition.Prefab, out Bounds bounds))
                continue;

            Vector2 centre = new(bounds.center.x, bounds.center.z);
            float radius = centre.magnitude + new Vector2(bounds.size.x, bounds.size.z).magnitude * 0.5f;
            definition.SetMeasurements(radius, -bounds.min.y);
            measured++;
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"Measured {measured} props: Radius is the circle around the mesh seen from above, GroundOffset lifts the bottom of the model onto the ground", this);
    }
#endif

    private string DescribeCoverage()
    {
        var counts = new Dictionary<PropContext, int>();
        int missing = 0;

        foreach (PropDefinition definition in _definitions)
        {
            if (definition == null || definition.Prefab == null)
            {
                missing++;
                continue;
            }

            foreach (PropContext context in Enum.GetValues(typeof(PropContext)))
            {
                if (!definition.Fits(context))
                    continue;
                counts.TryGetValue(context, out int count);
                counts[context] = count + 1;
            }
        }

        var text = new StringBuilder();

        foreach (PropContext context in Enum.GetValues(typeof(PropContext)))
        {
            counts.TryGetValue(context, out int count);
            if (text.Length > 0)
                text.Append(", ");
            text.Append($"{context}: {count}");
        }

        if (missing > 0)
            text.Append($", without prefab: {missing}");

        return text.ToString();
    }
}

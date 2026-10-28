using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public class GroundLayerSet : ScriptableObject
{
    [field: SerializeField, ReadOnly] public Material Material { get; private set; }
    [field: SerializeField, ReadOnly] public List<TerrainLayer> Layers { get; private set; } = new();
    [field: SerializeField, ReadOnly] public int ControlResolution { get; private set; }

    public bool IsValid => Material != null && Layers.Count > 0 && ControlResolution > 0 && !Layers.Contains(null);

#if UNITY_EDITOR
    public void EditorSetup(Material material, IEnumerable<TerrainLayer> layers, int controlResolution)
    {
        Material = material;
        Layers = new List<TerrainLayer>(layers);
        ControlResolution = controlResolution;
    }
#endif
}

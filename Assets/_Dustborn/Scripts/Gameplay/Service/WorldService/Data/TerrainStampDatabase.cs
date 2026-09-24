using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TerrainStampDatabase", menuName = "World/Terrain Stamp Database")]
public class TerrainStampDatabase : ScriptableObject
{
    [SerializeField] private List<TerrainStampDefinition> _stamps = new();

    public IReadOnlyList<TerrainStampDefinition> Stamps => _stamps;

    public int Count => _stamps.Count;

    public TerrainStampDefinition Get(int index)
    {
        return _stamps[index];
    }

    public void Replace(IEnumerable<TerrainStampDefinition> stamps)
    {
        _stamps = new List<TerrainStampDefinition>(stamps);
    }
}

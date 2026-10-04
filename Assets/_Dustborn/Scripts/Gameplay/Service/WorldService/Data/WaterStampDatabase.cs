using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WaterStampDatabase", menuName = "World/Water Stamp Database")]
public class WaterStampDatabase : ScriptableObject
{
    [SerializeField] private List<WaterStampDefinition> _stamps = new();

    public IReadOnlyList<WaterStampDefinition> Stamps => _stamps;

    public int Count => _stamps.Count;

    public WaterStampDefinition Get(int index)
    {
        return _stamps[index];
    }

    public void Replace(IEnumerable<WaterStampDefinition> stamps)
    {
        _stamps = new List<WaterStampDefinition>(stamps);
    }
}

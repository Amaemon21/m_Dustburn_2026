using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "RoadNetwork", menuName = "World/Road Network")]
public class RoadNetworkAsset : ScriptableObject
{
    [SerializeField] private List<Hub> _hubs = new();
    [SerializeField] private List<Road> _roads = new();
    [SerializeField] private List<Road> _streets = new();

    public IReadOnlyList<Hub> Hubs => _hubs;
    public IReadOnlyList<Road> Roads => _roads;
    public IReadOnlyList<Road> Streets => _streets;

    [ShowNativeProperty] public string NetworkStatus => $"{_hubs.Count} settlements, {_roads.Count} roads, {_streets.Count} streets";

    public List<Road> Paved()
    {
        var paved = new List<Road>(_roads.Count + _streets.Count);

        paved.AddRange(_roads);
        paved.AddRange(_streets);

        return paved;
    }

#if UNITY_EDITOR
    public void EditorSetup(RoadNetwork network)
    {
        _hubs = new List<Hub>(network.Hubs);
        _roads = new List<Road>(network.Roads);
        _streets = new List<Road>(network.Streets);
    }
#endif
}

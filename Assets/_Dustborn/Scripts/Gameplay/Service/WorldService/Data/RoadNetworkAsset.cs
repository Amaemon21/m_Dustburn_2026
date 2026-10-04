using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[CreateAssetMenu(fileName = "RoadNetwork", menuName = "World/Road Network")]
public class RoadNetworkAsset : ScriptableObject
{
    [SerializeField] private List<Hub> _hubs = new();
    [SerializeField] private List<Road> _roads = new();
    [SerializeField] private List<Road> _streets = new();
    [SerializeField] private List<RoadJunction> _junctions = new();
    [SerializeField] private List<SettlementRecord> _settlements = new();

    public IReadOnlyList<Hub> Hubs => _hubs;
    public IReadOnlyList<Road> Roads => _roads;
    public IReadOnlyList<Road> Streets => _streets;
    public IReadOnlyList<RoadJunction> Junctions => _junctions;
    public IReadOnlyList<SettlementRecord> Settlements => _settlements;

    [ShowNativeProperty] public string NetworkStatus => $"{_hubs.Count} settlements, {Count(_roads, RoadKind.Highway)} highways, {Count(_roads, RoadKind.DirtAccess)} dirt roads, {Count(_streets, RoadKind.Arterial)} arterials, {Count(_streets, RoadKind.LocalStreet)} local streets, {_junctions.Count} nodes";

    public List<Road> Paved()
    {
        var paved = new List<Road>(_roads.Count + _streets.Count);

        paved.AddRange(_roads);
        paved.AddRange(_streets);

        return paved;
    }

    private static int Count(List<Road> roads, RoadKind kind)
    {
        int count = 0;

        foreach (Road road in roads)
        {
            if (road != null && road.Kind == kind)
                count++;
        }

        return count;
    }

#if UNITY_EDITOR
    public void EditorSetup(RoadNetwork network)
    {
        _hubs = new List<Hub>(network.Hubs);
        _roads = new List<Road>(network.Roads);
        _streets = new List<Road>(network.Streets);
        _junctions = new List<RoadJunction>(network.Junctions);
        _settlements = new List<SettlementRecord>(network.Settlements);
    }
#endif
}

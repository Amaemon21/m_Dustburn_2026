using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

public class RoadNetworkGizmo : MonoBehaviour
{
    [Required("Assets/_Dustborn/Generated/RoadNetwork.asset"), SerializeField]
    private RoadNetworkAsset _network;

    [SerializeField] private PoiPlacementAsset _placement;

    [SerializeField] private bool _drawHubs = true;
    [SerializeField] private bool _drawRoads = true;
    [SerializeField] private bool _drawStreets = true;
    [SerializeField] private bool _drawPois = true;
    [SerializeField] private float _drawHeight = 5f;

    [ShowNativeProperty]
    public string NetworkStatus => _network == null
        ? "asset is not assigned"
        : $"{_network.Hubs.Count} hubs, {_network.Roads.Count} roads, {_network.Streets.Count} streets";

    [ShowNativeProperty]
    public string PoiStatus => _placement == null
        ? "POI asset is not assigned"
        : $"{_placement.Placements.Count} points of interest";

#if UNITY_EDITOR
    private static readonly Color HubColor = new(0.2f, 0.9f, 1f);
    private static readonly Color RoadColor = new(1f, 0.35f, 0.2f);
    private static readonly Color StreetColor = new(1f, 0.85f, 0.25f);

    private void OnDrawGizmos()
    {
        Handles.zTest = CompareFunction.Always;

        if (_network != null)
        {
            if (_drawHubs)
                DrawHubs();

            if (_drawRoads)
                DrawLines(_network.Roads, RoadColor, 4f);

            if (_drawStreets)
                DrawLines(_network.Streets, StreetColor, 2f);
        }

        if (_placement != null && _drawPois)
            DrawPois();
    }

    private void DrawHubs()
    {
        Handles.color = HubColor;

        foreach (Hub hub in _network.Hubs)
        {
            Vector3 center = ToWorld(hub.Position);

            Handles.DrawWireDisc(center, Vector3.up, hub.Radius);
            Handles.DrawLine(center, center + Vector3.up * 40f);
        }
    }

    private void DrawLines(IReadOnlyList<Road> roads, Color color, float thickness)
    {
        Handles.color = color;

        foreach (Road road in roads)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            var points = new Vector3[road.Points.Length];

            for (int i = 0; i < points.Length; i++)
                points[i] = ToWorld(road.Points[i]);

            Handles.DrawAAPolyLine(thickness, points);
        }
    }

    private void DrawPois()
    {
        foreach (PoiPlacement placement in _placement.Placements)
        {
            Handles.color = ColorFor(placement.District);

            Vector3 center = transform.position + placement.Position + Vector3.up * 0.5f;
            Vector3 forward = Quaternion.Euler(0f, placement.Rotation, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 halfWidth = right * (placement.Footprint.x * 0.5f);
            Vector3 halfDepth = forward * (placement.Footprint.y * 0.5f);

            var corners = new[]
            {
                center - halfWidth - halfDepth,
                center + halfWidth - halfDepth,
                center + halfWidth + halfDepth,
                center - halfWidth + halfDepth,
                center - halfWidth - halfDepth
            };

            Handles.DrawAAPolyLine(2f, corners);
            Handles.DrawLine(center + halfDepth, center + halfDepth + forward * 4f);
        }
    }

    private static Color ColorFor(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => new Color(1f, 0.45f, 0.85f),
            DistrictType.Residential => new Color(0.45f, 0.9f, 0.5f),
            DistrictType.Industrial => new Color(1f, 0.62f, 0.24f),
            _ => new Color(0.78f, 0.78f, 1f)
        };
    }

    private Vector3 ToWorld(Vector2 position)
    {
        return transform.position + new Vector3(position.x, _drawHeight, position.y);
    }
#endif
}

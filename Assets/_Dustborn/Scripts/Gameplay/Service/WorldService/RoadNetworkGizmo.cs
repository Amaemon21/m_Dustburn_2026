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
    [SerializeField] private bool _drawTiles = true;
    [SerializeField] private bool _drawJunctions = true;
    [SerializeField] private bool _drawPois = true;
    [SerializeField] private float _drawHeight = 5f;

    [ShowNativeProperty]
    public string NetworkStatus => _network == null ? "asset is not assigned" : _network.NetworkStatus;

    [ShowNativeProperty]
    public string PoiStatus => _placement == null
        ? "POI asset is not assigned"
        : $"{_placement.Placements.Count} points of interest";

#if UNITY_EDITOR
    private static readonly Color HubColor = new(0.2f, 0.9f, 1f);
    private static readonly Color GatewayColor = new(1f, 0.25f, 1f);
    private static readonly Color JunctionColor = new(0.3f, 1f, 1f);

    private void OnDrawGizmos()
    {
        Handles.zTest = CompareFunction.Always;

        if (_network != null)
        {
            if (_drawTiles)
                DrawSettlements();

            if (_drawHubs)
                DrawHubs();

            if (_drawRoads)
                DrawLines(_network.Roads);

            if (_drawStreets)
                DrawLines(_network.Streets);

            if (_drawJunctions)
                DrawJunctions();
        }

        if (_placement != null && _drawPois)
            DrawPois();
    }

    private void DrawHubs()
    {
        foreach (Hub hub in _network.Hubs)
        {
            Vector3 center = ToWorld(hub.Position);

            Handles.color = HubColor;
            Handles.DrawWireDisc(center, Vector3.up, hub.Radius);
            Handles.DrawLine(center, center + Vector3.up * 40f);
            Handles.Label(center + Vector3.up * 44f, hub.Type.ToString());
        }
    }

    private void DrawSettlements()
    {
        foreach (SettlementRecord settlement in _network.Settlements)
        {
            Vector2 axisU = settlement.AxisU;
            Vector2 axisV = settlement.AxisV;
            float half = settlement.TileSize * 0.5f;

            foreach (TileRecord tile in settlement.Tiles)
            {
                Vector2 center = settlement.TileCenter(tile);

                Handles.color = ColorFor(tile.District) * 0.7f;
                Handles.DrawAAPolyLine(1.5f, new[]
                {
                    ToWorld(center - axisU * half - axisV * half),
                    ToWorld(center + axisU * half - axisV * half),
                    ToWorld(center + axisU * half + axisV * half),
                    ToWorld(center - axisU * half + axisV * half),
                    ToWorld(center - axisU * half - axisV * half)
                });
            }

            Handles.color = GatewayColor;

            foreach (GatewayRecord gateway in settlement.Gateways)
            {
                Vector3 port = ToWorld(gateway.Position);
                Vector3 tip = ToWorld(gateway.Position + gateway.Tangent * 30f);

                Handles.DrawAAPolyLine(5f, new[] { port, tip });
                Handles.DrawWireDisc(port, Vector3.up, 6f);
            }
        }
    }

    private void DrawJunctions()
    {
        foreach (RoadJunction junction in _network.Junctions)
        {
            Handles.color = junction.Kind == RoadNodeKind.Gateway ? GatewayColor : JunctionColor;
            Handles.DrawWireDisc(ToWorld(junction.Position), Vector3.up, junction.Kind == RoadNodeKind.Terminal ? 3f : 5f + junction.Degree);
        }
    }

    private void DrawLines(IReadOnlyList<Road> roads)
    {
        foreach (Road road in roads)
        {
            if (road.Points == null || road.Points.Length < 2)
                continue;

            var points = new Vector3[road.Points.Length];

            for (int i = 0; i < points.Length; i++)
                points[i] = ToWorld(road.Points[i]);

            Handles.color = ColorFor(road.Kind);
            Handles.DrawAAPolyLine(ThicknessFor(road.Kind), points);
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

    private static Color ColorFor(RoadKind kind)
    {
        return kind switch
        {
            RoadKind.Highway => new Color(1f, 0.35f, 0.2f),
            RoadKind.Arterial => new Color(1f, 0.8f, 0.2f),
            RoadKind.LocalStreet => new Color(1f, 0.95f, 0.6f),
            _ => new Color(0.65f, 0.45f, 0.25f)
        };
    }

    private static float ThicknessFor(RoadKind kind)
    {
        return kind switch
        {
            RoadKind.Highway => 5f,
            RoadKind.Arterial => 3.5f,
            RoadKind.LocalStreet => 2f,
            _ => 2f
        };
    }

    private static Color ColorFor(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => new Color(1f, 0.45f, 0.85f),
            DistrictType.Commercial => new Color(0.4f, 0.7f, 1f),
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

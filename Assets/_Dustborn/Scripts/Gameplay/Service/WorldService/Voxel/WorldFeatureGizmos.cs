using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class WorldFeatureGizmos : MonoBehaviour
{
    private const string PREF_KEY = "Dustborn.WorldFeatureGizmos";

    [SerializeField] private List<TerrainStampPlacement> _stamps = new();
    [SerializeField] private TextAsset _water;

    public static void Attach(Transform parent, IEnumerable<TerrainStampPlacement> stamps, TextAsset water)
    {
        var holder = new GameObject("World Features");
        holder.transform.SetParent(parent, false);

        var gizmos = holder.AddComponent<WorldFeatureGizmos>();
        gizmos._stamps = new List<TerrainStampPlacement>(stamps ?? new List<TerrainStampPlacement>());
        gizmos._water = water;
    }

#if UNITY_EDITOR
    private static readonly Color AddColor = new(1f, 0.45f, 0.2f);
    private static readonly Color SubtractColor = new(0.3f, 0.6f, 1f);
    private static readonly Color RiverColor = new(0.1f, 0.75f, 1f);
    private static readonly Color LakeColor = new(0.2f, 0.45f, 1f);

    private static bool? _visible;

    private WaterMap _decoded;

    public static bool Visible
    {
        get => _visible ??= EditorPrefs.GetBool(PREF_KEY, false);
        set
        {
            _visible = value;
            EditorPrefs.SetBool(PREF_KEY, value);
        }
    }

    private void OnDrawGizmos()
    {
        if (!Visible)
            return;

        Handles.matrix = transform.localToWorldMatrix;

        foreach (TerrainStampPlacement stamp in _stamps)
            DrawStamp(stamp);

        DrawWater();

        Handles.matrix = Matrix4x4.identity;
    }

    private static void DrawStamp(TerrainStampPlacement stamp)
    {
        const float LIFT = 420f;

        Handles.color = stamp.Operation == TerrainStampOperation.Add ? AddColor : SubtractColor;

        var corners = new Vector3[5];

        for (int i = 0; i < 4; i++)
        {
            float x = (i == 1 || i == 2 ? 0.5f : -0.5f) * stamp.Size.x;
            float z = (i >= 2 ? 0.5f : -0.5f) * stamp.Size.y;
            Vector2 world = stamp.World(x, z);

            corners[i] = new Vector3(world.x, LIFT, world.y);
        }

        corners[4] = corners[0];
        Handles.DrawAAPolyLine(4f, corners);

        Vector2 forward = stamp.World(0f, 0.5f * stamp.Size.y);
        Handles.DrawAAPolyLine(3f, new Vector3(stamp.Center.x, LIFT, stamp.Center.y), new Vector3(forward.x, LIFT, forward.y));

        Handles.Label(new Vector3(stamp.Center.x, LIFT, stamp.Center.y),
            $"{stamp.Name}\n{(stamp.Operation == TerrainStampOperation.Add ? "поднимает" : "опускает")} на {stamp.Amplitude:0} м\n"
            + $"центр ({stamp.Center.x:0}, {stamp.Center.y:0}), поворот {stamp.Rotation:0}°");
    }

    private void DrawWater()
    {
        if (_water == null)
            return;

        _decoded ??= WaterMap.Load(_water);

        Handles.color = RiverColor;

        foreach (RiverPath river in _decoded.Rivers)
        {
            var line = new Vector3[river.Points.Count];

            for (int i = 0; i < line.Length; i++)
                line[i] = new Vector3(river.Points[i].Position.x, river.Points[i].Surface + 0.5f, river.Points[i].Position.y);

            Handles.DrawAAPolyLine(3f, line);
        }

        Handles.color = LakeColor;

        foreach (WaterBody body in _decoded.Bodies)
        {
            var center = new Vector3(body.Center.x, body.Surface, body.Center.y);

            Handles.DrawWireDisc(center, Vector3.up, Mathf.Sqrt(body.Area / Mathf.PI));
            Handles.Label(center, $"{(body.Kind == WaterKind.Lake ? "озеро" : "пруд")} {body.Area / 10000f:0.0} га, вода {body.Surface:0.0} м");
        }
    }
#endif
}

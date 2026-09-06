using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

public class UnitSpawnPoint : MonoBehaviour
{
    [field: SerializeField] public UnitType UnitType {get; private set;}
    [field: SerializeField] public int MaxUnits {get; private set;} = 1;
    [field: SerializeField] public float SpawnDelay {get; private set;} = 1f;

#if UNITY_EDITOR
    private const float MARKER_RADIUS = 0.4f;
    private const float MARKER_HEIGHT = 1.6f;

    private static readonly Color MarkerColor = new(0.4f, 0.9f, 1f);
    private static readonly Color WanderColor = new(0.25f, 0.7f, 1f);
    private static readonly Color MissingConfigColor = new(1f, 0.35f, 0.3f);

    private GUIStyle _labelStyle;
    private UnitConfig _cachedConfig;
    private UnitType _cachedType;

    private void OnDrawGizmos()
    {
        DrawMarker();
    }

    private void OnDrawGizmosSelected()
    {
        UnitConfig config = GetConfig();

        DrawWanderZone(config);
        DrawLabel(config);
    }

    private void DrawMarker()
    {
        Vector3 position = transform.position;
        Vector3 top = position + Vector3.up * MARKER_HEIGHT;

        Color color = GetConfig() != null ? MarkerColor : MissingConfigColor;

        Handles.zTest = CompareFunction.LessEqual;

        Handles.color = Fade(color, 0.9f);
        Handles.DrawWireDisc(position, Vector3.up, MARKER_RADIUS);
        Handles.DrawAAPolyLine(2f, position, top);

        Handles.color = color;
        Handles.ConeHandleCap(0, top, Quaternion.LookRotation(Vector3.down), MARKER_RADIUS, EventType.Repaint);

        Handles.zTest = CompareFunction.Always;
    }

    private void DrawWanderZone(UnitConfig config)
    {
        if (config == null || config.MaxDistance <= 0f)
            return;

        Vector3 position = transform.position;

        Handles.zTest = CompareFunction.LessEqual;

        Handles.color = Fade(WanderColor, 0.08f);
        Handles.DrawSolidDisc(position, Vector3.up, config.MaxDistance);

        Handles.color = Fade(WanderColor, 0.85f);
        Handles.DrawWireDisc(position, Vector3.up, config.MaxDistance);

        Handles.zTest = CompareFunction.Always;
    }

    private void DrawLabel(UnitConfig config)
    {
        _labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(6, 6, 3, 3),
            normal = { textColor = Color.white },
            richText = true
        };

        Color color = config != null ? MarkerColor : MissingConfigColor;
        string colorHex = ColorUtility.ToHtmlStringRGB(color);

        string details = config != null
            ? $"x{MaxUnits}  ·  {SpawnDelay:F1} s  ·  r {config.MaxDistance:F1} m"
            : "no UnitConfig in database";

        Handles.Label(transform.position + Vector3.up * (MARKER_HEIGHT + 0.6f),
            $"<b><color=#{colorHex}>{UnitType}</color></b>\n{details}", _labelStyle);
    }

    private UnitConfig GetConfig()
    {
        if (_cachedConfig != null && _cachedType == UnitType)
            return _cachedConfig;

        UnitDatabaseConfig database = Resources.Load<UnitDatabaseConfig>(UnitDatabaseConfig.RESOURCES_PATH);

        _cachedType = UnitType;
        _cachedConfig = database != null ? database.GetUnitConfigByType(UnitType) : null;

        return _cachedConfig;
    }

    private static Color Fade(Color color, float alpha)
    {
        color.a = alpha;

        return color;
    }
#endif
}

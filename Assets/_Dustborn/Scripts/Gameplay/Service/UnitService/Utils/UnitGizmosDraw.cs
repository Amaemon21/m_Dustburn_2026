using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

public class UnitGizmosDraw : MonoBehaviour
{
    [SerializeField] private UnitView _view;

    [Header("What to draw")]
    [SerializeField] private bool _drawWhenNotSelected = true;
    [SerializeField] private bool _drawSightCone = true;
    [SerializeField] private bool _drawWanderZone = true;
    [SerializeField] private bool _drawPath = true;
    [SerializeField] private bool _drawLabel = true;

    [Header("Style")]
    [SerializeField, Range(0f, 0.4f)] private float _fillAlpha = 0.12f;
    [SerializeField, Range(2, 12)] private int _rangeRings = 3;

#if UNITY_EDITOR
    private static readonly Color WanderColor = new(1f, 0.72f, 0.15f);
    private static readonly Color ChaseColor = new(1f, 0.25f, 0.2f);
    private static readonly Color SpottedColor = new(0.3f, 1f, 0.45f);
    private static readonly Color ZoneColor = new(0.25f, 0.7f, 1f);
    private static readonly Color PathColor = new(0.75f, 0.45f, 1f);

    private GUIStyle _labelStyle;

    private UnitController Controller => _view != null ? _view.Controller : null;

    private void OnDrawGizmos()
    {
        if (_drawWhenNotSelected && !IsSelected())
        {
            DrawCompact();
        }
    }

    private void OnDrawGizmosSelected()
    {
        DrawFull();
    }

    private bool IsSelected()
    {
        return Selection.activeGameObject == gameObject
               || (_view != null && Selection.activeGameObject == _view.gameObject);
    }

    private void DrawCompact()
    {
        if (!TryGetContext(out Vector3 center, out Vector3 forward))
            return;

        Vector3 left = Rotate(forward, -_view.Config.ViewAngle / 2f);

        Handles.color = Fade(GetStateColor(), 0.55f);
        Handles.zTest = CompareFunction.LessEqual;

        Handles.DrawWireArc(center, Vector3.up, left, _view.Config.ViewAngle, _view.Config.MaxDistance);
        Handles.DrawWireDisc(center, Vector3.up, 0.25f);

        Handles.zTest = CompareFunction.Always;
    }

    private void DrawFull()
    {
        if (!TryGetContext(out Vector3 center, out Vector3 forward))
            return;

        Handles.zTest = CompareFunction.LessEqual;

        if (_drawWanderZone)
            DrawWanderZone(center);

        if (_drawSightCone)
            DrawSightCone(center, forward);

        if (_drawPath)
            DrawDestination(center);

        if (_drawLabel)
            DrawLabel(center);

        Handles.zTest = CompareFunction.Always;
    }

    private void DrawSightCone(Vector3 center, Vector3 forward)
    {
        float fov = _view.Config.ViewAngle;
        float range = _view.Config.MaxDistance;

        if (range <= 0f || fov <= 0f)
            return;

        Vector3 left = Rotate(forward, -fov / 2f);
        Vector3 right = Rotate(forward, fov / 2f);

        Color stateColor = GetStateColor();

        Handles.color = Fade(stateColor, _fillAlpha);
        Handles.DrawSolidArc(center, Vector3.up, left, fov, range);

        Handles.color = Fade(stateColor, 0.22f);

        for (int i = 1; i < _rangeRings; i++)
        {
            float ringRadius = range * i / _rangeRings;

            Handles.DrawWireArc(center, Vector3.up, left, fov, ringRadius);
        }

        Handles.color = Fade(stateColor, 0.9f);
        Handles.DrawWireArc(center, Vector3.up, left, fov, range);
        Handles.DrawLine(center, center + left * range);
        Handles.DrawLine(center, center + right * range);

        DrawForwardArrow(center, forward, range);
    }

    private void DrawForwardArrow(Vector3 center, Vector3 forward, float range)
    {
        Vector3 tip = center + forward * (range * 0.55f);

        Handles.color = Color.white;
        Handles.DrawAAPolyLine(3f, center, tip);
        Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(forward), range * 0.12f, EventType.Repaint);
    }

    private void DrawWanderZone(Vector3 center)
    {
        if (_view.UnitCenter == null || _view.Config.MaxDistance <= 0f)
            return;

        Vector3 wanderCenter = _view.UnitCenter.position;

        Handles.color = Fade(ZoneColor, _fillAlpha * 0.7f);
        Handles.DrawSolidDisc(wanderCenter, Vector3.up, _view.Config.MaxDistance);

        Handles.color = Fade(ZoneColor, 0.85f);
        Handles.DrawWireDisc(wanderCenter, Vector3.up, _view.Config.MaxDistance);

        Handles.color = Fade(ZoneColor, 0.5f);
        Handles.DrawDottedLine(center, wanderCenter, 4f);
        Handles.DrawWireDisc(wanderCenter, Vector3.up, 0.2f);
    }

    private void DrawDestination(Vector3 center)
    {
        if (_view.FollowerEntity == null || !Application.isPlaying)
            return;

        Vector3 destination = _view.FollowerEntity.destination;

        if (float.IsInfinity(destination.x) || float.IsNaN(destination.x))
            return;

        Handles.color = Fade(PathColor, 0.75f);
        Handles.DrawDottedLine(center, destination, 3f);

        Handles.color = PathColor;
        Handles.DrawWireDisc(destination, Vector3.up, 0.35f);
        Handles.DrawLine(destination, destination + Vector3.up * 0.9f);
        Handles.SphereHandleCap(0, destination + Vector3.up * 0.9f, Quaternion.identity, 0.22f, EventType.Repaint);
    }

    private void DrawLabel(Vector3 center)
    {
        _labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(6, 6, 3, 3),
            normal = { textColor = Color.white },
            richText = true
        };

        string colorHex = ColorUtility.ToHtmlStringRGB(GetStateColor());

        string title = "Unit";
        string details = $"FOV {_view.Config.ViewAngle:F0}°  ·  {_view.Config.MaxDistance:F1} m";

        Handles.Label(center + Vector3.up * (_view.Config.MaxDistance * 0.08f + 2f),
            $"<b><color=#{colorHex}>{title}</color></b>\n{details}", _labelStyle);
    }

    private bool TryGetContext(out Vector3 center, out Vector3 forward)
    {
        center = default;
        forward = default;

        if (_view == null || _view.UnitCenter == null || _view.Config == null)
            return false;

        center = _view.UnitCenter.position;
        forward = _view.UnitCenter.forward;

        return true;
    }

    private Color GetStateColor()
    {
        if (Controller != null && Controller.CanSeePlayer)
            return SpottedColor;

        bool chasing = Application.isPlaying
                       && Controller != null
                       && Controller.State == UnitState.Chase;

        return chasing ? ChaseColor : WanderColor;
    }

    private static Vector3 Rotate(Vector3 direction, float angle)
    {
        return Quaternion.AngleAxis(angle, Vector3.up) * direction;
    }

    private static Color Fade(Color color, float alpha)
    {
        color.a = alpha;

        return color;
    }
#endif
}
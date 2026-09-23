using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class VoxelColumnDebug : MonoBehaviour
{
    private const string PREF_KEY = "Dustborn.VoxelSeamDebug";
    private const float LABEL_REACH = 64f;

    [SerializeField, ReadOnly] private int _lod;
    [SerializeField, ReadOnly] private Vector2Int _column;
    [SerializeField, ReadOnly] private float _voxel;
    [SerializeField, ReadOnly] private string _seams;
    [SerializeField, ReadOnly] private string _morph;
    [SerializeField, ReadOnly] private string _skirt;

    [SerializeField, HideInInspector] private int _seamBits;
    [SerializeField, HideInInspector] private int _morphBits;
    [SerializeField, HideInInspector] private float _size;
    [SerializeField, HideInInspector] private float _bottom;
    [SerializeField, HideInInspector] private float _top;

    public static void Attach(GameObject root, VoxelColumnKey key, float size, float voxel, int low, int high)
    {
        var debug = root.AddComponent<VoxelColumnDebug>();

        debug._lod = key.Lod;
        debug._column = new Vector2Int(key.X, key.Z);
        debug._voxel = voxel;
        debug._seamBits = key.Seams;
        debug._morphBits = key.Morph;
        debug._seams = Faces(key.Seams);
        debug._morph = Faces(key.Morph);
        debug._skirt = Faces(key.Seams & key.Morph);
        debug._size = size;
        debug._bottom = low * size;
        debug._top = (high + 1) * size;
    }

    private static string Faces(int bits)
    {
        string[] names = { "MIN_X", "MAX_X", "MIN_Z", "MAX_Z" };
        var parts = new List<string>();

        for (int face = 0; face < names.Length; face++)
        {
            if ((bits & (1 << face)) != 0)
                parts.Add(names[face]);
        }

        return parts.Count == 0 ? "-" : string.Join("|", parts);
    }

#if UNITY_EDITOR
    private static readonly Color[] LodColors =
    {
        new(0.2f, 0.9f, 0.3f),
        new(0.2f, 0.7f, 1f),
        new(0.65f, 0.45f, 1f),
        new(1f, 0.85f, 0.2f),
        new(1f, 0.55f, 0.15f),
        new(0.95f, 0.3f, 0.6f)
    };

    private static readonly Color SkirtColor = new(1f, 0.2f, 0.15f);
    private static readonly Color SeamColor = new(1f, 1f, 1f);

    private static bool? _visible;

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
        if (Visible)
            Draw(false);
    }

    private void OnDrawGizmosSelected()
    {
        Draw(true);
    }

    private void Draw(bool selected)
    {
        Color color = LodColors[Mathf.Clamp(_lod, 0, LodColors.Length - 1)];

        float x0 = _column.x * _size, x1 = x0 + _size;
        float z0 = _column.y * _size, z1 = z0 + _size;

        Handles.matrix = transform.localToWorldMatrix;

        Handles.color = new Color(color.r, color.g, color.b, selected ? 1f : 0.45f);
        Handles.DrawWireCube(new Vector3((x0 + x1) * 0.5f, (_bottom + _top) * 0.5f, (z0 + z1) * 0.5f),
            new Vector3(_size, _top - _bottom, _size));

        DrawFace(VoxelColumnKey.FACE_MIN_X, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1));
        DrawFace(VoxelColumnKey.FACE_MAX_X, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1));
        DrawFace(VoxelColumnKey.FACE_MIN_Z, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0));
        DrawFace(VoxelColumnKey.FACE_MAX_Z, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1));

        if (selected || Near(new Vector3((x0 + x1) * 0.5f, _top, (z0 + z1) * 0.5f)))
            Handles.Label(new Vector3((x0 + x1) * 0.5f, _top, (z0 + z1) * 0.5f), Describe());

        Handles.matrix = Matrix4x4.identity;
    }

    private void DrawFace(int face, Vector3 from, Vector3 to)
    {
        if ((_seamBits & face) == 0)
            return;

        bool owner = (_morphBits & face) != 0;
        Color outline = owner ? SkirtColor : SeamColor;

        Vector3[] quad =
        {
            new(from.x, _bottom, from.z),
            new(from.x, _top, from.z),
            new(to.x, _top, to.z),
            new(to.x, _bottom, to.z)
        };

        Handles.DrawSolidRectangleWithOutline(quad, new Color(outline.r, outline.g, outline.b, owner ? 0.12f : 0.05f), outline);
        Handles.color = outline;
        Handles.DrawAAPolyLine(owner ? 6f : 3f, quad[1], quad[2]);
    }

    private bool Near(Vector3 localPoint)
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view == null || view.camera == null)
            return false;

        Vector3 point = Handles.matrix.MultiplyPoint3x4(localPoint);
        float reach = _size * 0.75f + LABEL_REACH;

        return DistanceUtility.WithinRadius(view.camera.transform.position, point, reach);
    }

    private string Describe()
    {
        return $"LOD{_lod} колонка ({_column.x}, {_column.y}), воксель {_voxel:0.#} м\n"
            + $"швы {_seams}\nморф {_morph}\nюбка {_skirt}";
    }
#endif
}

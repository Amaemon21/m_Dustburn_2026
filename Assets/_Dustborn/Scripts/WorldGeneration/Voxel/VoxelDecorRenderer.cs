using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class VoxelDecorRenderer : MonoBehaviour
{
    private const int BATCH = 1023;

    private const float MIN_TILE = 8f;
    private const float TILE_FRACTION = 0.25f;
    private const float HYSTERESIS = 6f;

    private Mesh _mesh;
    private Material _material;
    private Matrix4x4[][] _batches;
    private Bounds[] _batchBounds;
    private bool[] _batchDrawn;
    private Bounds _bounds;
    private ShadowCastingMode _shadows = ShadowCastingMode.On;
    private float _drawDistance;

    private Camera _camera;

    public int Count { get; private set; }

    public void Setup(Mesh mesh, Material material, Matrix4x4[] matrices, Bounds bounds, ShadowCastingMode shadows, float drawDistance)
    {
        _mesh = mesh;
        _material = material;
        _bounds = bounds;
        _shadows = shadows;
        _drawDistance = drawDistance;

        Count = matrices.Length;

        if (drawDistance > 0f)
            SplitByTile(matrices, Mathf.Max(MIN_TILE, drawDistance * TILE_FRACTION));
        else
            SplitInOrder(matrices);
    }

    private void SplitInOrder(Matrix4x4[] matrices)
    {
        int batches = Mathf.CeilToInt(matrices.Length / (float)BATCH);

        _batches = new Matrix4x4[batches][];
        _batchBounds = null;

        for (int i = 0; i < batches; i++)
        {
            int count = Mathf.Min(BATCH, matrices.Length - i * BATCH);

            _batches[i] = new Matrix4x4[count];

            System.Array.Copy(matrices, i * BATCH, _batches[i], 0, count);
        }
    }

    private void SplitByTile(Matrix4x4[] matrices, float tile)
    {
        var tiles = new Dictionary<(int, int), List<Matrix4x4>>();

        foreach (Matrix4x4 matrix in matrices)
        {
            Vector3 position = matrix.GetColumn(3);

            (int, int) key = (Mathf.FloorToInt(position.x / tile), Mathf.FloorToInt(position.z / tile));

            if (!tiles.TryGetValue(key, out List<Matrix4x4> bucket))
                tiles[key] = bucket = new List<Matrix4x4>();

            bucket.Add(matrix);
        }

        var batches = new List<Matrix4x4[]>();
        var bounds = new List<Bounds>();

        foreach (KeyValuePair<(int, int), List<Matrix4x4>> pair in tiles)
        {
            List<Matrix4x4> bucket = pair.Value;

            for (int start = 0; start < bucket.Count; start += BATCH)
            {
                int count = Mathf.Min(BATCH, bucket.Count - start);

                var batch = new Matrix4x4[count];

                bucket.CopyTo(start, batch, 0, count);

                batches.Add(batch);
                bounds.Add(Enclose(batch));
            }
        }

        _batches = batches.ToArray();
        _batchBounds = bounds.ToArray();
        _batchDrawn = new bool[_batches.Length];
    }

    private static Bounds Enclose(Matrix4x4[] batch)
    {
        var bounds = new Bounds(batch[0].GetColumn(3), Vector3.zero);

        foreach (Matrix4x4 matrix in batch)
            bounds.Encapsulate((Vector3)matrix.GetColumn(3));

        bounds.Expand(2f);

        return bounds;
    }

    private void LateUpdate()
    {
        if (_mesh == null || _material == null || _batches == null)
            return;

        if (_drawDistance <= 0f)
        {
            Draw(0, _batches.Length);

            return;
        }

        if (!TryViewer(out Vector3 viewer))
        {
            Draw(0, _batches.Length);

            return;
        }

        float outer = _drawDistance * _drawDistance;
        float inner = (_drawDistance - HYSTERESIS) * (_drawDistance - HYSTERESIS);

        if (_bounds.SqrDistance(viewer) > outer)
            return;

        for (int i = 0; i < _batches.Length; i++)
        {
            float range = _batchBounds[i].SqrDistance(viewer);

            _batchDrawn[i] = range <= (_batchDrawn[i] ? outer : inner);

            if (!_batchDrawn[i])
                continue;

            Graphics.DrawMeshInstanced(_mesh, 0, _material, _batches[i], _batches[i].Length, null, _shadows, true, gameObject.layer);
        }
    }

    private void Draw(int from, int count)
    {
        for (int i = from; i < count; i++)
            Graphics.DrawMeshInstanced(_mesh, 0, _material, _batches[i], _batches[i].Length, null, _shadows, true, gameObject.layer);
    }

    private bool TryViewer(out Vector3 position)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;

            position = view == null || view.camera == null ? Vector3.zero : view.camera.transform.position;

            return view != null && view.camera != null;
        }
#endif

        if (_camera == null)
            _camera = Camera.main;

        position = _camera == null ? Vector3.zero : _camera.transform.position;

        return _camera != null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(_bounds.center, _bounds.size);
    }
}

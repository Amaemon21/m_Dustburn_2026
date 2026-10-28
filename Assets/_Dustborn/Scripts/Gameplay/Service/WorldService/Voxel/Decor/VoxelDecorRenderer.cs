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

    private static readonly int InstanceTint = Shader.PropertyToID("_InstanceTint");
    private static readonly List<VoxelDecorRenderer> Active = new();

    private Mesh _mesh;
    private int _subMesh;
    private Material _material;
    private Matrix4x4[][] _batches;
    private MaterialPropertyBlock[] _batchTints;
    private Bounds[] _batchBounds;
    private bool[] _batchDrawn;
    private Bounds _bounds;
    private ShadowCastingMode _shadows = ShadowCastingMode.On;
    private float _drawDistance;
    private int _slot = -1;

    public int Count { get; private set; }

    public void Setup(Mesh mesh, Material material, Matrix4x4[] matrices, ShadowCastingMode shadows, float drawDistance, int subMesh = 0,
        Vector4[] tints = null)
    {
        _mesh = mesh;
        _subMesh = subMesh;
        _material = material;
        _shadows = shadows;
        _drawDistance = drawDistance;

        Count = matrices.Length;

        if (drawDistance > 0f)
            SplitByTile(matrices, tints, Mathf.Max(MIN_TILE, drawDistance * TILE_FRACTION));
        else
            SplitInOrder(matrices, tints);

        _bounds = _batchBounds.Length == 0 ? new Bounds(transform.position, Vector3.zero) : _batchBounds[0];

        foreach (Bounds bounds in _batchBounds)
            _bounds.Encapsulate(bounds);
    }

    public void Draw(Vector3 viewer, Camera camera, bool steady)
    {
        if (_mesh == null || _material == null || _batches == null)
            return;

        if (_drawDistance <= 0f)
        {
            for (int i = 0; i < _batches.Length; i++)
                DrawBatch(i, camera);

            return;
        }

        float outer = _drawDistance * _drawDistance;
        float inner = (_drawDistance - HYSTERESIS) * (_drawDistance - HYSTERESIS);

        if (_bounds.SqrDistance(viewer) > outer)
            return;

        for (int i = 0; i < _batches.Length; i++)
        {
            float range = _batchBounds[i].SqrDistance(viewer);
            bool drawn = range <= (steady && !_batchDrawn[i] ? inner : outer);

            if (steady)
                _batchDrawn[i] = drawn;

            if (drawn)
                DrawBatch(i, camera);
        }
    }

    private void OnEnable()
    {
        if (Active.Count == 0)
            RenderPipelineManager.beginCameraRendering += Render;

        _slot = Active.Count;
        Active.Add(this);
    }

    private void OnDisable()
    {
        if (_slot < 0)
            return;

        int last = Active.Count - 1;
        VoxelDecorRenderer moved = Active[last];

        Active[_slot] = moved;
        moved._slot = _slot;
        Active.RemoveAt(last);

        _slot = -1;

        if (Active.Count == 0)
            RenderPipelineManager.beginCameraRendering -= Render;
    }

    private static void Render(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            return;

        Vector3 viewer = camera.transform.position;
        bool steady = camera.cameraType == CameraType.Game;

        foreach (VoxelDecorRenderer renderer in Active)
        {
            if ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                renderer.Draw(viewer, camera, steady);
        }
    }

    private void DrawBatch(int batch, Camera camera)
    {
        var parameters = new RenderParams(_material)
        {
            camera = camera,
            layer = gameObject.layer,
            shadowCastingMode = _shadows,
            receiveShadows = true,
            worldBounds = _batchBounds[batch],
            matProps = _batchTints?[batch]
        };

        Graphics.RenderMeshInstanced(parameters, _mesh, _subMesh, _batches[batch]);
    }

    private void SplitInOrder(Matrix4x4[] matrices, Vector4[] tints)
    {
        int batches = Mathf.CeilToInt(matrices.Length / (float)BATCH);

        _batches = new Matrix4x4[batches][];
        _batchTints = tints == null ? null : new MaterialPropertyBlock[batches];
        _batchBounds = new Bounds[batches];
        _batchDrawn = new bool[batches];

        for (int i = 0; i < batches; i++)
        {
            int count = Mathf.Min(BATCH, matrices.Length - i * BATCH);

            _batches[i] = new Matrix4x4[count];

            System.Array.Copy(matrices, i * BATCH, _batches[i], 0, count);

            _batchBounds[i] = Enclose(_batches[i]);

            if (tints == null)
                continue;

            var batchTints = new Vector4[count];

            System.Array.Copy(tints, i * BATCH, batchTints, 0, count);

            _batchTints[i] = Block(batchTints);
        }
    }

    private void SplitByTile(Matrix4x4[] matrices, Vector4[] tints, float tile)
    {
        var tiles = new Dictionary<(int, int), List<int>>();

        for (int i = 0; i < matrices.Length; i++)
        {
            Vector3 position = matrices[i].GetColumn(3);

            (int, int) key = (Mathf.FloorToInt(position.x / tile), Mathf.FloorToInt(position.z / tile));

            if (!tiles.TryGetValue(key, out List<int> bucket))
                tiles[key] = bucket = new List<int>();

            bucket.Add(i);
        }

        var batches = new List<Matrix4x4[]>();
        var blocks = new List<MaterialPropertyBlock>();
        var bounds = new List<Bounds>();

        foreach (KeyValuePair<(int, int), List<int>> pair in tiles)
        {
            List<int> bucket = pair.Value;

            for (int start = 0; start < bucket.Count; start += BATCH)
            {
                int count = Mathf.Min(BATCH, bucket.Count - start);

                var batch = new Matrix4x4[count];
                Vector4[] batchTints = tints == null ? null : new Vector4[count];

                for (int i = 0; i < count; i++)
                {
                    batch[i] = matrices[bucket[start + i]];

                    if (batchTints != null)
                        batchTints[i] = tints[bucket[start + i]];
                }

                batches.Add(batch);
                blocks.Add(batchTints == null ? null : Block(batchTints));
                bounds.Add(Enclose(batch));
            }
        }

        _batches = batches.ToArray();
        _batchTints = tints == null ? null : blocks.ToArray();
        _batchBounds = bounds.ToArray();
        _batchDrawn = new bool[_batches.Length];
    }

    private static MaterialPropertyBlock Block(Vector4[] tints)
    {
        var block = new MaterialPropertyBlock();

        block.SetVectorArray(InstanceTint, tints);

        return block;
    }

    private Bounds Enclose(Matrix4x4[] batch)
    {
        Bounds local = _mesh.bounds;
        Bounds bounds = Transformed(local, batch[0]);

        foreach (Matrix4x4 matrix in batch)
            bounds.Encapsulate(Transformed(local, matrix));

        bounds.Expand(2f * MeshCombiner.WIND_MARGIN);

        return bounds;
    }

    private static Bounds Transformed(Bounds local, Matrix4x4 matrix)
    {
        Vector3 centre = matrix.MultiplyPoint3x4(local.center);
        Vector3 extents = local.extents;

        var size = new Vector3(
            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);

        return new Bounds(centre, 2f * size);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(_bounds.center, _bounds.size);
    }
}

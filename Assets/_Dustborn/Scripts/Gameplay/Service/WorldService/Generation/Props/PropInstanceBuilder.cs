using System.Collections.Generic;
using UnityEngine;

public sealed class PropInstanceBuilder
{
    private const float PROBE_ABOVE = 4f;
    private const float PROBE_DEPTH = 12f;
    private const int PROBE_HITS = 8;

    private readonly IReadOnlyList<PropPlacement> _placements;
    private readonly Transform _parent;
    private readonly Transform _ground;
    private readonly int _groundMask;
    private readonly float _cullScreenHeight;
    private readonly IWorldObjectHook _hook;
    private readonly RaycastHit[] _hits = new RaycastHit[PROBE_HITS];
    private readonly Dictionary<Vector2Int, List<(Transform Instance, int Placement)>> _byCell = new();
    private readonly Queue<(Transform Instance, int Placement)> _reground = new();
    private readonly HashSet<int> _regroundPending = new();
    private readonly System.Diagnostics.Stopwatch _regroundClock = new();
    private readonly float _cellSize;
    private int _next;

    public bool Ready => _next >= _placements.Count;
    public float Progress => _placements.Count == 0 ? 1f : _next / (float)_placements.Count;
    public int RegroundPending => _reground.Count;

    public PropInstanceBuilder(IReadOnlyList<PropPlacement> placements, Transform parent, Transform ground, float cullScreenHeight,
        float cellSize, IWorldObjectHook hook = null)
    {
        _cellSize = Mathf.Max(1f, cellSize);
        _placements = placements ?? new List<PropPlacement>();
        _parent = parent;
        _ground = ground;
        _groundMask = ground == null ? Physics.DefaultRaycastLayers : 1 << ground.gameObject.layer;
        _cullScreenHeight = cullScreenHeight;
        _hook = hook;
    }

    public void BuildNext(int count)
    {
        WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.PropBatch);
        int end = Mathf.Min(_placements.Count, _next + Mathf.Max(1, count));

        if (_next == 0)
            Physics.SyncTransforms();

        while (_next < end)
        {
            PropPlacement placement = _placements[_next];

            if (placement.Prefab == null)
                throw new System.InvalidOperationException("A saved prop has no prefab. Generate the world again.");

            GameObject instance = Create(placement.Prefab);
            Place(instance.transform, placement);
            Track(instance.transform, placement, _next);
            Cull(instance);
            _hook?.Spawned(instance, WorldObjectKey.Prop(_next));
            _next++;
        }

        span.Finish(end);
    }

    public void QueueReground(IReadOnlyList<Vector2Int> cells)
    {
        foreach (Vector2Int cell in cells)
        {
            if (!_byCell.TryGetValue(cell, out List<(Transform Instance, int Placement)> props))
                continue;

            foreach ((Transform instance, int placement) in props)
            {
                if (instance == null || !_regroundPending.Add(placement))
                    continue;

                _reground.Enqueue((instance, placement));
            }
        }
    }

    public int RegroundStep(float milliseconds, int maxProps = 64)
    {
        if (_reground.Count == 0 || milliseconds <= 0f || maxProps <= 0)
            return 0;

        using WorldGenProbe.Span span = WorldGenProbe.Measure(WorldGenStage.PropReground);
        _regroundClock.Restart();
        int processed = 0;

        while (_reground.Count > 0 && processed < maxProps && _regroundClock.Elapsed.TotalMilliseconds < milliseconds)
        {
            (Transform instance, int placement) = _reground.Dequeue();
            _regroundPending.Remove(placement);
            processed++;

            if (instance != null)
                Place(instance, _placements[placement]);
        }

        return processed;
    }

    private void Track(Transform instance, PropPlacement placement, int index)
    {
        var cell = new Vector2Int(Mathf.FloorToInt(placement.Position.x / _cellSize), Mathf.FloorToInt(placement.Position.z / _cellSize));

        if (!_byCell.TryGetValue(cell, out List<(Transform Instance, int Placement)> props))
        {
            props = new List<(Transform Instance, int Placement)>();
            _byCell.Add(cell, props);
        }

        props.Add((instance, index));
    }

    private void Place(Transform target, PropPlacement placement)
    {
        Quaternion yaw = Quaternion.Euler(0f, placement.Yaw, 0f);
        Vector3 position = placement.Position;
        Quaternion rotation = yaw;

        if (TryGround(placement.Position, out RaycastHit hit))
        {
            position = hit.point;
            rotation = placement.TiltToGround ? Quaternion.FromToRotation(Vector3.up, hit.normal) * yaw : yaw;
        }

        target.SetPositionAndRotation(position + rotation * Vector3.up * placement.GroundOffset, rotation);
    }

    private bool TryGround(Vector3 baked, out RaycastHit ground)
    {
        ground = default;
        Vector3 origin = baked + Vector3.up * PROBE_ABOVE;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, PROBE_ABOVE + PROBE_DEPTH, _groundMask,
            QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _hits[i];

            if (hit.distance >= nearest || !IsGround(hit.collider))
                continue;

            nearest = hit.distance;
            ground = hit;
        }

        return nearest < float.MaxValue;
    }

    private bool IsGround(Collider collider)
    {
        if (!collider.TryGetComponent<GeneratedMesh>(out _))
            return false;

        return _ground == null || collider.transform.IsChildOf(_ground);
    }

    private void Cull(GameObject instance)
    {
        if (_cullScreenHeight <= 0f || instance.GetComponentInChildren<LODGroup>() != null)
            return;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        LODGroup group = instance.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(_cullScreenHeight, renderers) });
        group.RecalculateBounds();
    }

    private GameObject Create(GameObject prefab)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var instance = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _parent) as GameObject;

            if (instance == null)
                throw new System.InvalidOperationException($"{prefab.name} is not a prefab asset.");

            return instance;
        }
#endif

        GameObject copy = Object.Instantiate(prefab, _parent);
        MeshCombiner.FitMaterials(copy, prefab.name);
        return copy;
    }
}

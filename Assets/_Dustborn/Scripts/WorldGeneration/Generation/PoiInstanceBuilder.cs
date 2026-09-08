using System.Collections.Generic;
using UnityEngine;

public sealed class PoiInstanceBuilder
{
    private readonly IReadOnlyList<PoiPlacement> _placements;
    private readonly Transform _parent;
    private int _next;

    public bool Ready => _next >= _placements.Count;
    public float Progress => _placements.Count == 0 ? 1f : _next / (float)_placements.Count;

    public PoiInstanceBuilder(IReadOnlyList<PoiPlacement> placements, Transform parent)
    {
        _placements = placements;
        _parent = parent;
    }

    public void BuildNext(int count)
    {
        int end = Mathf.Min(_placements.Count, _next + Mathf.Max(1, count));

        while (_next < end)
        {
            PoiPlacement placement = _placements[_next];

            if (placement.Prefab == null)
                throw new System.InvalidOperationException("A saved POI has no prefab. Generate the world again.");

            GameObject instance = Create(placement.Prefab);
            instance.transform.SetPositionAndRotation(placement.PrefabPosition, Quaternion.Euler(0f, placement.Rotation, 0f));
            instance.name = $"{placement.Prefab.name}_{placement.District}";
            _next++;
        }
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

        return Object.Instantiate(prefab, _parent);
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed class WorldPrefabIndex
{
    private sealed class ByReference : IEqualityComparer<GameObject>
    {
        public bool Equals(GameObject left, GameObject right) => ReferenceEquals(left, right);
        public int GetHashCode(GameObject prefab) => RuntimeHelpers.GetHashCode(prefab);
    }

    private readonly Dictionary<string, GameObject> _pois = new();
    private readonly Dictionary<string, GameObject> _props = new();
    private readonly Dictionary<GameObject, string> _poiKeys = new(new ByReference());
    private readonly Dictionary<GameObject, string> _propKeys = new(new ByReference());

    public WorldPrefabIndex(PoiDatabase pois, PropDatabase props)
    {
        if (pois != null)
        {
            foreach (PoiDefinition definition in pois.Definitions)
                Add(_pois, _poiKeys, definition?.Prefab, nameof(PoiDatabase));
        }

        if (props != null)
        {
            foreach (PropDefinition definition in props.Definitions)
                Add(_props, _propKeys, definition?.Prefab, nameof(PropDatabase));
        }
    }

    public string PoiKey(GameObject prefab) => Key(_poiKeys, prefab, nameof(PoiDatabase));

    public string PropKey(GameObject prefab) => Key(_propKeys, prefab, nameof(PropDatabase));

    public bool TryPoi(string key, out GameObject prefab) => _pois.TryGetValue(key, out prefab);

    public bool TryProp(string key, out GameObject prefab) => _props.TryGetValue(key, out prefab);

    private static void Add(Dictionary<string, GameObject> prefabs, Dictionary<GameObject, string> keys, GameObject prefab, string database)
    {
        if (prefab == null)
            return;

        string key = prefab.name;

        if (prefabs.TryGetValue(key, out GameObject existing) && existing != prefab)
            throw new InvalidOperationException($"{database} holds two different prefabs named '{key}'; a saved world finds prefabs by name, rename one");

        prefabs[key] = prefab;
        keys[prefab] = key;
    }

    private static string Key(Dictionary<GameObject, string> keys, GameObject prefab, string database)
    {
        if (ReferenceEquals(prefab, null))
            throw new InvalidOperationException("A placement has no prefab and cannot be saved");

        if (!keys.TryGetValue(prefab, out string key))
            throw new InvalidOperationException($"A placed prefab is not in the {database}; a saved world can only refer to prefabs the build ships through it");

        return key;
    }
}

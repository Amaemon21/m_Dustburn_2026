using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class VoxelDecorPool : IDisposable
{
    private const int MAX_RETAINED = 512;
    private const int MAX_PER_LAYER = 64;

    private readonly GameObject _root;
    private readonly Stack<PooledDecorObject>[] _layers;
    private readonly int[] _retainedByLayer;
    private int _retained;
    private bool _disposed;

    public VoxelDecorPool(Transform parent, int layerCount)
    {
        _root = new GameObject("Decor Pool");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);
        _layers = new Stack<PooledDecorObject>[layerCount];
        _retainedByLayer = new int[layerCount];
    }

    public GameObject Take(GameObject prefab, int layer, Transform parent, out bool reused)
    {
        Stack<PooledDecorObject> available = _layers[layer];

        while (available != null && available.Count > 0)
        {
            PooledDecorObject member = available.Pop();

            if (member == null)
                continue;

            Forget(member);
            member.transform.SetParent(parent, false);
            reused = true;
            return member.gameObject;
        }

        GameObject copy = UnityEngine.Object.Instantiate(prefab, _root.transform);
        copy.SetActive(false);
        copy.AddComponent<PooledDecorObject>().Setup(this, layer);
        copy.transform.SetParent(parent, false);
        reused = false;
        return copy;
    }

    public bool Owns(GameObject copy)
    {
        return copy != null && copy.TryGetComponent(out PooledDecorObject member) && member.Owner == this && !member.Retained;
    }

    public void Return(GameObject copy)
    {
        if (!Owns(copy))
            return;

        PooledDecorObject member = copy.GetComponent<PooledDecorObject>();
        copy.SetActive(false);

        if (_disposed || _retained >= MAX_RETAINED || _retainedByLayer[member.Layer] >= MAX_PER_LAYER)
        {
            GeneratedMesh.Destroy(copy);
            return;
        }

        copy.transform.SetParent(_root.transform, false);
        member.Retained = true;
        _retained++;
        _retainedByLayer[member.Layer]++;
        (_layers[member.Layer] ??= new Stack<PooledDecorObject>()).Push(member);
    }

    public void Forget(PooledDecorObject member)
    {
        if (!member.Retained)
            return;

        member.Retained = false;
        _retained--;
        _retainedByLayer[member.Layer]--;
    }

    public void Clear()
    {
        foreach (Stack<PooledDecorObject> layer in _layers)
        {
            if (layer == null)
                continue;

            while (layer.Count > 0)
            {
                PooledDecorObject member = layer.Pop();

                if (member == null)
                    continue;

                Forget(member);
                GeneratedMesh.Destroy(member.gameObject);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Clear();
        GeneratedMesh.Destroy(_root);
    }
}

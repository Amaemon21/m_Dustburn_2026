using System;
using UnityEngine;

public sealed class VoxelDecorDemolition
{
    private int _nextChild;

    public GameObject Root { get; }
    public bool Complete => _nextChild < 0;

    public VoxelDecorDemolition(GameObject root)
    {
        Root = root;
        _nextChild = root.transform.childCount - 1;
    }

    public bool TryTake(out GameObject child)
    {
        child = null;

        if (Root == null)
        {
            _nextChild = -1;
            return false;
        }

        int index = Math.Min(_nextChild, Root.transform.childCount - 1);
        _nextChild = index - 1;

        if (index < 0)
            return false;

        child = Root.transform.GetChild(index).gameObject;
        return true;
    }
}

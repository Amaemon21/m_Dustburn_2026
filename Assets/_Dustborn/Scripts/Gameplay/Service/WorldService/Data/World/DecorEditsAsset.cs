using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public struct DecorEdit
{
    [field: SerializeField] public int Layer { get; private set; }
    [field: SerializeField] public int CellX { get; private set; }
    [field: SerializeField] public int CellY { get; private set; }
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public bool Removed { get; private set; }
    [field: SerializeField] public Vector2 Origin { get; private set; }
    [field: SerializeField] public Vector2 Position { get; private set; }
    [field: SerializeField] public float Rotation { get; private set; }
    [field: SerializeField] public float Scale { get; private set; }
    [field: SerializeField] public float Height { get; private set; }
    [field: SerializeField] public float Shade { get; private set; }

    public DecorKey Key => new(Layer, CellX, CellY);

    public static DecorEdit Moved(DecorKey key, GameObject prefab, Vector2 origin, DecorInstance pose)
    {
        return new DecorEdit
        {
            Layer = key.Layer,
            CellX = key.CellX,
            CellY = key.CellY,
            Prefab = prefab,
            Origin = origin,
            Position = new Vector2(pose.Position.x, pose.Position.z),
            Rotation = pose.Rotation,
            Scale = pose.Scale,
            Height = pose.Height,
            Shade = pose.Shade
        };
    }

    public static DecorEdit Removal(DecorKey key, GameObject prefab, Vector2 origin)
    {
        return new DecorEdit
        {
            Layer = key.Layer,
            CellX = key.CellX,
            CellY = key.CellY,
            Prefab = prefab,
            Removed = true,
            Origin = origin,
            Position = origin
        };
    }

    public DecorInstance Instance(float surface)
    {
        return new DecorInstance
        {
            Position = new Vector3(Position.x, surface, Position.y),
            Rotation = Rotation,
            Scale = Scale,
            Height = Height,
            Shade = Shade,
            CellX = CellX,
            CellY = CellY
        };
    }
}

[CreateAssetMenu(fileName = "DecorEdits", menuName = "World/Decor Edits")]
public class DecorEditsAsset : ScriptableObject
{
    [field: SerializeField, ReadOnly] public int Seed { get; private set; }

    [SerializeField] private List<DecorEdit> _edits = new();

    public IReadOnlyList<DecorEdit> Edits => _edits;

    [ShowNativeProperty] public int Moved => Count(false);
    [ShowNativeProperty] public int Removed => Count(true);

    public bool Fits(int seed)
    {
        return _edits.Count == 0 || Seed == seed;
    }

    public bool TryGet(DecorKey key, out DecorEdit edit)
    {
        int index = IndexOf(key);

        edit = index < 0 ? default : _edits[index];

        return index >= 0;
    }

    public void Set(int seed, DecorEdit edit)
    {
        if (_edits.Count == 0)
            Seed = seed;

        int index = IndexOf(edit.Key);

        if (index < 0)
            _edits.Add(edit);
        else
            _edits[index] = edit;
    }

    public bool Forget(DecorKey key)
    {
        int index = IndexOf(key);

        if (index < 0)
            return false;

        _edits.RemoveAt(index);

        return true;
    }

    public void Clear()
    {
        _edits.Clear();
    }

    private int IndexOf(DecorKey key)
    {
        for (int i = 0; i < _edits.Count; i++)
        {
            if (_edits[i].Key.Equals(key))
                return i;
        }

        return -1;
    }

    private int Count(bool removed)
    {
        int count = 0;

        foreach (DecorEdit edit in _edits)
        {
            if (edit.Removed == removed)
                count++;
        }

        return count;
    }
}

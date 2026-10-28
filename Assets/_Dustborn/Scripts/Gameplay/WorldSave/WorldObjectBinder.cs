using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class WorldObjectBinder : IWorldObjectHook
{
    private readonly BlockService _blocks;
    private readonly HashSet<string> _used = new();
    private readonly StringBuilder _path = new();

    public WorldObjectBinder(BlockService blocks)
    {
        _blocks = blocks;
    }

    public void Spawned(GameObject instance, WorldObjectKey key)
    {
        _used.Clear();
        Transform root = instance.transform;

        foreach (ContainerInteractable container in instance.GetComponentsInChildren<ContainerInteractable>(true))
            container.AssignId(IdOf(key, root, container.transform));

        foreach (BlockView block in instance.GetComponentsInChildren<BlockView>(true))
            _blocks.Attach(block, IdOf(key, root, block.transform));
    }

    private string IdOf(WorldObjectKey key, Transform root, Transform target)
    {
        string id = $"{key}/{PathOf(root, target)}";
        string unique = id;

        for (int duplicate = 1; !_used.Add(unique); duplicate++)
            unique = $"{id}#{duplicate}";

        return unique;
    }

    private string PathOf(Transform root, Transform target)
    {
        _path.Clear();

        for (Transform current = target; current != root && current != null; current = current.parent)
        {
            if (_path.Length > 0)
                _path.Insert(0, '/');

            _path.Insert(0, current.name);
        }

        return _path.Length == 0 ? "." : _path.ToString();
    }
}

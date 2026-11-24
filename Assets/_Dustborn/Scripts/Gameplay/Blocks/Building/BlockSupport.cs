using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BlockSupport
{
    private static readonly Vector3Int[] NEIGHBORS =
    {
        Vector3Int.down, Vector3Int.up, Vector3Int.left, Vector3Int.right,
        new(0, 0, -1), new(0, 0, 1)
    };

    private readonly BlockRepository _repository;
    private readonly HashSet<string> _visited = new();
    private readonly Queue<PlacedBlockData> _pending = new();
    private readonly List<PlacedBlockData> _component = new();

    public BlockSupport(BlockRepository repository) => _repository = repository;

    public bool HasNeighbor(Vector3Int cell, int height)
    {
        for (int level = 0; level < height; level++)
        {
            foreach (Vector3Int offset in NEIGHBORS)
            {
                if (_repository.IsOccupied(cell + Vector3Int.up * level + offset))
                    return true;
            }
        }

        return false;
    }

    public void FindUnsupported(PlacedBlockData removed, Func<PlacedBlockData, bool> isGrounded, List<PlacedBlockData> result)
    {
        result.Clear();
        _visited.Clear();

        for (int level = 0; level < removed.CellHeight; level++)
        {
            foreach (Vector3Int offset in NEIGHBORS)
            {
                Enqueue(removed.GetCell(level) + offset);

                if (_pending.Count > 0)
                    CollectUnsupported(isGrounded, result);
            }
        }

        _visited.Clear();
    }

    private void CollectUnsupported(Func<PlacedBlockData, bool> isGrounded, List<PlacedBlockData> result)
    {
        bool grounded = false;

        while (_pending.Count > 0)
        {
            PlacedBlockData block = _pending.Dequeue();
            _component.Add(block);
            grounded = grounded || isGrounded(block);

            for (int level = 0; level < block.CellHeight; level++)
                foreach (Vector3Int direction in NEIGHBORS)
                    Enqueue(block.GetCell(level) + direction);
        }

        if (!grounded)
            result.AddRange(_component);

        _component.Clear();
    }

    private void Enqueue(Vector3Int cell)
    {
        if (_repository.TryGetPlaced(cell, out PlacedBlockData block) && _visited.Add(block.Id))
            _pending.Enqueue(block);
    }
}

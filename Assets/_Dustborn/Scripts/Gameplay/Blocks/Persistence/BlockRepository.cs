using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BlockRepository
{
    private readonly ISaveService _saves;
    private readonly Dictionary<Vector3Int, string> _occupied = new();

    public BlocksData Origin { get; }

    public BlockRepository(ISaveService saves)
    {
        _saves = saves;
        Origin = saves.Get<BlocksData>();
        Origin.States ??= new Dictionary<string, BlockStateData>();
        Origin.Placed ??= new Dictionary<string, PlacedBlockData>();
        
        foreach (PlacedBlockData block in Origin.Placed.Values)
            if (block != null && !IsDestroyed(block.Id))
                for (int level = 0; level < block.CellHeight; level++)
                    _occupied[block.GetCell(level)] = block.Id;
    }

    public int DamageOf(string id) => Origin.States.TryGetValue(id, out BlockStateData state) && state != null ? state.Damage : 0;

    public bool IsDestroyed(string id) => Origin.States.TryGetValue(id, out BlockStateData state) && state != null && state.Destroyed;

    public void SetDamage(string id, int damage)
    {
        BlockStateData state = StateOf(id);
        
        if (state.Damage == damage)
            return;

        state.Damage = damage;
        MarkDirty();
    }

    public void MarkDestroyed(string id)
    {
        BlockStateData state = StateOf(id);
        if (state.Destroyed)
            return;

        state.Destroyed = true;
        if (Origin.Placed.Remove(id, out PlacedBlockData placed) && placed != null)
            for (int level = 0; level < placed.CellHeight; level++)
                _occupied.Remove(placed.GetCell(level));

        MarkDirty();
    }

    public bool IsOccupied(Vector3Int cell) => _occupied.ContainsKey(cell);

    public bool IsOccupied(Vector3Int baseCell, int height)
    {
        for (int level = 0; level < height; level++)
        {
            if (IsOccupied(baseCell + Vector3Int.up * level))
                return true;
        }

        return false;
    }

    public bool TryGetPlaced(Vector3Int cell, out PlacedBlockData block)
    {
        block = null;
        return _occupied.TryGetValue(cell, out string id) && Origin.Placed.TryGetValue(id, out block);
    }

    public void AddPlaced(PlacedBlockData block, float originY)
    {
        if (block == null || string.IsNullOrWhiteSpace(block.Id) || string.IsNullOrWhiteSpace(block.ItemId))
            throw new ArgumentException("A constructed block requires an instance id and an item id", nameof(block));
        
        if (IsOccupied(block.GetCell(), block.CellHeight) || Origin.Placed.ContainsKey(block.Id) || IsDestroyed(block.Id))
            throw new InvalidOperationException("The block instance or cell is already used");
        
        Origin.BuildOriginY ??= originY;
        Origin.Placed.Add(block.Id, block);
        
        for (int level = 0; level < block.CellHeight; level++)
            _occupied.Add(block.GetCell(level), block.Id);
        
        MarkDirty();
    }

    private BlockStateData StateOf(string id)
    {
        if (Origin.States.TryGetValue(id, out BlockStateData state) && state != null)
            return state;

        state = new BlockStateData();
        Origin.States[id] = state;
        return state;
    }

    private void MarkDirty() => _saves.MarkDirty<BlocksData>();
}

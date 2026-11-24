using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class BlockPlacementService : IGameplayActivatable, IDisposable
{
    private readonly BlockRepository _repository;
    private readonly BlockService _blocks;
    private readonly ItemCatalog _catalog;
    private readonly IInventoryService _inventory;
    private readonly IObjectResolver _resolver;
    private readonly BlockPlacementProbe _probe;
    private readonly BlockSupport _support;
    private readonly Dictionary<string, BlockView> _views = new();
    private readonly List<PlacedBlockData> _unsupported = new();
    private Transform _root;

    public BlockPlacementService(BlockRepository repository, BlockService blocks, ItemCatalog catalog,
        IInventoryService inventory, IObjectResolver resolver, BlockPlacementProbe probe, BlockSupport support)
    {
        _repository = repository;
        _blocks = blocks;
        _catalog = catalog;
        _inventory = inventory;
        _resolver = resolver;
        _probe = probe;
        _support = support;
    }

    public void Activate()
    {
        if (_root != null)
            return;

        _root = new GameObject("PlayerBuiltBlocks").transform;

        foreach (PlacedBlockData data in _repository.Origin.Placed.Values)
        {
            if (data == null || _repository.IsDestroyed(data.Id))
                continue;
            if (_catalog.GetItem(data.ItemId) is not BlockInventoryItem item || item.Prefab == null)
            {
                Debug.LogWarning($"Cannot restore built block '{data.Id}': item '{data.ItemId}' is unavailable");
                continue;
            }
            Spawn(item, data, _repository.Origin.BuildOriginY ?? 0f);
        }

        _blocks.BuiltBlockDestroyed += OnBuiltBlockDestroyed;
    }

    public bool TryPlace(HeldItem held, BlockInventoryItem item, Ray ray, Transform player, int rotation)
    {
        if (_root == null || item == null || item.Prefab == null || item.Prefab.Config == null ||
            held.ItemId != item.ItemId ||
            !_probe.TryFind(item, ray, player, rotation, out Vector3Int cell, out float originY, out bool allowed, out bool grounded) ||
            !allowed)
            return false;
        IReadOnlyInventorySlot slot = _inventory.GetInventory(held.OwnerId).GetSlot(held.Coordinates);
        if (slot.State.CurrentValue.IsEmpty || slot.State.CurrentValue.ItemId != item.ItemId)
            return false;

        PlacedBlockData data = new()
        {
            Id = "built/" + Guid.NewGuid().ToString("N"), ItemId = item.ItemId,
            X = cell.x, Y = cell.y, Z = cell.z, Rotation = rotation & 3, Height = Mathf.Max(1, item.Height), Grounded = grounded
        };
        BlockView view = Spawn(item, data, originY);
        if (!_inventory.RemoveItemsFromInventory(held.OwnerId, held.Coordinates, item.ItemId, 1).Success)
        {
            _views.Remove(data.Id);
            view.Remove();
            return false;
        }
        _repository.AddPlaced(data, originY);
        Physics.SyncTransforms();
        return true;
    }

    private BlockView Spawn(BlockInventoryItem item, PlacedBlockData data, float originY)
    {
        BlockView view = _resolver.Instantiate(item.Prefab, BlockGrid.Center(data.GetCell(), originY),
            BlockGrid.Rotation(data.Rotation), _root);
        _blocks.Attach(view, data.Id);
        _views.Add(data.Id, view);
        return view;
    }

    private void OnBuiltBlockDestroyed(PlacedBlockData removed)
    {
        _views.Remove(removed.Id);
        _support.FindUnsupported(removed, IsGrounded, _unsupported);

        foreach (PlacedBlockData block in _unsupported)
        {
            _views.Remove(block.Id);
            _blocks.Collapse(block.Id);
        }

        _unsupported.Clear();
    }

    private bool IsGrounded(PlacedBlockData block) =>
        block.Grounded ?? (_catalog.GetItem(block.ItemId) is BlockInventoryItem item &&
            item.Prefab != null &&
            _probe.HasGroundSupport(item, block.GetCell(), _repository.Origin.BuildOriginY ?? 0f, BlockGrid.Rotation(block.Rotation)));

    public void Dispose()
    {
        _blocks.BuiltBlockDestroyed -= OnBuiltBlockDestroyed;

        foreach (BlockView view in _views.Values)
            if (view != null)
                view.Detach();
        _views.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root.gameObject);
    }
}

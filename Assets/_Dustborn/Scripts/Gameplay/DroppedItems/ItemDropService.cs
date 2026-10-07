using System;
using UnityEngine;

public sealed class ItemDropService : IItemDropService
{
    private const float WALL_GAP = 0.3f;
    private const float MIN_FLAT_LOOK = 0.0001f;

    private static readonly Vector2 VIEW_CENTER = new(0.5f, 0.5f);

    private readonly IInventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly DroppedItemService _droppedItems;
    private readonly IPlayerProvider _player;
    private readonly ICameraService _cameras;
    private readonly PlayerInventorySettings _settings;

    public ItemDropService(IInventoryService inventory, ItemCatalog catalog, DroppedItemService droppedItems, IPlayerProvider player,
        ICameraService cameras, PlayerInventorySettings settings)
    {
        _inventory = inventory;
        _catalog = catalog;
        _droppedItems = droppedItems;
        _player = player;
        _cameras = cameras;
        _settings = settings;
    }

    public bool Drop(string ownerId, Vector2Int coordinates, int amount)
    {
        if (amount <= 0 || !_inventory.TryGetInventory(ownerId, out IReadOnlyInventoryGrid grid))
            return false;

        InventorySlotState state = grid.GetSlot(coordinates).State.CurrentValue;
        if (state.IsEmpty)
            return false;

        InventoryItem item = _catalog.GetItem(state.ItemId);
        if (item == null || item.PickUpPrefab == null)
        {
            Debug.LogWarning($"{nameof(ItemDropService)}: item '{state.ItemId}' has no {nameof(InventoryItem.PickUpPrefab)}, it cannot be dropped");
            return false;
        }

        if (!TryFindOrigin(out Vector3 origin, out Quaternion rotation))
            return false;

        InventorySlotState dropped = state.WithAmount(Math.Min(amount, state.Amount));
        if (!_inventory.RemoveItemsFromInventory(ownerId, coordinates, dropped.ItemId, dropped.Amount).Success)
            return false;

        _droppedItems.Drop(item, dropped, origin, rotation);
        return true;
    }

    private bool TryFindOrigin(out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = default;
        Transform player = _player.Player;
        if (player == null || !_cameras.TryGetAimRay(VIEW_CENTER, out Ray aim))
            return false;

        Vector3 forward = Vector3.ProjectOnPlane(aim.direction, Vector3.up);
        if (forward.sqrMagnitude < MIN_FLAT_LOOK)
            forward = player.forward;
        forward.Normalize();
        rotation = Quaternion.LookRotation(forward);

        float reach = _settings.DropDistance;
        if (Physics.Raycast(aim.origin, forward, out RaycastHit wall, reach + WALL_GAP, _settings.DropMask, QueryTriggerInteraction.Ignore))
            reach = Mathf.Max(0f, wall.distance - WALL_GAP);

        origin = aim.origin + forward * reach;
        return true;
    }
}

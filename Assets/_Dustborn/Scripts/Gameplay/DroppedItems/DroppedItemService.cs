using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DroppedItemService : IGameplayActivatable, IDisposable
{
    private readonly DroppedItemRepository _repository;
    private readonly ItemCatalog _catalog;
    private readonly PlayerInventorySettings _settings;
    private readonly Dictionary<DroppedItemData, IDisposable> _tracked = new();

    public DroppedItemService(DroppedItemRepository repository, ItemCatalog catalog, PlayerInventorySettings settings)
    {
        _repository = repository;
        _catalog = catalog;
        _settings = settings;
    }

    public void Activate()
    {
        foreach (DroppedItemData item in new List<DroppedItemData>(_repository.Items))
            Restore(item);
    }

    public void Drop(InventoryItem item, InventorySlotState stack, Vector3 origin, Quaternion rotation)
    {
        PickUpItemInteractable view = Create(item, stack, origin, rotation);
        DropPlacement.Settle(view.transform, _settings.DropMask);
        Track(view, _repository.Add(stack, view.transform.position, rotation));
    }

    private void Restore(DroppedItemData data)
    {
        InventorySlotState stack = data?.Stack?.ToState() ?? default;
        if (stack.IsEmpty || string.IsNullOrWhiteSpace(stack.ItemId) || stack.Wear < 0)
        {
            Debug.LogWarning($"{nameof(DroppedItemService)}: a saved dropped item is empty or broken and is removed from the save");
            _repository.Remove(data);
            return;
        }

        InventoryItem item = _catalog.GetItem(stack.ItemId);
        if (item == null || item.PickUpPrefab == null)
        {
            Debug.LogWarning($"{nameof(DroppedItemService)}: dropped item '{stack.ItemId}' has no {nameof(InventoryItem.PickUpPrefab)}; it stays in the save but is not shown");
            return;
        }

        Track(Create(item, stack, data.Position, data.Rotation), data);
    }

    private static PickUpItemInteractable Create(InventoryItem item, InventorySlotState stack, Vector3 position, Quaternion rotation)
    {
        PickUpItemInteractable view = Object.Instantiate(item.PickUpPrefab, position, rotation);
        view.name = item.PickUpPrefab.name;
        view.Setup(item, stack);
        return view;
    }

    private void Track(PickUpItemInteractable view, DroppedItemData data)
    {
        _tracked.Add(data, view.Pickup.Stack.Skip(1).Subscribe(stack => OnTaken(data, stack)));
    }

    private void OnTaken(DroppedItemData data, InventorySlotState stack)
    {
        _repository.SetStack(data, stack);
        if (stack.IsEmpty && _tracked.Remove(data, out IDisposable subscription))
            subscription.Dispose();
    }

    public void Dispose()
    {
        foreach (IDisposable subscription in _tracked.Values)
            subscription.Dispose();
        _tracked.Clear();
    }
}

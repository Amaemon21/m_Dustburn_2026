using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ItemCatalog
{
    public const int DEFAULT_MAX_STACK = 99;

    private readonly Dictionary<string, HashSet<EquipmentSlot>> _equipment = new();
    private readonly Dictionary<string, int> _maxStacks = new();
    private readonly Dictionary<string, int> _durabilities = new();
    private readonly Dictionary<string, InventoryItem> _items = new();

    public ItemCatalog()
    {
    }

    public ItemCatalog(InventoryItemDatabase database)
    {
        if (database == null)
            throw new ArgumentNullException(nameof(database));

        for (int index = 0; index < database.Items.Count; index++)
        {
            InventoryItem item = database.Items[index];
            if (item == null)
                throw new ArgumentException($"Inventory database '{database.name}' has no item asset at index {index}. Assign an InventoryItem asset or remove the entry", nameof(database));
            if (string.IsNullOrWhiteSpace(item.ItemId))
                throw new ArgumentException($"Inventory item '{item.name}' at index {index} in database '{database.name}' has an empty ItemId", nameof(database));
            if (_items.ContainsKey(item.ItemId))
                throw new InvalidOperationException($"Duplicate item id '{item.ItemId}'");
            if (item.WearsOut && item.MaxStack != 1)
                throw new ArgumentException($"Inventory item '{item.ItemId}' wears out (Durability {item.Durability}) and must stack to 1, it stacks to {item.MaxStack}", nameof(database));

            _items.Add(item.ItemId, item);
            SetMaxStack(item.ItemId, item.MaxStack);
            if (item.WearsOut)
                SetDurability(item.ItemId, item.Durability);

            if (item is EquipmentInventoryItem equipmentItem)
                RegisterEquipment(item.ItemId, equipmentItem.EquipmentSlot);
        }
    }

    public void RegisterEquipment(string itemId, params EquipmentSlot[] slots)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item id is required", nameof(itemId));

        if (_equipment.ContainsKey(itemId))
            throw new InvalidOperationException($"Duplicate item id '{itemId}'");

        HashSet<EquipmentSlot> allowed = new();

        foreach (EquipmentSlot slot in slots ?? Array.Empty<EquipmentSlot>())
        {
            if (!Enum.IsDefined(typeof(EquipmentSlot), slot))
                throw new ArgumentOutOfRangeException(nameof(slots));

            allowed.Add(slot);
        }

        _equipment.Add(itemId, allowed);
    }

    public void SetMaxStack(string itemId, int maxStack)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item id is required", nameof(itemId));
        if (maxStack < 1 || maxStack > InventorySlotProxy.MAX_AMOUNT)
            throw new ArgumentOutOfRangeException(nameof(maxStack), $"Item '{itemId}' stacks to {maxStack}, allowed 1..{InventorySlotProxy.MAX_AMOUNT}");
        if (maxStack != 1 && DurabilityOf(itemId) > 0)
            throw new InvalidOperationException($"Item '{itemId}' wears out and must stack to 1, not {maxStack}");

        _maxStacks[itemId] = maxStack;
    }

    public void SetDurability(string itemId, int durability)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item id is required", nameof(itemId));
        if (durability < 0)
            throw new ArgumentOutOfRangeException(nameof(durability), $"Item '{itemId}' has durability {durability}, it must not be negative");
        if (durability > 0 && MaxStack(itemId) != 1)
            throw new InvalidOperationException($"Item '{itemId}' wears out and must stack to 1, it stacks to {MaxStack(itemId)}");

        _durabilities[itemId] = durability;
    }

    public int MaxStack(string itemId)
        => itemId != null && _maxStacks.TryGetValue(itemId, out int maxStack) ? maxStack : DEFAULT_MAX_STACK;

    public int DurabilityOf(string itemId)
        => itemId != null && _durabilities.TryGetValue(itemId, out int durability) ? durability : 0;

    public bool IsWornOut(InventorySlotState state)
    {
        int durability = DurabilityOf(state.ItemId);
        return !state.IsEmpty && durability > 0 && state.Wear >= durability;
    }

    public bool CanEquip(string itemId, EquipmentSlot slot)
        => itemId != null && _equipment.TryGetValue(itemId, out HashSet<EquipmentSlot> allowed) && allowed.Contains(slot);

    public Sprite GetIcon(string itemId) => GetItem(itemId)?.Icon;

    public float WeightOf(string itemId)
    {
        InventoryItem item = GetItem(itemId);
        return item == null ? 0f : item.Weight;
    }

    public InventoryItem GetItem(string itemId)
        => itemId != null && _items.TryGetValue(itemId, out InventoryItem item) ? item : null;
}

using System.Collections.Generic;
using UnityEngine;

public sealed class DroppedItemRepository
{
    private readonly ISaveService _saves;

    public DroppedItemsData Origin { get; }
    public IReadOnlyList<DroppedItemData> Items => Origin.Items;

    public DroppedItemRepository(ISaveService saves)
    {
        _saves = saves;
        Origin = saves.Get<DroppedItemsData>();
        Origin.Items ??= new List<DroppedItemData>();
    }

    public DroppedItemData Add(InventorySlotState stack, Vector3 position, Quaternion rotation)
    {
        DroppedItemData item = new()
        {
            Stack = InventorySlotData.From(stack),
            Position = position,
            Rotation = rotation
        };
        Origin.Items.Add(item);
        MarkDirty();
        return item;
    }

    public void SetStack(DroppedItemData item, InventorySlotState stack)
    {
        if (stack.IsEmpty)
        {
            Remove(item);
            return;
        }

        item.Stack = InventorySlotData.From(stack);
        MarkDirty();
    }

    public void Remove(DroppedItemData item)
    {
        if (Origin.Items.Remove(item))
            MarkDirty();
    }

    private void MarkDirty() => _saves.MarkDirty<DroppedItemsData>();
}

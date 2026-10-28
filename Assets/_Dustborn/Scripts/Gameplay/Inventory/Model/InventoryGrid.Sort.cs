using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class InventoryGrid
{
    public bool Sort(InventorySortMode mode)
    {
        if (Kind != InventoryGridKind.Backpack && Kind != InventoryGridKind.Container)
            return false;

        List<Vector2Int> slots = new();
        List<InventorySlotState> stacks = new();
        for (int i = 0; i < _slots.Count; i++)
        {
            Vector2Int coords = new(i % Size.x, i / Size.x);
            if (IsLocked(coords))
                continue;
            slots.Add(coords);
            InventorySlotProxy slot = Slot(coords);
            if (!slot.IsEmpty)
                stacks.Add(new InventorySlotState(slot.ItemId, slot.Amount, slot.Wear));
        }

        List<InventorySlotState> ordered = InventorySort.Order(_catalog, mode, Merge(stacks));
        InventorySlotState[] plan = Place(ordered, slots) ?? Place(ConstrainedFirst(ordered, slots), slots);
        if (plan == null)
            return false;

        using IDisposable update = Proxy.DeferNotifications();
        List<IDisposable> deferred = new(slots.Count);
        try
        {
            foreach (Vector2Int coords in slots)
                deferred.Add(Slot(coords).DeferNotifications());
            for (int i = 0; i < slots.Count; i++)
                Slot(slots[i]).Set(plan[i].ItemId, plan[i].Amount, plan[i].Wear);
        }
        finally
        {
            foreach (IDisposable scope in deferred)
                scope.Dispose();
        }
        return true;
    }

    private List<InventorySlotState> Merge(List<InventorySlotState> stacks)
    {
        List<InventorySlotState> merged = new();
        List<string> order = new();
        Dictionary<string, int> totals = new();
        foreach (InventorySlotState stack in stacks)
        {
            if (stack.Wear != 0)
            {
                merged.Add(stack);
                continue;
            }
            if (!totals.ContainsKey(stack.ItemId))
                order.Add(stack.ItemId);
            totals[stack.ItemId] = (totals.TryGetValue(stack.ItemId, out int total) ? total : 0) + stack.Amount;
        }

        foreach (string itemId in order)
        {
            int capacity = Math.Max(1, CapacityFor(itemId));
            for (int left = totals[itemId]; left > 0; left -= capacity)
                merged.Add(new InventorySlotState(itemId, Math.Min(left, capacity)));
        }
        return merged;
    }

    private InventorySlotState[] Place(IReadOnlyList<InventorySlotState> stacks, IReadOnlyList<Vector2Int> slots)
    {
        InventorySlotState[] plan = new InventorySlotState[slots.Count];
        bool[] taken = new bool[slots.Count];
        foreach (InventorySlotState stack in stacks)
        {
            int index = FirstFree(stack.ItemId, slots, taken);
            if (index < 0)
                return null;
            taken[index] = true;
            plan[index] = stack;
        }
        return plan;
    }

    private int FirstFree(string itemId, IReadOnlyList<Vector2Int> slots, bool[] taken)
    {
        for (int i = 0; i < slots.Count; i++)
            if (!taken[i] && Accepts(slots[i], itemId))
                return i;
        return -1;
    }

    private List<InventorySlotState> ConstrainedFirst(List<InventorySlotState> stacks, IReadOnlyList<Vector2Int> slots)
    {
        List<InventorySlotState> constrained = new();
        List<InventorySlotState> free = new();
        foreach (InventorySlotState stack in stacks)
            (FitsEverywhere(stack.ItemId, slots) ? free : constrained).Add(stack);
        constrained.AddRange(free);
        return constrained;
    }

    private bool FitsEverywhere(string itemId, IReadOnlyList<Vector2Int> slots)
    {
        foreach (Vector2Int coords in slots)
            if (!Accepts(coords, itemId))
                return false;
        return true;
    }
}

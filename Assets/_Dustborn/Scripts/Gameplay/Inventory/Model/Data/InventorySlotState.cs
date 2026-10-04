using System;

public readonly struct InventorySlotState : IEquatable<InventorySlotState>
{
    public string ItemId { get; }
    public int Amount { get; }
    public bool IsEmpty => Amount == 0;

    public InventorySlotState(string itemId, int amount)
    {
        ItemId = itemId;
        Amount = amount;
    }

    public bool Equals(InventorySlotState other) => ItemId == other.ItemId && Amount == other.Amount;
    public override bool Equals(object obj) => obj is InventorySlotState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ItemId, Amount);
}

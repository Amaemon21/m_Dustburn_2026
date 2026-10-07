using System;

public readonly struct InventorySlotState : IEquatable<InventorySlotState>
{
    public string ItemId { get; }
    public int Amount { get; }
    public int Wear { get; }
    public bool IsEmpty => Amount == 0;

    public InventorySlotState(string itemId, int amount, int wear = 0)
    {
        ItemId = itemId;
        Amount = amount;
        Wear = wear;
    }

    public InventorySlotState WithAmount(int amount) => new(ItemId, amount, Wear);

    public bool Equals(InventorySlotState other) => ItemId == other.ItemId && Amount == other.Amount && Wear == other.Wear;
    public override bool Equals(object obj) => obj is InventorySlotState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ItemId, Amount, Wear);
}

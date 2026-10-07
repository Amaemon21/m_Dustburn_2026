using System;

[Serializable]
public class InventorySlotData
{
    public string ItemId;
    public int Amount;
    public int Wear;

    public static InventorySlotData From(InventorySlotState state)
        => new() { ItemId = state.ItemId, Amount = state.Amount, Wear = state.Wear };

    public InventorySlotState ToState() => new(ItemId, Amount, Wear);
}

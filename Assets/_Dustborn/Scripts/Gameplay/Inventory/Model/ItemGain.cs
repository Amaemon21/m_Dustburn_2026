public readonly struct ItemGain
{
    public string OwnerId { get; }
    public InventorySlotState Stack { get; }

    public ItemGain(string ownerId, InventorySlotState stack)
    {
        OwnerId = ownerId;
        Stack = stack;
    }
}

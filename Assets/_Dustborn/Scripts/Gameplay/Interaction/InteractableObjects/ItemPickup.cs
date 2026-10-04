using System;

public sealed class ItemPickup
{
    private readonly string _itemId;
    public int Remaining { get; private set; }

    public ItemPickup(string itemId, int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        
        _itemId = itemId;
        Remaining = amount;
    }

    public void PickUp(IInteractionContext context)
    {
        if (Remaining == 0)
            return;
        
        Remaining -= context.PickUp(_itemId, Remaining);
    }
}

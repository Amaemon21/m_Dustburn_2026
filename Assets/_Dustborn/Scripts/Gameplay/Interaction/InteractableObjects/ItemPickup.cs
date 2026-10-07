using System;
using R3;

public sealed class ItemPickup : IDisposable
{
    private readonly ReactiveProperty<InventorySlotState> _stack;

    public ReadOnlyReactiveProperty<InventorySlotState> Stack => _stack;
    public int Remaining => _stack.CurrentValue.Amount;

    public ItemPickup(InventorySlotState stack)
    {
        if (string.IsNullOrWhiteSpace(stack.ItemId))
            throw new ArgumentException("A pickup needs an item id", nameof(stack));
        if (stack.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(stack), "A pickup must hold at least one item");
        if (stack.Wear < 0)
            throw new ArgumentOutOfRangeException(nameof(stack), "A pickup cannot have negative wear");

        _stack = new ReactiveProperty<InventorySlotState>(stack);
    }

    public void PickUp(IInteractionContext context)
    {
        InventorySlotState stack = _stack.Value;
        if (stack.IsEmpty)
            return;

        int taken = context.PickUp(stack);
        if (taken > 0)
            _stack.Value = stack.WithAmount(stack.Amount - taken);
    }

    public void Dispose() => _stack.Dispose();
}

using R3;
public interface IReadOnlyInventorySlot
{
    string ItemId { get; }
    int Amount { get; }
    bool IsEmpty { get; }
    ReadOnlyReactiveProperty<InventorySlotState> State { get; }
}

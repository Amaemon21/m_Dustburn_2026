using R3;
public interface IReadOnlyInventory
{
    string OwnerId { get; }
    Observable<(string ItemId, int Amount)> ItemsAdded { get; }
    Observable<(string ItemId, int Amount)> ItemsRemoved { get; }
    int GetAmount(string itemId);
    bool Has(string itemId, int amount = 1);
}

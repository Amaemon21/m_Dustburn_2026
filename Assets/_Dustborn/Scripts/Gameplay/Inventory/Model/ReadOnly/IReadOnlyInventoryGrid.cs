using R3;
using UnityEngine;
public interface IReadOnlyInventoryGrid : IReadOnlyInventory
{
    Vector2Int Size { get; }
    InventoryGridKind Kind { get; }
    Observable<Vector2Int> SizeChanged { get; }
    Observable<Unit> Changed { get; }
    int CapacityFor(string itemId);
    IReadOnlyInventorySlot GetSlot(Vector2Int coords);
    IReadOnlyInventorySlot[,] GetSlots();
}

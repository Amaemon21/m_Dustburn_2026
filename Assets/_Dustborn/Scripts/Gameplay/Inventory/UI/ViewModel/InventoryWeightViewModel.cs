using R3;
using UnityEngine;

public sealed class InventoryWeightViewModel : ViewModel
{
    private readonly IReadOnlyInventoryGrid[] _grids;
    private readonly ItemCatalog _catalog;
    private readonly ReactiveProperty<float> _total = new(0f);

    public ReadOnlyReactiveProperty<float> Total => _total;

    public InventoryWeightViewModel(ItemCatalog catalog, params IReadOnlyInventoryGrid[] grids)
    {
        _catalog = catalog;
        _grids = grids;
        foreach (IReadOnlyInventoryGrid grid in grids)
            Disposables.Add(grid.Changed.Subscribe(_ => Refresh()));
        Refresh();
    }

    private void Refresh()
    {
        float total = 0f;
        foreach (IReadOnlyInventoryGrid grid in _grids)
            for (int x = 0; x < grid.Size.x; x++)
                for (int y = 0; y < grid.Size.y; y++)
                {
                    IReadOnlyInventorySlot slot = grid.GetSlot(new Vector2Int(x, y));
                    if (!slot.IsEmpty)
                        total += _catalog.WeightOf(slot.ItemId) * slot.Amount;
                }
        _total.Value = total;
    }

    protected override void OnDisposed() => _total.Dispose();
}

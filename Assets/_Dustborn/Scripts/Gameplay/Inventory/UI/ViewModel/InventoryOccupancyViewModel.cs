using R3;
using UnityEngine;

public sealed class InventoryOccupancyViewModel : ViewModel
{
    private readonly IReadOnlyInventoryGrid _grid;
    private readonly ReactiveProperty<int> _used = new(0);
    private readonly ReactiveProperty<int> _total = new(0);

    public ReadOnlyReactiveProperty<int> Used => _used;
    public ReadOnlyReactiveProperty<int> Total => _total;

    public InventoryOccupancyViewModel(IReadOnlyInventoryGrid grid)
    {
        _grid = grid;
        Disposables.Add(grid.Changed.Subscribe(_ => Refresh()));
        Refresh();
    }

    private void Refresh()
    {
        int used = 0;
        for (int x = 0; x < _grid.Size.x; x++)
            for (int y = 0; y < _grid.Size.y; y++)
                if (!_grid.GetSlot(new Vector2Int(x, y)).IsEmpty)
                    used++;

        _total.Value = _grid.Size.x * _grid.Size.y;
        _used.Value = used;
    }

    protected override void OnDisposed()
    {
        _used.Dispose();
        _total.Dispose();
    }
}

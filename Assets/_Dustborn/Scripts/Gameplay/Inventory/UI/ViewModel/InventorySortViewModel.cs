using System;
using System.Collections.Generic;
using R3;

public sealed class InventorySortViewModel : ViewModel
{
    private static readonly InventorySortMode[] MODES =
    {
        InventorySortMode.Type,
        InventorySortMode.Name,
        InventorySortMode.Amount,
        InventorySortMode.Weight
    };

    public IReadOnlyList<InventorySortMode> Modes => MODES;
    public InventorySortMode DefaultMode => InventorySort.DEFAULT_MODE;
    public ReactiveCommand<InventorySortMode> Sort { get; } = new();

    public InventorySortViewModel(Action<InventorySortMode> sort)
    {
        if (sort == null)
            throw new ArgumentNullException(nameof(sort));
        Disposables.Add(Sort.Executed.Subscribe(sort));
    }

    protected override void OnDisposed()
    {
        Sort.Dispose();
        base.OnDisposed();
    }
}

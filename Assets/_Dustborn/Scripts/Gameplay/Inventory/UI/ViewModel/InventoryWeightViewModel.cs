using System;
using R3;

public sealed class InventoryWeightViewModel : ViewModel
{
    private readonly IReadOnlyInventoryWeight _weight;

    public ReadOnlyReactiveProperty<float> Current => _weight.Current;
    public IReadOnlyStat Capacity => _weight.Capacity;
    public bool HasLimit => _weight.HasLimit;
    public ReadOnlyReactiveProperty<bool> IsOverloaded => _weight.IsOverloaded;
    public ReadOnlyReactiveProperty<CarryZone> Zone => _weight.Zone;

    public InventoryWeightViewModel(IReadOnlyInventoryWeight weight)
    {
        _weight = weight ?? throw new ArgumentNullException(nameof(weight));
    }

    public static InventoryWeightViewModel ForGrid(ItemCatalog catalog, IReadOnlyInventoryGrid grid)
    {
        InventoryWeight weight = new(catalog, null, grid);
        InventoryWeightViewModel viewModel = new(weight);
        viewModel.Disposables.Add(weight);
        return viewModel;
    }
}

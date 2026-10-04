using System;
using R3;

public class HudHotbarSlotViewModel : ItemSlotViewModel
{
    public int Index { get; }
    public string KeyLabel => Index == 9 ? "0" : (Index + 1).ToString();

    public HudHotbarSlotViewModel(IReadOnlyInventorySlot slot, int index,
        ReadOnlyReactiveProperty<int> selectedSlot, ItemCatalog catalog,
        Action<int> onSelected = null) : base(slot, catalog)
    {
        Index = index;
        Disposables.Add(Select.Executed.Subscribe(_ => onSelected?.Invoke(Index)));
        Disposables.Add(selectedSlot.Subscribe(selected => SetSelected(selected == Index)));
    }
}

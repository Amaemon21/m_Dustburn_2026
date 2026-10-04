using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using UnityEngine;
using VContainer;

public sealed class InventoryView : View<InventoryGridViewModel>
{
    [SerializeField, Required] private InventorySlotView _slotPrefab;
    [SerializeField, Required] private Transform _content;
    [SerializeField] private InventoryOccupancyView _occupancy;
    [SerializeField] private InventoryWeightView _weight;
    private SlotViewPool<InventorySlotView> _pool;

    [Inject]
    public void Construct(IObjectResolver resolver)
    {
        _pool = new SlotViewPool<InventorySlotView>(_slotPrefab, _content, resolver);
    }

    protected override void BindCore(InventoryGridViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Slots.Subscribe(BindSlots));

        if (_occupancy != null)
            _occupancy.Bind(viewModel.Occupancy);
        if (_weight != null)
            _weight.Bind(viewModel.Weight);
    }

    private void BindSlots(IReadOnlyList<InventorySlotViewModel> slots)
    {
        UnbindSlots();
        IReadOnlyList<InventorySlotView> views = _pool.Show(slots.Count);
        for (int i = 0; i < slots.Count; i++)
            views[i].Bind(slots[i]);
    }

    private void UnbindSlots()
    {
        if (_pool == null)
            return;

        foreach (InventorySlotView slot in _pool.Views)
            slot.Unbind();
    }

    protected override void OnUnbound()
    {
        UnbindSlots();

        if (_occupancy != null)
            _occupancy.Unbind();
        if (_weight != null)
            _weight.Unbind();
    }
}

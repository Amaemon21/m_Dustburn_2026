using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using UnityEngine;
using VContainer;

public class HotbarView : View<HotbarViewModel>
{
    [SerializeField, Required] private HotbarViewSlot _slotPrefab;
    [SerializeField, Required] private Transform _content;
    private SlotViewPool<HotbarViewSlot> _pool;

    [Inject]
    public void Construct(IObjectResolver resolver)
    {
        _pool = new SlotViewPool<HotbarViewSlot>(_slotPrefab, _content, resolver);
    }

    protected override void BindCore(HotbarViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Grid.Slots.Subscribe(BindSlots));
    }

    private void BindSlots(IReadOnlyList<InventorySlotViewModel> slots)
    {
        UnbindSlots();
        IReadOnlyList<HotbarViewSlot> views = _pool.Show(slots.Count);
        for (int i = 0; i < slots.Count; i++)
            views[i].Bind(slots[i], i);
    }

    private void UnbindSlots()
    {
        if (_pool == null)
            return;

        foreach (HotbarViewSlot slot in _pool.Views)
            slot.Unbind();
    }

    protected override void OnUnbound() => UnbindSlots();
}

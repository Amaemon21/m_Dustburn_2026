using System;
using R3;
using UnityEngine;

public sealed class EquipmentView : View<InventoryGridViewModel>
{
    [Serializable]
    private sealed class SlotBinding
    {
        public EquipmentSlot Slot;
        public EquipmentSlotView View;
    }

    [SerializeField] private SlotBinding[] _slots = Array.Empty<SlotBinding>();

    protected override void BindCore(InventoryGridViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Slots.Subscribe(slots =>
        {
            foreach (SlotBinding slot in _slots)
            {
                slot.View.Bind(slots[(int)slot.Slot]);
            }
        }));
    }

    protected override void OnUnbound()
    {
        foreach (SlotBinding slot in _slots)
        {
            slot.View.Unbind();
        }
    }
}

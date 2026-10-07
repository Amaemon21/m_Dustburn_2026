using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventorySlotView : InteractiveItemSlotView<InventorySlotViewModel>
{
    [SerializeField] private TMP_Text _textAmount;

    protected override InventorySlotViewModel DraggableSlot => ViewModel;

    protected override void BindSlot(InventorySlotViewModel viewModel, CompositeDisposable bindings)
    {
        base.BindSlot(viewModel, bindings);
        bindings.Add(viewModel.State.Subscribe(_ => RefreshStack(viewModel)));
        bindings.Add(viewModel.DraggedAmount.Subscribe(_ => RefreshStack(viewModel)));
    }

    private void RefreshStack(InventorySlotViewModel viewModel)
    {
        int amount = viewModel.DisplayAmount;
        _textAmount.text = SlotAmountText.Format(amount);
    }

    protected override void UnbindSlot()
    {
        _textAmount.text = string.Empty;
        base.UnbindSlot();
    }
}

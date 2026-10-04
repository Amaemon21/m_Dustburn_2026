using R3;
using TMPro;
using UnityEngine;

public class HudHotbarSlotView : ItemSlotView<HudHotbarSlotViewModel>
{
    [SerializeField] private TMP_Text _textAmount;
    [SerializeField] private TMP_Text _keyLabel;

    protected override void BindSlot(HudHotbarSlotViewModel viewModel, CompositeDisposable bindings)
    {
        _keyLabel.text = viewModel.KeyLabel;
        bindings.Add(viewModel.State.Subscribe(_ => _textAmount.text = SlotAmountText.Format(viewModel.DisplayAmount)));
    }

    protected override void UnbindSlot()
    {
        _textAmount.text = string.Empty;
        _keyLabel.text = string.Empty;
    }
}

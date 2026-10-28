using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ConfirmDialogView : View<ConfirmDialogViewModel>
{
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField, Required] private Button _confirmButton;
    [SerializeField, Required] private Button _cancelButton;

    protected override void BindCore(ConfirmDialogViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.IsOpen.Subscribe(gameObject.SetActive));
        bindings.Add(viewModel.Text.Subscribe(text => _text.text = text));
        _confirmButton.BindCommand(viewModel.Confirm, bindings);
        _cancelButton.BindCommand(viewModel.Cancel, bindings);
    }
}

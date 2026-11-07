using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class OptionFieldView : View<OptionFieldViewModel>
{
    [SerializeField, Required] private Button _previous;
    [SerializeField, Required] private TMP_Text _value;
    [SerializeField, Required] private Button _next;

    protected override void BindCore(OptionFieldViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Label.Subscribe(label => _value.text = label));
        _previous.BindCommand(viewModel.Previous, bindings);
        _next.BindCommand(viewModel.Next, bindings);
    }
}

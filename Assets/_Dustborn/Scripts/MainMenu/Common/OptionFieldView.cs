using System.Collections.Generic;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;

public sealed class OptionFieldView : View<OptionFieldViewModel>
{
    [SerializeField, Required] private TMP_Dropdown _dropdown;

    protected override void BindCore(OptionFieldViewModel viewModel, CompositeDisposable bindings)
    {
        _dropdown.ClearOptions();
        _dropdown.AddOptions(new List<string>(viewModel.Labels));

        bindings.Add(viewModel.Index.Subscribe(index => _dropdown.SetValueWithoutNotify(index)));
        bindings.Add(_dropdown.onValueChanged.AsObservable().Subscribe(index => viewModel.Index.Value = index));
    }
}

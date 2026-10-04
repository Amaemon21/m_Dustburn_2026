using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;

public sealed class InventoryWeightView : View<InventoryWeightViewModel>
{
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField] private string _format = "{0:0.#} кг";

    protected override void BindCore(InventoryWeightViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Total.Subscribe(total => _text.text = string.Format(_format, total)));
    }

    protected override void OnUnbound()
    {
        if (_text != null)
            _text.text = string.Empty;
    }
}

using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;

public sealed class InventoryWeightView : View<InventoryWeightViewModel>
{
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField] private string _format = "{0:0.#} кг";
    [SerializeField] private string _limitedFormat = "{0:0.#}/{1:0.#} кг";
    [SerializeField] private Color _overloadedColor = new(0.85f, 0.25f, 0.2f);

    private Color _normalColor;
    private bool _normalColorCaptured;

    protected override void BindCore(InventoryWeightViewModel viewModel, CompositeDisposable bindings)
    {
        CaptureNormalColor();

        if (viewModel.HasLimit)
            bindings.Add(viewModel.Current.CombineLatest(viewModel.Capacity.Value, viewModel.Zone, (current, max, zone) => (current, max, zone))
                .Subscribe(value => _text.text = string.Format(_limitedFormat, value.current, value.max, value.zone?.Label ?? string.Empty)));
        else
            bindings.Add(viewModel.Current.Subscribe(current => _text.text = string.Format(_format, current)));

        bindings.Add(viewModel.IsOverloaded.CombineLatest(viewModel.Zone, (overloaded, zone) => (overloaded, zone))
            .Subscribe(value => _text.color = ColorFor(value.overloaded, value.zone)));
    }

    private Color ColorFor(bool overloaded, CarryZone zone)
    {
        if (zone != null)
            return zone.Color;
        return overloaded ? _overloadedColor : _normalColor;
    }

    private void CaptureNormalColor()
    {
        if (_normalColorCaptured)
            return;

        _normalColor = _text.color;
        _normalColorCaptured = true;
    }

    protected override void OnUnbound()
    {
        if (_text == null)
            return;

        _text.text = string.Empty;
        if (_normalColorCaptured)
            _text.color = _normalColor;
    }
}

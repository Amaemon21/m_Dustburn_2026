using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;

public sealed class InventoryWeightView : View<InventoryWeightViewModel>
{
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField] private string _format;
    [SerializeField] private string _limitedFormat;
    [SerializeField] private Color _overloadedColor = new(0.85f, 0.25f, 0.2f);

    private Color _normalColor;
    private bool _normalColorCaptured;

    protected override void BindCore(InventoryWeightViewModel viewModel, CompositeDisposable bindings)
    {
        CaptureNormalColor();

        if (viewModel.HasLimit)
        {
            string format = string.IsNullOrEmpty(_limitedFormat) ? LocalizationService.Text("inventory.weight.limited") : _limitedFormat;
            bindings.Add(viewModel.Current.CombineLatest(viewModel.Capacity.Value, viewModel.Zone, (current, max, zone) => (current, max, zone))
                .Subscribe(value => _text.text = string.Format(format, value.current, value.max, value.zone?.Label ?? string.Empty)));
        }
        else
        {
            string format = string.IsNullOrEmpty(_format) ? LocalizationService.Text("inventory.weight.current") : _format;
            bindings.Add(viewModel.Current.Subscribe(current => _text.text = string.Format(format, current)));
        }

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

using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class VitalBarView : View<VitalViewModel>
{
    [SerializeField, Required] private Image _fill;
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField] private VitalDisplay _display = VitalDisplay.Value;
    [SerializeField] private string _valueFormat = "{0}/{1}";
    [SerializeField] private string _percentFormat = "{0}%";

    protected override void BindCore(VitalViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Current.CombineLatest(viewModel.Max, (current, max) => (current, max))
            .Subscribe(value => Show(value.current, value.max)));
    }

    private void Show(float current, float max)
    {
        float fraction = max <= 0f ? 0f : Mathf.Clamp01(current / max);
        _fill.fillAmount = fraction;
        _text.text = _display == VitalDisplay.Percent
            ? string.Format(_percentFormat, Mathf.CeilToInt(fraction * 100f))
            : string.Format(_valueFormat, Mathf.CeilToInt(current), Mathf.CeilToInt(max));
    }

    protected override void OnUnbound()
    {
        if (_fill != null)
            _fill.fillAmount = 0f;
        if (_text != null)
            _text.text = string.Empty;
    }
}

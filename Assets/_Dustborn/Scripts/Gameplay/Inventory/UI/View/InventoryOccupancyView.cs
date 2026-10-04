using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;

public sealed class InventoryOccupancyView : View<InventoryOccupancyViewModel>
{
    [SerializeField, Required] private TMP_Text _text;
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _fullColor = new(0.85f, 0.25f, 0.2f);

    protected override void BindCore(InventoryOccupancyViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Used.CombineLatest(viewModel.Total, (used, total) => (used, total))
            .Subscribe(value => Show(value.used, value.total)));
    }

    private void Show(int used, int total)
    {
        _text.text = $"{used}/{total}";
        _text.color = used >= total ? _fullColor : _normalColor;
    }

    protected override void OnUnbound()
    {
        if (_text != null)
            _text.text = string.Empty;
    }
}

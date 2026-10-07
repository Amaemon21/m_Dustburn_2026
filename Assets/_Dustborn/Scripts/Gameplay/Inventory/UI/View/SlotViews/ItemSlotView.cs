using NaughtyAttributes;
using R3;
using UnityEngine;
using UnityEngine.UI;

public abstract class ItemSlotView<T> : View<T> where T : ItemSlotViewModel
{
    [SerializeField] private Image _icon;

    [Space(10)]
    [SerializeField, Required] private Image _rarityImage;
    [SerializeField, Required] private ItemRarityPalette _rarityPalette;

    [Space(10)]
    [SerializeField] private Image _frameImage;
    [SerializeField] private Sprite _idleFrame;
    [SerializeField] private Sprite _selectionFrame;

    [Space(10)]
    [SerializeField] private GameObject _durabilityPanel;
    [SerializeField] private Image _durabilityFill;

    protected bool IsSelected => ViewModel != null && ViewModel.IsSelected.CurrentValue;

    protected sealed override void BindCore(T viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Icon.Subscribe(_ => RefreshIcon(viewModel)));
        bindings.Add(viewModel.DraggedAmount.Subscribe(_ => RefreshIcon(viewModel)));
        bindings.Add(viewModel.Rarity.Subscribe(_ => RefreshIcon(viewModel)));
        bindings.Add(viewModel.Durability.Subscribe(_ => RefreshDurability(viewModel)));
        bindings.Add(viewModel.DraggedAmount.Subscribe(_ => RefreshDurability(viewModel)));
        bindings.Add(viewModel.IsSelected.Subscribe(_ => RefreshFrame()));

        BindSlot(viewModel, bindings);
    }

    protected virtual void BindSlot(T viewModel, CompositeDisposable bindings) { }
    protected virtual void UnbindSlot() { }

    protected virtual Sprite ResolveFrame(bool selected) => selected ? _selectionFrame : _idleFrame;

    protected void RefreshFrame()
    {
        _frameImage.overrideSprite = null;
        _frameImage.sprite = ResolveFrame(IsSelected);
    }

    private void RefreshIcon(T viewModel)
    {
        _icon.sprite = viewModel.Icon.CurrentValue;
        _icon.enabled = viewModel.Icon.CurrentValue != null && viewModel.DisplayAmount > 0;
        RefreshRarity(viewModel.Rarity.CurrentValue, viewModel.DisplayAmount > 0);
    }

    private void RefreshDurability(T viewModel)
    {
        if (_durabilityFill == null)
            return;

        float? durability = viewModel.Durability.CurrentValue;
        bool visible = durability.HasValue && viewModel.DisplayAmount > 0;
        ShowDurability(visible);
        if (!visible)
            return;

        _durabilityFill.fillAmount = durability.Value;
    }

    private void ShowDurability(bool visible)
    {
        if (_durabilityPanel != null)
            _durabilityPanel.SetActive(visible);
        if (_durabilityFill != null)
            _durabilityFill.enabled = visible;
    }

    private void RefreshRarity(ItemRarity? rarity, bool visible)
    {
        Color color = default;
        bool hasColor = rarity.HasValue && _rarityPalette.TryGetColor(rarity.Value, out color);
        _rarityImage.enabled = visible && hasColor;
        if (hasColor)
            _rarityImage.color = color;
    }

    protected sealed override void OnUnbound()
    {
        _icon.sprite = null;
        _icon.enabled = false;
        _rarityImage.enabled = false;
        ShowDurability(false);
        RefreshFrame();

        UnbindSlot();
    }
}

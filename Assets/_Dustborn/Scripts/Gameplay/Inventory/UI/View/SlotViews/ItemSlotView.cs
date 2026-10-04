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

    protected bool IsSelected => ViewModel != null && ViewModel.IsSelected.CurrentValue;

    protected sealed override void BindCore(T viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.Icon.Subscribe(_ => RefreshIcon(viewModel)));
        bindings.Add(viewModel.DraggedAmount.Subscribe(_ => RefreshIcon(viewModel)));
        bindings.Add(viewModel.Rarity.Subscribe(_ => RefreshIcon(viewModel)));
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

    private void RefreshRarity(ItemRarity? rarity, bool visible)
    {
        Sprite sprite = rarity.HasValue ? _rarityPalette.SpriteOf(rarity.Value) : null;
        _rarityImage.sprite = sprite;
        _rarityImage.enabled = visible && sprite != null;
    }

    protected sealed override void OnUnbound()
    {
        _icon.sprite = null;
        _icon.enabled = false;
        _rarityImage.enabled = false;
        RefreshFrame();

        UnbindSlot();
    }
}

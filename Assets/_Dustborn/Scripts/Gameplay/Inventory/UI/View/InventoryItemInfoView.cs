using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public class InventoryItemInfoView : View<InventoryItemInfoViewModel>
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _categoryText;
    [SerializeField] private Image _iconImage;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField, Required] private ItemRarityPalette _rarityPalette;
    [SerializeField, Required] private ItemStatsView _stats;
    [SerializeField, Required] private InputHintView _useHint;
    [SerializeField, Required] private InputHintView _dropHint;

    [Space(10)]
    [SerializeField, Required] private GameObject _durabilityPanel;
    [SerializeField, Required] private GameObject _durabilityLine;
    [SerializeField, Required] private TMP_Text _durabilityText;
    [SerializeField, Required] private Image _durabilityFill;

    private UIInputService _input;
    private Color? _nameColor;

    [Inject]
    public void Construct(UIInputService input)
    {
        _input = input;
    }

    protected override void BindCore(InventoryItemInfoViewModel viewModel, CompositeDisposable bindings)
    {
        _useHint.SetKey(_input.UseItemBinding);
        _dropHint.SetKey(_input.DropItemBinding);

        bindings.Add(viewModel.State.Subscribe(state =>
        {
            InventoryItem item = viewModel.Item;
            float? durability = viewModel.Durability;

            if (item != null)
                OnItemChanged(item, state);
            else
                ClearItem();

            SetDurabilityVisible(durability.HasValue);

            if (durability.HasValue)
                RefreshDurability(durability.Value);
        }));
        bindings.Add(viewModel.Use.CanExecute.Subscribe(_useHint.SetVisible));
        bindings.Add(viewModel.Drop.CanExecute.Subscribe(_dropHint.SetVisible));
        bindings.Add(_input.UseItemPressed.Subscribe(_ => viewModel.Use.Execute(Unit.Default)));
        bindings.Add(_input.DropItemPressed.Subscribe(_ => viewModel.Drop.Execute(Unit.Default)));
    }

    protected virtual void OnItemChanged(InventoryItem item, InventorySlotState state)
    {
        _nameColor ??= _nameText.color;

        _nameText.text = item.ItemName;
        _nameText.color = NameColor(item.Rarity);
        _categoryText.text = ItemInfoText.Subtitle(item);
        _iconImage.sprite = item.Icon;
        _descriptionText.text = item.Description;
        _stats.Show(item, state);
    }

    private void ClearItem()
    {
        _nameColor ??= _nameText.color;
        _nameText.text = string.Empty;
        _nameText.color = _nameColor.Value;
        _categoryText.text = string.Empty;
        _iconImage.sprite = null;
        _descriptionText.text = string.Empty;
        _stats.Clear();
    }

    private Color NameColor(ItemRarity rarity)
    {
        if (rarity == ItemRarity.None || !_rarityPalette.TryGetColor(rarity, out Color color) || color.a <= 0f)
            return _nameColor.Value;

        return color;
    }

    private void SetDurabilityVisible(bool visible)
    {
        _durabilityPanel.SetActive(visible);
        _durabilityLine.SetActive(visible);
    }

    private void RefreshDurability(float durability)
    {
        _durabilityFill.fillAmount = durability;
        _durabilityText.text = $"{Mathf.CeilToInt(durability * 100f)}%";
    }

    public void CloseSelection()
    {
        ViewModel?.Clear();
    }

    protected override void OnUnbound()
    {
        ClearItem();
        SetDurabilityVisible(false);
        _useHint.SetVisible(false);
        _dropHint.SetVisible(false);
    }
}

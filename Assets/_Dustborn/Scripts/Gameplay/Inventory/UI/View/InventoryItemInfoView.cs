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
    [SerializeField, Required] private ItemStatsView _stats;
    [SerializeField, Required] private InputHintView _useHint;
    [SerializeField, Required] private InputHintView _dropHint;
    private UIInputService _input;

    [Inject]
    public void Construct(UIInputService input)
    {
        _input = input;
    }

    protected override void BindCore(InventoryItemInfoViewModel viewModel, CompositeDisposable bindings)
    {
        _useHint.SetKey(_input.UseItemBinding);
        _dropHint.SetKey(_input.DropItemBinding);

        bindings.Add(viewModel.State.Subscribe(state => OnItemChanged(viewModel.Item, state)));
        bindings.Add(viewModel.Use.CanExecute.Subscribe(_useHint.SetVisible));
        bindings.Add(viewModel.Drop.CanExecute.Subscribe(_dropHint.SetVisible));
        bindings.Add(_input.UseItemPressed.Subscribe(_ => viewModel.Use.Execute(Unit.Default)));
        bindings.Add(_input.DropItemPressed.Subscribe(_ => viewModel.Drop.Execute(Unit.Default)));
    }

    protected virtual void OnItemChanged(InventoryItem item, InventorySlotState state)
    {
        if (item == null)
        {
            _nameText.text = string.Empty;
            _categoryText.text = string.Empty;
            _iconImage.sprite = null;
            _descriptionText.text = string.Empty;
            _stats.Clear();
            return;
        }

        _nameText.text = item.ItemName;
        _categoryText.text = item.Category.ToString();
        _iconImage.sprite = item.Icon;
        _descriptionText.text = item.Description;
        _stats.Show(item, state.Amount);
    }

    public void CloseSelection()
    {
        ViewModel?.Clear();
    }

    protected override void OnUnbound()
    {
        OnItemChanged(null, new InventorySlotState(null, 0));
        _useHint.SetVisible(false);
        _dropHint.SetVisible(false);
    }
}

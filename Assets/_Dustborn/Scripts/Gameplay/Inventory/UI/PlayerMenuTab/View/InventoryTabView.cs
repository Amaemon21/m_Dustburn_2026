using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;

public sealed class InventoryTabView : View<InventoryTabViewModel>, IPointerClickHandler
{
    [SerializeField] private InventoryView _backpack;
    [SerializeField] private HotbarView _hotbar;
    [SerializeField] private EquipmentView _equipment;
    [SerializeField] private InventoryItemInfoView _itemInfo;
    [SerializeField] private GameObject _playerInfoPanel;
    [SerializeField] private DraggableSlotView _draggableSlot;
    private UIInputService _input;

    [Inject]
    public void Construct(UIInputService input)
    {
        _input = input;
    }

    protected override void BindCore(InventoryTabViewModel viewModel, CompositeDisposable bindings)
    {
        _backpack.Bind(viewModel.Backpack);
        _hotbar.Bind(viewModel.Hotbar);
        _equipment.Bind(viewModel.Equipment);
        _itemInfo.Bind(viewModel.ItemInfo);
        _draggableSlot.Bind(viewModel.DragDrop);
        bindings.Add(_input.Scrolled.Subscribe(direction => viewModel.DragDrop.ChangeAmount(direction)));
        bindings.Add(viewModel.ItemInfo.State.Subscribe(state => SetItemInfoVisible(!state.IsEmpty)));
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        CloseSelection();
    }

    public void CloseSelection()
    {
        ViewModel?.ItemInfo.Clear();
    }

    private void SetItemInfoVisible(bool visible)
    {
        _itemInfo.gameObject.SetActive(visible);
        if (_playerInfoPanel != _itemInfo.gameObject)
            _playerInfoPanel.SetActive(!visible);
    }

    protected override void OnUnbound()
    {
        ViewModel?.DragDrop.Cancel();
        _draggableSlot.Unbind();
        _backpack.Unbind();
        _hotbar.Unbind();
        _equipment.Unbind();
        _itemInfo.Unbind();
        SetItemInfoVisible(false);
    }

    private void OnDisable()
    {
        ViewModel?.DragDrop.Cancel();
    }
}

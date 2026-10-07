using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;

public abstract class InteractiveItemSlotView<T> : ItemSlotView<T>, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler where T : ItemSlotViewModel
{
    [SerializeField] private Sprite _hoverFrame;

    private bool _isHovered;
    private InventorySlotViewModel _dragSlot;
    private UIInputService _input;

    protected abstract InventorySlotViewModel DraggableSlot { get; }

    [Inject]
    public void Construct(UIInputService input)
    {
        _input = input;
    }

    protected override void BindSlot(T viewModel, CompositeDisposable bindings)
    {
        _dragSlot = DraggableSlot;
    }

    protected override void UnbindSlot()
    {
        _dragSlot?.DragDrop?.Cancel(_dragSlot);
        _dragSlot = null;
        _isHovered = false;
        RefreshFrame();
    }

    protected override Sprite ResolveFrame(bool selected)
        => !selected && _isHovered ? _hoverFrame : base.ResolveFrame(selected);

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        RefreshFrame();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        RefreshFrame();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
            return;

        if (_dragSlot != null && _input.IsQuickTransferPressed)
            _dragSlot.QuickTransfer.Execute(Unit.Default);
        else
            ViewModel?.Select.Execute(Unit.Default);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || _dragSlot?.DragDrop == null)
            return;

        if (_dragSlot.DragDrop.Begin(_dragSlot, eventData.position, _input.IsQuickTransferPressed))
            eventData.eligibleForClick = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _dragSlot?.DragDrop?.Move(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && eventData.pointerCurrentRaycast.gameObject == null)
            _dragSlot?.DragDrop?.DropToWorld(_dragSlot);

        _dragSlot?.DragDrop?.Cancel(_dragSlot);
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _dragSlot?.DragDrop?.Drop(_dragSlot);
    }

    private void OnDisable()
    {
        _dragSlot?.DragDrop?.Cancel(_dragSlot);
        _isHovered = false;
        RefreshFrame();
    }
}

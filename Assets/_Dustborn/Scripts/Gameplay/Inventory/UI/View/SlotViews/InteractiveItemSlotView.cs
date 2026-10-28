using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer;

public abstract class InteractiveItemSlotView<T> : ItemSlotView<T>, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler where T : ItemSlotViewModel
{
    
    [Inject] private readonly UIInputService _input;
    
    [SerializeField] private Sprite _hoverFrame;

    private bool _isHovered;
    private T _slot;
    private InventorySlotViewModel _dragSlot;

    protected abstract InventorySlotViewModel DraggableSlot { get; }

    protected override void BindSlot(T viewModel, CompositeDisposable bindings)
    {
        _slot = viewModel;
        _dragSlot = DraggableSlot;
    }

    protected override void UnbindSlot()
    {
        _dragSlot?.DragDrop?.Cancel(_dragSlot);
        _dragSlot = null;
        SetHovered(false);
        _slot = null;
    }

    protected override Sprite ResolveFrame(bool selected)
        => !selected && _isHovered ? _hoverFrame : base.ResolveFrame(selected);

    public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);

    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

    public void OnPointerDown(PointerEventData eventData) => _slot?.SetHover(null);

    private void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        _slot?.SetHover(hovered ? TopEdge() : (Vector3?)null);
        RefreshFrame();
    }

    private Vector3 TopEdge()
    {
        RectTransform rectTransform = (RectTransform)transform;
        Rect rect = rectTransform.rect;
        return rectTransform.TransformPoint(new Vector3(rect.center.x, rect.yMax, 0f));
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
        SetHovered(false);
    }
}

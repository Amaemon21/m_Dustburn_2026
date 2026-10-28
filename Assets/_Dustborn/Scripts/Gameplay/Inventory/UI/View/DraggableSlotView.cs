using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DraggableSlotView : View<InventoryDragDropViewModel>
{
    private Canvas _canvas;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _amountText;
    [SerializeField, Foldout("Motion")] private DragSwingSettings _motion = new();

    private DragSwing _swing;
    private Vector2 _localPosition;
    private bool _dragging;
    private bool _settled;

    protected override void BindCore(InventoryDragDropViewModel viewModel, CompositeDisposable bindings)
    {
        _canvas = GetComponentInParent<Canvas>(true).rootCanvas;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
        _swing ??= new DragSwing(_motion);
        bindings.Add(viewModel.Icon.Subscribe(icon => _icon.sprite = icon));
        bindings.Add(viewModel.Position.Subscribe(Move));
        bindings.Add(viewModel.State.Subscribe(Show));
        bindings.Add(Observable.EveryUpdate().Subscribe(_ => Animate()));
    }

    private void Show(InventorySlotState state)
    {
        _amountText.text = state.Amount <= 1 ? string.Empty : state.Amount.ToString();
        bool dragging = !state.IsEmpty;
        if (dragging && !_dragging)
            Grab();
        _dragging = dragging;
        gameObject.SetActive(dragging);
        if (dragging)
            transform.SetAsLastSibling();
        else
            ResetPose();
    }

    private void Grab()
    {
        _swing.Grab();
        _settled = false;
        ResetPose();
    }

    private void Move(Vector2 position)
    {
        Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransform parent = (RectTransform)transform.parent;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, position, camera, out Vector2 local))
            return;

        _localPosition = local;
        transform.localPosition = new Vector3(local.x, local.y, 0f);
        _settled = false;
    }

    private void Animate()
    {
        if (!_dragging || _settled)
            return;

        _swing.Tick(_localPosition, Time.unscaledDeltaTime);
        transform.localRotation = Quaternion.Euler(0f, 0f, _swing.Angle);
        transform.localScale = Vector3.one * _swing.Scale;
        _settled = _swing.IsResting;
    }

    private void ResetPose()
    {
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    protected override void OnUnbound()
    {
        _icon.sprite = null;
        _amountText.text = string.Empty;
        _dragging = false;
        ResetPose();
        gameObject.SetActive(false);
    }
}

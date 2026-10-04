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

    protected override void BindCore(InventoryDragDropViewModel viewModel, CompositeDisposable bindings)
    {
        _canvas = GetComponentInParent<Canvas>(true).rootCanvas;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
        bindings.Add(viewModel.Icon.Subscribe(icon => _icon.sprite = icon));
        bindings.Add(viewModel.Position.Subscribe(Move));
        
        bindings.Add(viewModel.State.Subscribe(state =>
        {
            gameObject.SetActive(!state.IsEmpty);
            _amountText.text = state.Amount <= 1 ? string.Empty : state.Amount.ToString();
            if (!state.IsEmpty)
                transform.SetAsLastSibling();
        }));
    }

    private void Move(Vector2 position)
    {
        Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        
        RectTransform parent = (RectTransform)transform.parent;
        
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, position, camera, out Vector2 local))
            transform.localPosition = new Vector3(local.x, local.y, 0f);
    }

    protected override void OnUnbound()
    {
        _icon.sprite = null;
        _amountText.text = string.Empty;
        gameObject.SetActive(false);
    }
}

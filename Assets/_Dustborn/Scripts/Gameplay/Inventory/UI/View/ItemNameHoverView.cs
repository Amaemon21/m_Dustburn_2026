using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ItemNameHoverView : View<ItemNameHoverViewModel>
{
    private const float MAX_WIDTH = 360f;
    private const float MIN_WIDTH = 108f;
    private const float HORIZONTAL_PADDING = 30f;
    private const float VERTICAL_PADDING = 20f;
    private const float VIEWPORT_MARGIN = 8f;

    [SerializeField] private TMP_Text _itemName;
    [SerializeField] private Vector2 _offset = new(22f, 26f);

    private Canvas _canvas;
    private RectTransform _panel;
    private RectTransform _parent;
    private Vector2 _viewportSize;
    private bool _layoutDirty;

    public IDisposable BindFor(ItemNameHoverViewModel model)
    {
        Bind(model);
        return Disposable.Create(() =>
        {
            if (ReferenceEquals(ViewModel, model))
                Unbind();
        });
    }

    protected override void BindCore(ItemNameHoverViewModel viewModel, CompositeDisposable bindings)
    {
        _canvas = GetComponentInParent<Canvas>(true).rootCanvas;
        _panel = (RectTransform)transform;
        _parent = (RectTransform)_panel.parent;
        _itemName.overflowMode = TextOverflowModes.Ellipsis;

        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        bindings.Add(viewModel.Name.Subscribe(Show));
        bindings.Add(viewModel.Details.Subscribe(_ => _layoutDirty = true));
    }

    private void LateUpdate()
    {
        if (ViewModel == null || string.IsNullOrEmpty(ViewModel.Name.CurrentValue))
            return;

        Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        Rect viewport = _canvas.pixelRect;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, viewport.min, camera, out Vector2 min) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, viewport.max, camera, out Vector2 max))
            return;

        Vector2 margin = Vector2.Min(Vector2.one * VIEWPORT_MARGIN, (max - min) * 0.25f);
        Rect bounds = Rect.MinMaxRect(min.x + margin.x, min.y + margin.y, max.x - margin.x, max.y - margin.y);

        if (bounds.width <= 0f || bounds.height <= 0f)
            return;

        if (_layoutDirty || _viewportSize != bounds.size)
            Resize(bounds.size);

        Vector2 pointer = _parent.InverseTransformPoint(ViewModel.Anchor.CurrentValue);
        Mouse mouse = Mouse.current;

        if (mouse != null && !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, mouse.position.ReadValue(), camera, out pointer))
            return;

        _panel.localPosition = PlaceBelowPointer(pointer, _panel.rect.size, bounds, _offset);
    }

    internal static Vector2 PlaceBelowPointer(Vector2 pointer, Vector2 size, Rect bounds, Vector2 offset)
    {
        float maxX = Mathf.Max(bounds.xMin, bounds.xMax - size.x);
        float maxY = Mathf.Max(bounds.yMin, bounds.yMax - size.y);
        float x = pointer.x + offset.x;
        float y = pointer.y - offset.y - size.y;

        if (x > maxX)
            x = pointer.x - offset.x - size.x;

        if (y < bounds.yMin)
            y = pointer.y + offset.y;

        return new Vector2(Mathf.Clamp(x, bounds.xMin, maxX), Mathf.Clamp(y, bounds.yMin, maxY));
    }

    private void Resize(Vector2 available)
    {
        _layoutDirty = false;
        _viewportSize = available;
        string name = ViewModel.Name.CurrentValue;
        string details = ViewModel.Details.CurrentValue;
        _itemName.text = string.IsNullOrEmpty(details) ? name : $"{name}\n<size=72%><color=#C5B49D>{details}</color></size>";
        float maxWidth = Mathf.Min(MAX_WIDTH, available.x);
        Vector2 preferred = _itemName.GetPreferredValues(_itemName.text, Mathf.Max(1f, maxWidth - HORIZONTAL_PADDING), Mathf.Infinity);
        float width = Mathf.Min(maxWidth, Mathf.Max(MIN_WIDTH, Mathf.Ceil(preferred.x) + HORIZONTAL_PADDING));
        float height = Mathf.Min(available.y, Mathf.Ceil(preferred.y) + VERTICAL_PADDING);
        _panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private void Show(string itemName)
    {
        bool visible = !string.IsNullOrEmpty(itemName);
        _layoutDirty = true;
        gameObject.SetActive(visible);

        if (visible)
            transform.SetAsLastSibling();
    }

    protected override void OnUnbound()
    {
        _itemName.text = string.Empty;
        gameObject.SetActive(false);
    }
}

using R3;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MapPointerArea : MonoBehaviour, IScrollHandler, IDragHandler
{
    private readonly Subject<(float Scroll, Vector2 Pivot)> _scrolled = new();
    private readonly Subject<Vector2> _dragged = new();
    private RectTransform _rect;

    public Observable<(float Scroll, Vector2 Pivot)> Scrolled => _scrolled;
    public Observable<Vector2> Dragged => _dragged;

    private RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

    public void OnScroll(PointerEventData eventData)
    {
        _scrolled.OnNext((eventData.scrollDelta.y, Normalized(eventData.position, eventData.enterEventCamera)));
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 size = Rect.rect.size * Rect.lossyScale;
        if (size.x <= 0f || size.y <= 0f)
            return;

        _dragged.OnNext(new Vector2(eventData.delta.x / size.x, eventData.delta.y / size.y));
    }

    private Vector2 Normalized(Vector2 screen, Camera eventCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, eventCamera, out Vector2 local))
            return new Vector2(0.5f, 0.5f);

        Rect rect = Rect.rect;
        return new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
    }

    private void OnDestroy()
    {
        _scrolled.Dispose();
        _dragged.Dispose();
    }
}

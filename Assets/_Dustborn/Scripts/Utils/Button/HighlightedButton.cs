using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class HighlightedButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float _targetScale = 1.1f;
    [SerializeField] private float _duration = 0.5f;

    private RectTransform _rectTransform;
    private Button _button;
    private Vector3 _defaultScale;
    private Tween _scaleTween;
    private bool _pointerInside;
    private bool _highlighted;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _button = GetComponent<Button>();
        _defaultScale = _rectTransform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData) => _pointerInside = true;

    public void OnPointerExit(PointerEventData eventData) => _pointerInside = false;

    private void LateUpdate()
    {
        bool highlighted = _pointerInside && _button.IsInteractable();
        if (highlighted == _highlighted)
            return;

        _highlighted = highlighted;
        Vector3 scale = highlighted ? _defaultScale * _targetScale : _defaultScale;
        scale.z = _defaultScale.z;
        _scaleTween?.Kill();
        _scaleTween = _rectTransform.DOScale(scale, _duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .OnKill(() => _scaleTween = null);
    }

    private void OnDisable()
    {
        _scaleTween?.Kill();
        _pointerInside = false;
        _highlighted = false;
        _rectTransform.localScale = _defaultScale;
    }
}

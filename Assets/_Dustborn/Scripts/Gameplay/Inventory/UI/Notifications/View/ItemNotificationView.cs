using DG.Tweening;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ItemNotificationView : View<ItemNotificationViewModel>
{
    [SerializeField, Required] private RectTransform _animationRoot;
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private LayoutElement _layout;

    [Space(10)]
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _text;
    [SerializeField] private Sprite _experienceIcon;

    [Space(10)]
    [SerializeField] private Vector2 _slideOffset = new(80f, 0f);
    [SerializeField, Min(0f)] private float _expandDuration = 0.12f;
    [SerializeField, Min(0f)] private float _appearDuration = 0.25f;
    [SerializeField, Min(0f)] private float _disappearDuration = 0.3f;
    [SerializeField, Min(0f)] private float _collapseDuration = 0.15f;
    [SerializeField, Min(0f)] private float _pulse = 0.1f;
    [SerializeField, Min(0.01f)] private float _pulseDuration = 0.2f;

    private Sequence _sequence;
    private Tween _pulseTween;
    private float _height;
    private bool _shown;

    protected override void BindCore(ItemNotificationViewModel viewModel, CompositeDisposable bindings)
    {
        _shown = false;
        _height = _layout != null ? _layout.preferredHeight : 0f;

        if (_group != null)
        {
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        ShowIcon(viewModel);
        Appear();

        bindings.Add(viewModel.Text.Subscribe(ShowText));
        bindings.Add(viewModel.IsExpired.Where(expired => expired).Subscribe(_ => Disappear()));
    }

    private void ShowIcon(ItemNotificationViewModel viewModel)
    {
        if (_icon == null)
            return;

        Sprite icon = viewModel.Kind == NotificationKind.Experience ? _experienceIcon : viewModel.Icon;
        _icon.sprite = icon;
        _icon.enabled = icon != null;
    }

    private void ShowText(string text)
    {
        if (_text != null)
            _text.text = text;

        if (_shown && _pulse > 0f)
            Pulse();

        _shown = true;
    }

    private void Appear()
    {
        _sequence?.Kill();
        _animationRoot.anchoredPosition = _slideOffset;
        _animationRoot.localScale = Vector3.one;
        SetAlpha(0f);

        bool expands = _layout != null && _expandDuration > 0f;
        if (_layout != null)
            _layout.preferredHeight = expands ? 0f : _height;

        _sequence = NewSequence();
        if (expands)
            _sequence.Append(Height(_height, _expandDuration));

        _sequence.Append(_animationRoot.DOAnchorPos(Vector2.zero, _appearDuration).SetEase(Ease.OutCubic));
        if (_group != null)
            _sequence.Join(_group.DOFade(1f, _appearDuration).SetEase(Ease.Linear));
    }

    private void Disappear()
    {
        _sequence?.Kill();
        _sequence = NewSequence();
        _sequence.Append(_animationRoot.DOAnchorPos(_slideOffset, _disappearDuration).SetEase(Ease.InCubic));
        if (_group != null)
            _sequence.Join(_group.DOFade(0f, _disappearDuration).SetEase(Ease.Linear));
        if (_layout != null && _collapseDuration > 0f)
            _sequence.Append(Height(0f, _collapseDuration));

        _sequence.OnComplete(() => Destroy(gameObject));
    }

    private void Pulse()
    {
        _pulseTween?.Kill(true);
        _pulseTween = _animationRoot
            .DOPunchScale(Vector3.one * _pulse, _pulseDuration)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnKill(() => _pulseTween = null);
    }

    private Sequence NewSequence()
    {
        Sequence sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        sequence.OnKill(() =>
        {
            if (_sequence == sequence)
                _sequence = null;
        });
        return sequence;
    }

    private Tween Height(float height, float duration)
        => DOTween.To(() => _layout.preferredHeight, value => _layout.preferredHeight = value, height, duration).SetEase(Ease.OutQuad);

    private void SetAlpha(float alpha)
    {
        if (_group != null)
            _group.alpha = alpha;
    }
}

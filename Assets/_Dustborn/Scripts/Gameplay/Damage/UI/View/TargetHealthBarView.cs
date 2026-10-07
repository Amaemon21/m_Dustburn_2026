using DG.Tweening;
using NaughtyAttributes;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public sealed class TargetHealthBarView : View<TargetHealthViewModel>
{
    [SerializeField] private DamageTargetKind _kind = DamageTargetKind.Block;

    [Space(10)]
    [SerializeField, Required] private CanvasGroup _group;
    [SerializeField, Required] private Image _fill;
    [SerializeField, Required] private Image _trail;
    [SerializeField] private TMP_Text _value;
    [SerializeField] private string _valueFormat = "{0}/{1}";

    [Space(10)]
    [SerializeField, Min(0f)] private float _trailDelay = 0.3f;
    [SerializeField, Min(0.01f)] private float _trailDuration = 0.35f;
    [SerializeField, Min(0f)] private float _fadeDuration = 0.15f;

    private Tween _trailTween;
    private Tween _fadeTween;
    private int _target = -1;
    private bool _shown;

    [Inject]
    public void Construct(TargetHealthService bars) => Bind(bars.For(_kind));

    protected override void BindCore(TargetHealthViewModel viewModel, CompositeDisposable bindings)
    {
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _target = -1;
        _shown = false;

        bindings.Add(viewModel.Frame.Subscribe(Show));
        bindings.Add(viewModel.Visible.Subscribe(Fade));
    }

    private void Show(HealthBarFrame frame)
    {
        _fill.fillAmount = frame.Fill;
        if (_value != null)
            _value.text = string.Format(_valueFormat, frame.Health, frame.MaxHealth);

        if (frame.Target != _target || frame.Fill >= _trail.fillAmount)
        {
            _target = frame.Target;
            _trailTween?.Kill();
            _trail.fillAmount = frame.Fill;
            return;
        }

        _trailTween?.Kill();
        _trailTween = _trail
            .DOFillAmount(frame.Fill, _trailDuration)
            .SetDelay(_trailDelay)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnKill(() => _trailTween = null);
    }

    private void Fade(bool visible)
    {
        float alpha = visible ? 1f : 0f;
        _fadeTween?.Kill();

        if (!_shown || _fadeDuration <= 0f)
        {
            _shown = true;
            _group.alpha = alpha;
            return;
        }

        _fadeTween = _group
            .DOFade(alpha, _fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .OnKill(() => _fadeTween = null);
    }

    protected override void OnUnbound()
    {
        _trailTween?.Kill();
        _fadeTween?.Kill();
        if (_group != null)
            _group.alpha = 0f;
    }
}

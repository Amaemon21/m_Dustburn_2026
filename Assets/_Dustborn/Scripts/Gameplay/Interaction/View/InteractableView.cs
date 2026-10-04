using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using VContainer;

public sealed class InteractableView : View<InteractedViewModel>
{
    [SerializeField] private TMP_Text _contextActionText;
    [SerializeField] private TMP_Text _bindingText;
    [SerializeField] private CanvasGroup _contextActionCanvasGroup;
    [SerializeField, Min(0f)] private float _fadeDuration = 0.25f;
    private Tween _fadeTween;

    [Inject]
    public void Construct(InteractedViewModel viewModel) => Bind(viewModel);

    protected override void BindCore(InteractedViewModel viewModel, CompositeDisposable bindings)
    {
        bindings.Add(viewModel.ContextActionText.Subscribe(text => ShowText(_contextActionText, text)));
        bindings.Add(viewModel.BindingText.Subscribe(text => ShowText(_bindingText, text)));
        bindings.Add(viewModel.ContextActionAlpha.Subscribe(Fade));
    }

    private static void ShowText(TMP_Text target, string text)
    {
        if (!string.IsNullOrEmpty(text))
            target.text = text;
    }

    private void Fade(float alpha)
    {
        _fadeTween?.Kill();

        if (_fadeDuration <= 0f)
        {
            _contextActionCanvasGroup.alpha = alpha;
            ClearTextIfHidden(alpha);
            return;
        }

        _fadeTween = _contextActionCanvasGroup
            .DOFade(alpha, _fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .OnComplete(() => ClearTextIfHidden(alpha))
            .OnKill(() => _fadeTween = null);
    }

    private void ClearTextIfHidden(float alpha)
    {
        if (alpha > 0f)
            return;

        _contextActionText.text = string.Empty;
        _bindingText.text = string.Empty;
    }

    protected override void OnUnbound()
    {
        _fadeTween?.Kill();
        _contextActionCanvasGroup.alpha = 0f;
        ClearTextIfHidden(0f);
    }
}

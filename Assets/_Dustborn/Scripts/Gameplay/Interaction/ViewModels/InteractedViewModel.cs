using R3;

public sealed class InteractedViewModel : ViewModel
{
    private readonly ReactiveProperty<string> _contextActionText = new(string.Empty);
    private readonly ReactiveProperty<string> _bindingText = new(string.Empty);
    private readonly ReactiveProperty<float> _contextActionAlpha = new(0f);

    public ReadOnlyReactiveProperty<string> ContextActionText => _contextActionText;
    public ReadOnlyReactiveProperty<string> BindingText => _bindingText;
    public ReadOnlyReactiveProperty<float> ContextActionAlpha => _contextActionAlpha;

    public void Show(string text, string binding)
    {
        _contextActionText.Value = text;
        _bindingText.Value = binding;
        _contextActionAlpha.Value = 1f;
    }

    public void Hide()
    {
        _contextActionAlpha.Value = 0f;
        _contextActionText.Value = string.Empty;
        _bindingText.Value = string.Empty;
    }

    protected override void OnDisposed()
    {
        _contextActionText.Dispose();
        _bindingText.Dispose();
        _contextActionAlpha.Dispose();
    }
}

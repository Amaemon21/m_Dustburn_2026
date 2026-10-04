using R3;

public abstract class WindowViewModel : ViewModel
{
    private readonly ReactiveProperty<bool> _isOpen = new(false);
    public ReadOnlyReactiveProperty<bool> IsOpen => _isOpen;
    public ReactiveCommand<Unit> Close { get; } = new();

    protected override void OnDisposed()
    {
        _isOpen.Dispose();
        Close.Dispose();
        base.OnDisposed();
    }

    internal void SetOpen(bool value) => _isOpen.Value = value;
}

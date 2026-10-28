using System;
using R3;

public sealed class ConfirmDialogViewModel : ViewModel
{
    private readonly ReactiveProperty<bool> _isOpen = new();
    private readonly ReactiveProperty<string> _text = new(string.Empty);
    private Action _confirmed;

    public ReadOnlyReactiveProperty<bool> IsOpen => _isOpen;
    public ReadOnlyReactiveProperty<string> Text => _text;
    public ReactiveCommand<Unit> Confirm { get; } = new();
    public ReactiveCommand<Unit> Cancel { get; } = new();

    public ConfirmDialogViewModel()
    {
        Disposables.Add(Confirm.Executed.Subscribe(_ => Answer()));
        Disposables.Add(Cancel.Executed.Subscribe(_ => Hide()));
    }

    public void Ask(string text, Action confirmed)
    {
        _confirmed = confirmed;
        _text.Value = text;
        _isOpen.Value = true;
    }

    public void Hide()
    {
        _confirmed = null;
        _isOpen.Value = false;
    }

    private void Answer()
    {
        Action confirmed = _confirmed;
        Hide();
        confirmed?.Invoke();
    }

    protected override void OnDisposed()
    {
        Confirm.Dispose();
        Cancel.Dispose();
        _isOpen.Dispose();
        _text.Dispose();
    }
}

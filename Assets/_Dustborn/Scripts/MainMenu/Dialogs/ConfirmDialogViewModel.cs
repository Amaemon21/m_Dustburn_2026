using System;
using R3;

public sealed class ConfirmDialogViewModel : ViewModel
{
    private readonly ReactiveProperty<bool> _isOpen = new();
    private readonly ReactiveProperty<string> _text = new(string.Empty);
    private Action _confirmed;
    private readonly ReactiveProperty<string> _confirmLabel = new("\u0423\u0434\u0430\u043b\u0438\u0442\u044c");
    private readonly ReactiveProperty<bool> _canCancel = new(true);
    public ReadOnlyReactiveProperty<string> ConfirmLabel => _confirmLabel;
    public ReadOnlyReactiveProperty<bool> CanCancel => _canCancel;

    public void ShowMessage(string text)
    {
        Ask(text, null);
        _confirmLabel.Value = "\u041f\u043e\u043d\u044f\u0442\u043d\u043e";
        _canCancel.Value = false;
    }

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
        _confirmLabel.Value = "\u0423\u0434\u0430\u043b\u0438\u0442\u044c";
        _canCancel.Value = true;
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
        _confirmLabel.Dispose();
        _canCancel.Dispose();
    }
}

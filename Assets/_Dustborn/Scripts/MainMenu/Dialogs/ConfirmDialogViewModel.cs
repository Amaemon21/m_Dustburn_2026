using R3;

public sealed class ConfirmDialogViewModel : ViewModel
{
    private readonly ReactiveProperty<bool> _isOpen = new();
    private readonly ReactiveProperty<string> _text = new(string.Empty);
    private readonly ReactiveProperty<string> _confirmLabel = new(LocalizationService.Text("dialog.delete"));
    private readonly ReactiveProperty<bool> _canCancel = new(true);
    private Subject<Unit> _confirmed;

    public ReadOnlyReactiveProperty<bool> IsOpen => _isOpen;
    public ReadOnlyReactiveProperty<string> Text => _text;
    public ReadOnlyReactiveProperty<string> ConfirmLabel => _confirmLabel;
    public ReadOnlyReactiveProperty<bool> CanCancel => _canCancel;
    public ReactiveCommand<Unit> Confirm { get; } = new();
    public ReactiveCommand<Unit> Cancel { get; } = new();

    public ConfirmDialogViewModel()
    {
        Disposables.Add(Confirm.Executed.Subscribe(_ => Answer()));
        Disposables.Add(Cancel.Executed.Subscribe(_ => Hide()));
    }

    public Observable<Unit> Ask(string text)
    {
        if (IsDisposed)
            return Observable.Empty<Unit>();

        using Subject<Unit> previous = TakeConfirmation();
        Subject<Unit> confirmed = new();
        _confirmed = confirmed;
        Open(text, "dialog.delete", canCancel: true);
        return Observable.Defer<Unit>(() => confirmed.IsDisposed ? Observable.Empty<Unit>() : confirmed);
    }

    public void ShowMessage(string text)
    {
        if (IsDisposed)
            return;

        using Subject<Unit> previous = TakeConfirmation();
        Open(text, "dialog.acknowledge", canCancel: false);
    }

    public void Hide()
    {
        if (!IsDisposed)
            Close()?.Dispose();
    }

    private void Open(string text, string confirmLabelKey, bool canCancel)
    {
        _text.Value = text;
        _confirmLabel.Value = LocalizationService.Text(confirmLabelKey);
        _canCancel.Value = canCancel;
        _isOpen.Value = true;
    }

    private Subject<Unit> Close()
    {
        Subject<Unit> confirmed = TakeConfirmation();
        _isOpen.Value = false;
        return confirmed;
    }

    private Subject<Unit> TakeConfirmation()
    {
        Subject<Unit> confirmed = _confirmed;
        _confirmed = null;
        return confirmed;
    }

    private void Answer()
    {
        using Subject<Unit> confirmed = Close();
        confirmed?.OnNext(Unit.Default);
    }

    protected override void OnDisposed()
    {
        Close()?.Dispose();
        Confirm.Dispose();
        Cancel.Dispose();
        _isOpen.Dispose();
        _text.Dispose();
        _confirmLabel.Dispose();
        _canCancel.Dispose();
    }
}

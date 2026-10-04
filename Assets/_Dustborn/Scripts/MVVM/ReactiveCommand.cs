using System;
using R3;

public sealed class ReactiveCommand<T> : IDisposable
{
    private readonly Subject<T> _executed = new();
    private readonly ReactiveProperty<bool> _canExecute = new(true);
    private bool _disposed;

    public Observable<T> Executed => _executed;
    public ReadOnlyReactiveProperty<bool> CanExecute => _canExecute;

    public void SetCanExecute(bool value) => _canExecute.Value = value;

    public void Execute(T value)
    {
        if (!_disposed && _canExecute.Value)
            _executed.OnNext(value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _executed.Dispose();
        _canExecute.Dispose();
    }
}

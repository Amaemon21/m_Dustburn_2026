using System;
using R3;

public abstract class ViewModel : IDisposable
{
    protected CompositeDisposable Disposables { get; } = new();
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Disposables.Dispose();
        OnDisposed();
    }

    protected virtual void OnDisposed() { }
}

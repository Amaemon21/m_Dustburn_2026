using R3;
using UnityEngine;

public abstract class View<T> : MonoBehaviour where T : ViewModel
{
    private CompositeDisposable _bindings;
    protected T ViewModel { get; private set; }

    public void Bind(T viewModel)
    {
        Unbind();
        ViewModel = viewModel;
        _bindings = new CompositeDisposable();
        try
        {
            BindCore(viewModel, _bindings);
        }
        catch
        {
            Unbind();
            throw;
        }
    }

    public void Unbind()
    {
        _bindings?.Dispose();
        _bindings = null;
        ViewModel = null;
        OnUnbound();
    }

    protected abstract void BindCore(T viewModel, CompositeDisposable bindings);
    protected virtual void OnUnbound() { }
    protected virtual void OnDestroy() => Unbind();
}

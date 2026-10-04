using System;
using R3;
using UnityEngine;

public abstract class WindowView : MonoBehaviour, IWindowPresenter
{
    private CompositeDisposable _bindings;
    public WindowViewModel BoundViewModel { get; private set; }
    public abstract Type ViewModelType { get; }
    public abstract string WindowId { get; }

    public void Bind(WindowViewModel viewModel)
    {
        Unbind();
        BoundViewModel = viewModel;
        _bindings = new CompositeDisposable();
        
        try
        {
            _bindings.Add(viewModel.IsOpen.Subscribe(gameObject.SetActive));
            BindCore(viewModel, _bindings);
        }
        catch
        {
            Unbind();
            throw;
        }
    }

    public void Focus() => transform.SetAsLastSibling();

    public void Unbind()
    {
        _bindings?.Dispose();
        _bindings = null;
        BoundViewModel = null;
        OnUnbound();
        gameObject.SetActive(false);
    }

    protected abstract void BindCore(WindowViewModel viewModel, CompositeDisposable bindings);
    protected virtual void OnUnbound() { }
    protected virtual void OnDestroy() => Unbind();
}

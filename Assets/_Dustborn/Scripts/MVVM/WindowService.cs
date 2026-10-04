using System;
using System.Collections.Generic;
using R3;

public sealed class WindowService : IDisposable
{
    private readonly Dictionary<string, WindowViewModel> _windows = new();
    private readonly Dictionary<string, IWindowPresenter> _presenters = new();
    private readonly Dictionary<string, IDisposable> _closeBindings = new();
    private readonly List<string> _order = new();
    private readonly ReactiveProperty<bool> _hasOpenWindows = new(false);
    private readonly Subject<WindowViewModel> _opened = new();
    private readonly Subject<WindowViewModel> _closed = new();

    public Observable<WindowViewModel> Opened => _opened;
    public Observable<WindowViewModel> Closed => _closed;
    public ReadOnlyReactiveProperty<bool> HasOpenWindows => _hasOpenWindows;
    public IReadOnlyCollection<WindowViewModel> OpenWindows => _windows.Values;
    public bool TryGet(string id, out WindowViewModel viewModel) => _windows.TryGetValue(id, out viewModel);

    public void Register(string id, IWindowPresenter presenter)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Window id is required", nameof(id));
        _presenters.Add(id, presenter ?? throw new ArgumentNullException(nameof(presenter)));
    }

    public void Unregister(string id, IWindowPresenter presenter)
    {
        if (!_presenters.TryGetValue(id, out IWindowPresenter current) || !ReferenceEquals(current, presenter))
            return;
        Close(id);
        _presenters.Remove(id);
    }

    public void Open(string id, WindowViewModel viewModel)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Window id is required", nameof(id));
        if (viewModel == null || viewModel.IsDisposed)
            throw new ArgumentException("A live window view model is required", nameof(viewModel));
        if (_windows.TryGetValue(id, out WindowViewModel current) && ReferenceEquals(current, viewModel))
        {
            Focus(id);
            return;
        }
        if (_windows.ContainsValue(viewModel))
            throw new InvalidOperationException("The view model is already open under another id");
        if (!_presenters.TryGetValue(id, out IWindowPresenter presenter))
            throw new InvalidOperationException($"No view registered for window '{id}'");
        if (!presenter.ViewModelType.IsInstanceOfType(viewModel))
            throw new ArgumentException($"Window '{id}' does not support {viewModel.GetType().Name}", nameof(viewModel));

        try
        {
            presenter.Bind(viewModel);
            presenter.Focus();
        }
        catch
        {
            presenter.Unbind();
            if (current != null)
            {
                presenter.Bind(current);
                presenter.Focus();
            }
            throw;
        }

        _windows[id] = viewModel;
        if (current != null)
            _closeBindings[id].Dispose();
        _closeBindings[id] = viewModel.Close.Executed.Subscribe(_ => Close(id));
        _order.Remove(id);
        _order.Add(id);
        viewModel.SetOpen(true);
        _hasOpenWindows.Value = _windows.Count > 0;
        if (current != null)
        {
            current.SetOpen(false);
            _closed.OnNext(current);
            current.Dispose();
        }
        CloseAllExcept(id);
        if (_windows.TryGetValue(id, out WindowViewModel opened) && ReferenceEquals(opened, viewModel))
            _opened.OnNext(viewModel);
    }

    private void CloseAllExcept(string id)
    {
        for (int i = _order.Count - 1; i >= 0; i--)
        {
            if (i < _order.Count && _order[i] != id)
                Close(_order[i]);
        }
    }

    public void Close(string id)
    {
        if (!_windows.Remove(id, out WindowViewModel viewModel))
            return;
        _closeBindings[id].Dispose();
        _closeBindings.Remove(id);
        _order.Remove(id);
        _presenters[id].Unbind();
        viewModel.SetOpen(false);
        _hasOpenWindows.Value = _windows.Count > 0;
        _closed.OnNext(viewModel);
        viewModel.Dispose();
    }

    public void Focus(string id)
    {
        if (!_windows.ContainsKey(id))
            return;
        _presenters[id].Focus();
        _order.Remove(id);
        _order.Add(id);
    }

    public bool CloseTop()
    {
        if (_order.Count == 0)
            return false;
        Close(_order[_order.Count - 1]);
        return true;
    }

    public void CloseAll()
    {
        while (CloseTop()) { }
    }

    public void Dispose()
    {
        CloseAll();
        _opened.Dispose();
        _closed.Dispose();
        _hasOpenWindows.Dispose();
        _presenters.Clear();
    }
}

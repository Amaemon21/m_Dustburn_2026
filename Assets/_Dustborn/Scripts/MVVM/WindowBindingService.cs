using System;
using System.Collections.Generic;

public sealed class WindowBindingService : IDisposable
{
    private readonly WindowService _windows;
    private readonly List<WindowView> _views = new();

    public WindowBindingService(WindowService windows, IReadOnlyList<WindowView> views)
    {
        _windows = windows;
        try
        {
            for (int i = 0; i < views.Count; i++)
            {
                WindowView view = views[i];
                if (view == null)
                    throw new InvalidOperationException($"WindowBindingService: window slot {i} is empty. Remove it from ScreenView or assign a WindowView");

                view.Unbind();
                _windows.Register(view.WindowId, view);
                _views.Add(view);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (WindowView view in _views)
            _windows.Unregister(view.WindowId, view);
        _views.Clear();
    }
}

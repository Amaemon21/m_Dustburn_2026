using System.Collections.Generic;

public sealed class WindowBindingServiceFactory
{
    private readonly WindowService _windows;
    public WindowBindingServiceFactory(WindowService windows) => _windows = windows;
    public WindowBindingService Create(IReadOnlyList<WindowView> views)
        => new WindowBindingService(_windows, views);
}

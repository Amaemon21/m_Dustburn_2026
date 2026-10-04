using R3;

public sealed class ScreenService : IScreenService
{
    private readonly WindowService _windows;
    public ReadOnlyReactiveProperty<bool> HasOpenWindows => _windows.HasOpenWindows;
    public ScreenService(WindowService windows) => _windows = windows;
    public bool HasAnyWindowOpen() => HasOpenWindows.CurrentValue;
    public void Open(string id, WindowViewModel viewModel) => _windows.Open(id, viewModel);
    public void Close(string id) => _windows.Close(id);
    public bool CloseTop() => _windows.CloseTop();
    public void CloseAll() => _windows.CloseAll();
}

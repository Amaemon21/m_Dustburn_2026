using R3;

public sealed class PauseScreenService
{
    public const string WINDOW_ID = "pause";

    private readonly WindowService _windows;
    private readonly ISaveService _saves;
    private readonly IGameplayExit _exit;

    public bool IsOpen => _windows.TryGet(WINDOW_ID, out _);

    public PauseScreenService(WindowService windows, ISaveService saves, IGameplayExit exit)
    {
        _windows = windows;
        _saves = saves;
        _exit = exit;
    }

    public void HandleEscape()
    {
        if (_windows.TryGet(WINDOW_ID, out WindowViewModel current))
        {
            current.Close.Execute(Unit.Default);
            return;
        }

        if (_windows.CloseTop() || !_windows.CanOpen(WINDOW_ID))
            return;

        PauseScreenViewModel model = new(_saves, _exit);

        try
        {
            _windows.Open(WINDOW_ID, model);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }
}

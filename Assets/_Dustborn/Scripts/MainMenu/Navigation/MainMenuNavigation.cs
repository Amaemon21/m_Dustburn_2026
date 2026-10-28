using System;
using R3;

public sealed class MainMenuNavigation : IDisposable
{
    private readonly ReactiveProperty<MainMenuScreen> _current = new(MainMenuScreen.Main);

    public ReadOnlyReactiveProperty<MainMenuScreen> Current => _current;

    public void Show(MainMenuScreen screen) => _current.Value = screen;

    public void Dispose() => _current.Dispose();
}

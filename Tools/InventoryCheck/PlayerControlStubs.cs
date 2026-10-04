using System;
using R3;

public sealed class PlayerSpawnService
{
    public bool ControlEnabled { get; private set; }
    public void SetControlEnabled(bool enabled) => ControlEnabled = enabled;
}

public sealed class PlayerInputService : IDisposable
{
    private readonly Subject<Unit> _pressed = new();
    public Observable<Unit> InteractPressed => _pressed;
    public string InteractBindingDisplayString { get; set; } = "E";
    public bool Enabled { get; private set; }
    public int EnableCount { get; private set; }
    public void Enable() { Enabled = true; EnableCount++; }
    public void Disable() => Enabled = false;
    public void Press()
    {
        if (Enabled)
            _pressed.OnNext(Unit.Default);
    }
    public void Dispose() => _pressed.Dispose();
}

namespace UnityEngine
{
    internal static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }
}

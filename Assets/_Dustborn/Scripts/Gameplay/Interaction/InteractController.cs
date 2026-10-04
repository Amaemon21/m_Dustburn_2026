using System;
using R3;
using VContainer.Unity;

public sealed class InteractController : IPostLateTickable, IGameplayActivatable, IDisposable
{
    private readonly InteractHandler _handler;
    private readonly PlayerInputService _input;
    private readonly IScreenService _screen;
    private readonly CompositeDisposable _subscriptions = new();
    private bool _running;

    public InteractController(InteractHandler handler, PlayerInputService input, IScreenService screen)
    {
        _handler = handler;
        _input = input;
        _screen = screen;
    }

    public void Activate()
    {
        if (_running)
            return;
        _running = true;
        _subscriptions.Add(_input.InteractPressed.Subscribe(_ => _handler.Interact()));
        _subscriptions.Add(_screen.HasOpenWindows.Subscribe(open =>
        {
            if (open)
                _handler.Clear();
        }));
    }

    public void PostLateTick()
    {
        if (_running)
            _handler.RefreshTarget();
    }

    public void Stop()
    {
        _running = false;
        _subscriptions.Clear();
        _handler.Clear();
    }

    public void Dispose()
    {
        Stop();
        _subscriptions.Dispose();
    }
}

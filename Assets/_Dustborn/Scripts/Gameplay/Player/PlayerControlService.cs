using System;
using R3;
using UnityEngine;

public sealed class PlayerControlService : IGameplayActivatable, IDisposable
{
    private readonly PlayerCharacterController _character;
    private readonly IScreenService _screen;
    private readonly PlayerInputService _input;
    private readonly CompositeDisposable _subscriptions = new();

    public PlayerControlService(PlayerCharacterController character, IScreenService screen, PlayerInputService input)
    {
        _character = character;
        _screen = screen;
        _input = input;
    }

    public void Activate()
    {
        _subscriptions.Clear();

        _subscriptions.Add(_screen.HasOpenWindows.Subscribe(open =>
        {
            if (open)
                _input.Disable();
            else
                _input.Enable();

            _character.SetInputEnabled(!open);

            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }));
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _input.Disable();
        _character.SetInputEnabled(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

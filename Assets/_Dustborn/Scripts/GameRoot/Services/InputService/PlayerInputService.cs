using System;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInputService : InputService
{
    private readonly Subject<Unit> _interactPressed = new();
    private readonly InputAction _interactAction;
    private readonly InputAction _move;
    private readonly InputAction _look;
    private readonly InputAction _sprint;
    private readonly InputAction _jump;
    private readonly InputAction _crouch;
    private readonly InputAction _primary;
    private readonly InputAction _secondary;

    public Observable<Unit> InteractPressed => _interactPressed;
    public string InteractBindingDisplayString => BindingDisplay.Of(_interactAction);
    public Vector2 Move => _move.ReadValue<Vector2>();
    public Vector2 Look => _look.ReadValue<Vector2>();
    public bool SprintHeld => _sprint.IsPressed();
    public bool CrouchHeld => _crouch.IsPressed();
    public bool JumpPressed => _jump.WasPressedThisFrame();
    public bool PrimaryHeld => _primary != null && _primary.IsPressed();
    public bool SecondaryHeld => _secondary != null && _secondary.IsPressed();

    public PlayerInputService(PlayerInputSettings settings)
    {
        _interactAction = Require(settings.Interact, nameof(settings.Interact)).action;
        Bindings.Add(ObservePerformed(settings.Interact).Subscribe(_ => _interactPressed.OnNext(Unit.Default)));
        _move = RegisterAction(Require(settings.Move, nameof(settings.Move)));
        _look = RegisterAction(Require(settings.Look, nameof(settings.Look)));
        _sprint = RegisterAction(Require(settings.Sprint, nameof(settings.Sprint)));
        _jump = RegisterAction(Require(settings.Jump, nameof(settings.Jump)));
        _crouch = RegisterAction(Require(settings.Crouch, nameof(settings.Crouch)));
        _primary = Optional(settings.PrimaryAction, nameof(settings.PrimaryAction));
        _secondary = Optional(settings.SecondaryAction, nameof(settings.SecondaryAction));
    }

    private static InputActionReference Require(InputActionReference reference, string name)
    {
        if (reference == null)
            throw new ArgumentException($"Assign the '{name}' action in PlayerInputSettings", name);
        return reference;
    }

    private InputAction Optional(InputActionReference reference, string name)
    {
        if (reference != null)
            return RegisterAction(reference);

        Debug.LogWarning($"{nameof(PlayerInputService)}: the '{name}' action is not assigned in PlayerInputSettings, held items ignore it");
        return null;
    }

    protected override void OnDisposed() => _interactPressed.Dispose();
}

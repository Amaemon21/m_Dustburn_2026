using UnityEngine;
using VContainer.Unity;

public sealed class PlayerCharacterController : ITickable, ILateTickable, IGameplayActivatable
{
    private readonly PlayerSpawnService _player;
    private readonly PlayerInputService _input;
    private readonly PlayerMovementModifiers _modifiers;
    private bool _running;
    private bool _inputEnabled;

    public PlayerCharacterController(PlayerSpawnService player, PlayerInputService input, PlayerMovementModifiers modifiers)
    {
        _player = player;
        _input = input;
        _modifiers = modifiers;
    }

    public void Activate() => _running = true;

    public void SetInputEnabled(bool value) => _inputEnabled = value;

    public void Tick()
    {
        PlayerCharacter character = _player.Character;
        if (!_running || character == null)
            return;

        float deltaTime = Time.deltaTime;
        PlayerMovementRequest request = ReadRequest();
        _modifiers.Modify(ref request);
        PlayerMovementResult result = character.Move(request, deltaTime);
        _modifiers.Apply(result, deltaTime);
    }

    public void LateTick()
    {
        PlayerCharacter character = _player.Character;
        if (!_running || character == null)
            return;

        character.Look(_inputEnabled ? _input.Look : Vector2.zero, Time.deltaTime);
    }

    private PlayerMovementRequest ReadRequest()
    {
        PlayerMovementRequest request = PlayerMovementRequest.Idle;
        if (!_inputEnabled)
            return request;

        request.Move = _input.Move;
        request.Sprint = _input.SprintHeld;
        request.Jump = _input.JumpPressed;
        request.Crouch = _input.CrouchHeld;
        return request;
    }
}

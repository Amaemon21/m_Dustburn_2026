using UnityEngine;
using VContainer.Unity;

public sealed class PlayerHandsController : ITickable, IGameplayActivatable
{
    private readonly PlayerSpawnService _player;
    private readonly PlayerInputService _input;
    private readonly HeldItemSelection _selection;
    private bool _running;

    public PlayerHandsController(PlayerSpawnService player, PlayerInputService input, HeldItemSelection selection)
    {
        _player = player;
        _input = input;
        _selection = selection;
    }

    public void Activate() => _running = true;

    public void Tick()
    {
        PlayerCharacter character = _player.Character;
        if (!_running || character == null)
            return;

        PlayerMovementResult movement = character.LastMovement;
        character.Hands.Hold(_selection.Held.CurrentValue);
        character.Hands.Tick(new HandsRequest
        {
            Primary = _input.PrimaryHeld,
            Secondary = _input.SecondaryHeld,
            Moving = movement.IsMoving,
            Running = movement.IsSprinting
        }, Time.deltaTime);
    }
}

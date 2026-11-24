using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

public sealed class PlayerVitalsController : ITickable, IGameplayActivatable
{
    private readonly IReadOnlyList<IPlayerVitalsSystem> _systems;
    private readonly PlayerMovementModifiers _movement;
    private readonly StaminaSystem _stamina;
    private readonly PlayerInjurySystem _injuries;
    private bool _running;

    public PlayerVitalsController(IReadOnlyList<IPlayerVitalsSystem> systems, PlayerMovementModifiers movement, StaminaSystem stamina,
        PlayerInjurySystem injuries)
    {
        _systems = systems;
        _movement = movement;
        _stamina = stamina;
        _injuries = injuries;
    }

    public void Activate()
    {
        _movement.Add(_stamina);
        _movement.Add(_injuries);
        _running = true;
    }

    public void Tick()
    {
        if (!_running)
            return;

        float deltaTime = Time.deltaTime;

        foreach (IPlayerVitalsSystem system in _systems)
            system.Tick(deltaTime);
    }
}

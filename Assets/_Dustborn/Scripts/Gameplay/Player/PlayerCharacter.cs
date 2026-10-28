using System;
using UnityEngine;

public sealed class PlayerCharacter : IDisposable
{
    private readonly PlayerMotor _motor;
    private readonly PlayerLook _look;

    public PlayerView View { get; }
    public PlayerHands Hands { get; }
    public PlayerCameraMotion CameraMotion { get; }
    public PlayerMovementResult LastMovement { get; private set; }

    public PlayerCharacter(PlayerView view, PlayerHands hands, PlayerCameraMotion cameraMotion)
    {
        View = view != null ? view : throw new ArgumentNullException(nameof(view));
        Hands = hands ?? throw new ArgumentNullException(nameof(hands));
        _motor = new PlayerMotor(view.Controller, view.Movement);
        _look = new PlayerLook(view.transform, view.CameraPivot, view.Movement);
        CameraMotion = cameraMotion ?? throw new ArgumentNullException(nameof(cameraMotion));
    }

    public PlayerMovementResult Move(PlayerMovementRequest request, float deltaTime)
    {
        LastMovement = _motor.Tick(request, deltaTime);
        return LastMovement;
    }

    public void Look(Vector2 delta, float deltaTime)
    {
        Vector2 turn = _look.Tick(delta);
        CameraMotion.Tick(LastMovement, turn, deltaTime);
    }

    public void Dispose()
    {
        Hands.Dispose();
        _motor.Dispose();
        CameraMotion.Dispose();
    }
}

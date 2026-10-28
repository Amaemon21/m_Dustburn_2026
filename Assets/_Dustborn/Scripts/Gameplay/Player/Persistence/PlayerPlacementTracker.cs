using UnityEngine;
using VContainer.Unity;

public sealed class PlayerPlacementTracker : ILateTickable, IGameplayActivatable
{
    private const float MOVE_THRESHOLD = 0.25f;
    private const float TURN_THRESHOLD = 2f;

    private readonly IPlayerProvider _player;
    private readonly PlayerPlacementRepository _repository;
    private bool _running;

    public PlayerPlacementTracker(IPlayerProvider player, PlayerPlacementRepository repository)
    {
        _player = player;
        _repository = repository;
    }

    public void Activate() => _running = true;

    public void LateTick()
    {
        Transform player = _player.Player;
        if (!_running || player == null)
            return;

        Vector3 position = player.position;
        float yaw = player.eulerAngles.y;
        PlayerPlacementData saved = _repository.Origin;

        if (saved.Saved && !Moved(saved, position, yaw))
            return;

        _repository.Write(position, yaw);
    }

    private static bool Moved(PlayerPlacementData saved, Vector3 position, float yaw)
        => !DistanceUtility.WithinRadius(saved.Position, position, MOVE_THRESHOLD)
            || Mathf.Abs(Mathf.DeltaAngle(saved.Yaw, yaw)) > TURN_THRESHOLD;
}

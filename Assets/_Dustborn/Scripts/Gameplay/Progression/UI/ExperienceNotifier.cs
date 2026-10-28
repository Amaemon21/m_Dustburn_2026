using System;
using R3;

public sealed class ExperienceNotifier : IGameplayActivatable, IDisposable
{
    private const float CARRY_EPSILON = 0.0001f;

    private readonly PlayerProgression _progression;
    private readonly ItemNotificationsViewModel _notifications;
    private IDisposable _subscription;
    private float _carry;

    public ExperienceNotifier(PlayerProgression progression, ItemNotificationsViewModel notifications)
    {
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public void Activate()
    {
        _subscription?.Dispose();
        _subscription = _progression.Gained.Subscribe(OnGained);
    }

    private void OnGained(float experience)
    {
        _carry += experience;
        int whole = (int)Math.Floor(_carry + CARRY_EPSILON);
        if (whole <= 0)
            return;

        _carry = Math.Max(0f, _carry - whole);
        _notifications.NotifyExperience(whole);
    }

    public void Dispose() => _subscription?.Dispose();
}

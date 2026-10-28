using System;
using R3;

public sealed class HarvestExperienceSystem : IGameplayActivatable, IDisposable
{
    private readonly DamageFeed _feed;
    private readonly PlayerProgression _progression;
    private readonly HarvestExperienceSettings _settings;
    private IDisposable _subscription;

    public HarvestExperienceSystem(DamageFeed feed, PlayerProgression progression, HarvestExperienceSettings settings)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Activate()
    {
        _subscription?.Dispose();
        _subscription = _feed.Reports.Subscribe(OnDamage);
    }

    private void OnDamage(DamageReport report)
    {
        if (report.Target == null || report.Target.Kind != DamageTargetKind.Block)
            return;

        _progression.AddExperience(_settings.For(report.Result, report.Target.MaxHealth));
    }

    public void Dispose() => _subscription?.Dispose();
}

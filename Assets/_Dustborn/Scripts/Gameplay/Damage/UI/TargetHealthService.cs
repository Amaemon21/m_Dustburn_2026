using System;
using System.Collections.Generic;
using R3;

public sealed class TargetHealthService : IDisposable
{
    private static readonly TimeSpan HIDE_DELAY = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan DESTROYED_HIDE_DELAY = TimeSpan.FromSeconds(0.8);

    private readonly Dictionary<DamageTargetKind, TargetHealthViewModel> _bars = new();
    private readonly IDisposable _subscription;

    public TargetHealthService(DamageFeed feed)
    {
        foreach (DamageTargetKind kind in Enum.GetValues(typeof(DamageTargetKind)))
            _bars.Add(kind, new TargetHealthViewModel(HIDE_DELAY, DESTROYED_HIDE_DELAY));

        _subscription = feed.Reports.Subscribe(Show);
    }

    public TargetHealthViewModel For(DamageTargetKind kind) => _bars[kind];

    private void Show(DamageReport report)
    {
        if (report.Target != null)
            For(report.Target.Kind).Show(report.Target, report.Result.Dealt);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        foreach (TargetHealthViewModel bar in _bars.Values)
            bar.Dispose();
        _bars.Clear();
    }
}

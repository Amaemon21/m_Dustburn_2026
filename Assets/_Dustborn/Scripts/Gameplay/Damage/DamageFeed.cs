using System;
using R3;

public sealed class DamageFeed : IDisposable
{
    private readonly Subject<DamageReport> _reports = new();

    public Observable<DamageReport> Reports => _reports;

    public void Publish(DamageReport report) => _reports.OnNext(report);

    public void Dispose() => _reports.Dispose();
}

public readonly struct DamageReport
{
    public IDamageable Target { get; }
    public DamageResult Result { get; }

    public DamageReport(IDamageable target, DamageResult result)
    {
        Target = target;
        Result = result;
    }
}

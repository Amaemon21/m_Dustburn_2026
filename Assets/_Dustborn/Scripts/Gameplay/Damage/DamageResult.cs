public readonly struct DamageResult
{
    public int Dealt { get; }
    public bool Destroyed { get; }

    public DamageResult(int dealt, bool destroyed)
    {
        Dealt = dealt;
        Destroyed = destroyed;
    }
}

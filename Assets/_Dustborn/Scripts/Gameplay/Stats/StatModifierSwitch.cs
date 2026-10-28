using System;

public sealed class StatModifierSwitch
{
    private readonly Stat _stat;
    private readonly StatModifier _modifier;

    public bool IsActive { get; private set; }

    public StatModifierSwitch(Stat stat, StatModifier modifier)
    {
        _stat = stat ?? throw new ArgumentNullException(nameof(stat));
        _modifier = modifier;
    }

    public void Set(bool active)
    {
        if (active == IsActive)
            return;

        IsActive = active;
        if (active)
            _stat.AddModifier(_modifier);
        else
            _stat.RemoveModifier(_modifier);
    }
}

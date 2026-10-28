using System;

public sealed class ExperienceCurve
{
    private readonly int _baseExperience;
    private readonly double _growth;

    public int MaxLevel { get; }

    public ExperienceCurve(int baseExperience, float growth, int maxLevel)
    {
        if (baseExperience < 1)
            throw new ArgumentOutOfRangeException(nameof(baseExperience), "A level needs at least one experience point");
        if (growth < 1f)
            throw new ArgumentOutOfRangeException(nameof(growth), "Experience growth cannot shrink the next level");
        if (maxLevel < 2)
            throw new ArgumentOutOfRangeException(nameof(maxLevel), "There must be a level to reach");

        _baseExperience = baseExperience;
        _growth = growth;
        MaxLevel = maxLevel;
    }

    public int ClampLevel(int level) => Math.Clamp(level, 1, MaxLevel);

    public int RequiredFor(int level)
    {
        if (level >= MaxLevel)
            return 0;
        return (int)Math.Round(_baseExperience * Math.Pow(_growth, ClampLevel(level) - 1));
    }
}

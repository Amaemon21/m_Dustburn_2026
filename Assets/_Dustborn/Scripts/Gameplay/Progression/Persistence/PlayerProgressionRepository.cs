using System;

public sealed class PlayerProgressionRepository
{
    private readonly ISaveService _saves;

    public PlayerProgressionData Origin { get; }

    public PlayerProgressionRepository(ISaveService saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        Origin = saves.Get<PlayerProgressionData>();
    }

    public void Write(int level, float experience)
    {
        if (Origin.Level == level && Origin.Experience.Equals(experience))
            return;

        Origin.Level = level;
        Origin.Experience = experience;
        _saves.MarkDirty<PlayerProgressionData>();
    }
}

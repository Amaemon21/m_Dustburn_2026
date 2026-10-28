using System;

public sealed class WorldSaveRepository
{
    private readonly ISaveService _saves;

    public WorldSaveData Origin { get; }

    public WorldSaveRepository(ISaveService saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        Origin = saves.Get<WorldSaveData>();
    }

    public void Claim(string fingerprint, int seed, int worldSize, string worldName = null)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("A world fingerprint is required", nameof(fingerprint));

        if (string.IsNullOrEmpty(Origin.Fingerprint))
        {
            Origin.Fingerprint = fingerprint;
            Origin.Seed = seed;
            Origin.WorldSize = worldSize;
            Origin.WorldName = worldName;
            _saves.MarkDirty<WorldSaveData>();
            return;
        }

        if (Origin.Fingerprint != fingerprint)
            throw new WorldSaveMismatchException(_saves.ActiveSlot, Origin.Fingerprint, fingerprint);
    }
}

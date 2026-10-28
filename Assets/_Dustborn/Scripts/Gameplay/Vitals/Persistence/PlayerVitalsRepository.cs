using System;

public sealed class PlayerVitalsRepository
{
    private readonly ISaveService _saves;

    public PlayerVitalsData Origin { get; }
    public bool HasState => Origin.Initialized;

    public PlayerVitalsRepository(ISaveService saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        Origin = saves.Get<PlayerVitalsData>();
    }

    public float Read(VitalType type) => type switch
    {
        VitalType.Health => Origin.Health,
        VitalType.Stamina => Origin.Stamina,
        VitalType.Food => Origin.Food,
        VitalType.Water => Origin.Water,
        VitalType.Infection => Origin.Infection,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown vital")
    };

    public void Write(VitalType type, float value)
    {
        if (Origin.Initialized && Read(type).Equals(value))
            return;

        switch (type)
        {
            case VitalType.Health: Origin.Health = value; break;
            case VitalType.Stamina: Origin.Stamina = value; break;
            case VitalType.Food: Origin.Food = value; break;
            case VitalType.Water: Origin.Water = value; break;
            case VitalType.Infection: Origin.Infection = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown vital");
        }

        _saves.MarkDirty<PlayerVitalsData>();
    }

    public void MarkInitialized()
    {
        if (Origin.Initialized)
            return;

        Origin.Initialized = true;
        _saves.MarkDirty<PlayerVitalsData>();
    }
}

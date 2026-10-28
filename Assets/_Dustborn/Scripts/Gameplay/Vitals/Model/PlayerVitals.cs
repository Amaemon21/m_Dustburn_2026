using System;
using System.Collections.Generic;
using R3;

public sealed class PlayerVitals : IDisposable
{
    private readonly Dictionary<VitalType, Vital> _vitals = new();
    private readonly CompositeDisposable _subscriptions = new();

    public Vital Health { get; }
    public Vital Stamina { get; }
    public Vital Food { get; }
    public Vital Water { get; }
    public Vital Infection { get; }

    public PlayerVitals(PlayerStatSheet stats, PlayerVitalsRepository repository)
    {
        if (stats == null)
            throw new ArgumentNullException(nameof(stats));
        if (repository == null)
            throw new ArgumentNullException(nameof(repository));

        bool restored = repository.HasState;
        Health = Create(VitalType.Health, stats.Get(PlayerStatType.MaxHealth), repository, restored, true);
        Stamina = Create(VitalType.Stamina, stats.Get(PlayerStatType.MaxStamina), repository, restored, true);
        Food = Create(VitalType.Food, stats.Get(PlayerStatType.MaxFood), repository, restored, true);
        Water = Create(VitalType.Water, stats.Get(PlayerStatType.MaxWater), repository, restored, true);
        Infection = Create(VitalType.Infection, stats.Get(PlayerStatType.MaxInfection), repository, restored, false);
        repository.MarkInitialized();
    }

    public Vital Get(VitalType type)
    {
        if (_vitals.TryGetValue(type, out Vital vital))
            return vital;
        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown vital");
    }

    public void Apply(in VitalsEffect effect)
    {
        Health.Change(effect.Health);
        Stamina.Change(effect.Stamina);
        Food.Change(effect.Food);
        Water.Change(effect.Water);
        Infection.Change(effect.Infection);
    }

    private Vital Create(VitalType type, IReadOnlyStat max, PlayerVitalsRepository repository, bool restored, bool startsFull)
    {
        float initial = restored ? repository.Read(type) : startsFull ? max.Value.CurrentValue : 0f;
        Vital vital = new(type, max, initial);
        _vitals.Add(type, vital);
        _subscriptions.Add(vital.Current.Subscribe(value => repository.Write(type, value)));
        return vital;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (Vital vital in _vitals.Values)
            vital.Dispose();
        _vitals.Clear();
    }
}

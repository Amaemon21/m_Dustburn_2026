using System;
using R3;

public sealed class PlayerEffects : IPlayerVitalsSystem, IDisposable
{
    private readonly TimedPlayerEffectData[] _timed = new TimedPlayerEffectData[(int)PlayerEffectType.Protection + 1];
    private readonly Subject<Unit> _changed = new();
    private readonly PlayerVitals _vitals;
    private readonly PlayerEffectsRepository _repository;
    private readonly StatModifierSwitch _infection;
    private readonly CompositeDisposable _subscriptions = new();

    public Observable<Unit> Changed => _changed;
    public float InfectionFraction => _vitals.Infection.Fraction;
    public float FractureSlowdown => Strength(PlayerEffectType.Fracture);
    public float DamageProtection => Strength(PlayerEffectType.Protection);

    public PlayerEffects(PlayerVitals vitals, PlayerStatSheet stats, PlayerEffectsRepository repository, PlayerEffectSettings settings)
    {
        _vitals = vitals;
        _repository = repository;
        Stat regen = stats.Get(PlayerStatType.StaminaRegen);
        _infection = new StatModifierSwitch(regen, new StatModifier(StatModifierType.Multiplier, settings.InfectionStaminaRegen, this));

        for (int i = 0; i < _timed.Length; i++)
        {
            PlayerEffectType type = (PlayerEffectType)i;
            TimedPlayerEffectData restored = repository.Read(type);

            if (IsValid(type, restored))
                _timed[i] = restored;
        }

        _subscriptions.Add(_vitals.Infection.Current.Subscribe(_ =>
        {
            _infection.Set(!_vitals.Infection.IsEmpty);
            _changed.OnNext(Unit.Default);
        }));
        _subscriptions.Add(_vitals.Infection.Max.Subscribe(_ => _changed.OnNext(Unit.Default)));
    }

    public float Remaining(PlayerEffectType type) => IsTimed(type) ? _timed[(int)type].Remaining : 0f;

    public bool ApplyHealing(float health, float duration) => TryRefresh(PlayerEffectType.Healing, health / duration, duration);
    public bool ApplyProtection(float reduction, float duration) => TryRefresh(PlayerEffectType.Protection, reduction, duration);
    public bool ApplyBleeding(float damagePerSecond, float duration) => TryRefresh(PlayerEffectType.Bleeding, damagePerSecond, duration);
    public bool ApplyFracture(float slowdown, float duration) => TryRefresh(PlayerEffectType.Fracture, slowdown, duration);

    public void ReduceBleeding(float damagePerSecond)
    {
        TimedPlayerEffectData bleeding = _timed[(int)PlayerEffectType.Bleeding];

        if (!float.IsFinite(damagePerSecond) || damagePerSecond <= 0f || bleeding.Remaining <= 0f)
            return;

        bleeding.Strength -= damagePerSecond;
        Write(PlayerEffectType.Bleeding, bleeding.Strength > 0f ? bleeding : default);
        _changed.OnNext(Unit.Default);
    }

    public void Tick(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            return;

        float bleeding = 0f;
        float healing = 0f;
        bool advanced = false;

        for (int i = 0; i < _timed.Length; i++)
        {
            TimedPlayerEffectData effect = _timed[i];

            if (effect.Remaining <= 0f)
                continue;

            PlayerEffectType type = (PlayerEffectType)i;
            float elapsed = Math.Min(deltaTime, effect.Remaining);

            if (type == PlayerEffectType.Bleeding)
                bleeding = effect.Strength * elapsed;
            else if (type == PlayerEffectType.Healing)
                healing = effect.Strength * elapsed;

            effect.Remaining -= elapsed;
            Write(type, effect.Remaining > 0f ? effect : default);
            advanced = true;
        }

        if (!advanced)
            return;

        if (!_vitals.Health.IsEmpty)
            _vitals.Health.Change(healing - bleeding);

        _changed.OnNext(Unit.Default);
    }

    private bool TryRefresh(PlayerEffectType type, float strength, float duration)
    {
        TimedPlayerEffectData next = new(duration, strength);
        TimedPlayerEffectData current = _timed[(int)type];

        if (!IsValid(type, next) || strength < current.Strength || (strength == current.Strength && duration <= current.Remaining))
            return false;

        Write(type, next);
        _changed.OnNext(Unit.Default);
        return true;
    }

    private float Strength(PlayerEffectType type) => _timed[(int)type].Strength;

    private bool IsTimed(PlayerEffectType type) => (uint)type < (uint)_timed.Length;

    private static bool IsValid(PlayerEffectType type, TimedPlayerEffectData effect)
    {
        if (!float.IsFinite(effect.Remaining) || effect.Remaining <= 0f || !float.IsFinite(effect.Strength) || effect.Strength <= 0f)
            return false;

        return type is not (PlayerEffectType.Fracture or PlayerEffectType.Protection) || effect.Strength <= 1f;
    }

    private void Write(PlayerEffectType type, TimedPlayerEffectData effect)
    {
        _timed[(int)type] = effect;
        _repository.Write(type, effect);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _infection.Set(false);
        _changed.Dispose();
    }
}

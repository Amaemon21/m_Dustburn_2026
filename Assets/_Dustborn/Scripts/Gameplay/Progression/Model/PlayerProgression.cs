using System;
using R3;

public sealed class PlayerProgression : IDisposable
{
    private readonly ExperienceCurve _curve;
    private readonly IReadOnlyStat _gain;
    private readonly PlayerProgressionRepository _repository;
    private readonly ReactiveProperty<int> _level;
    private readonly ReactiveProperty<float> _experience;
    private readonly ReactiveProperty<int> _required;
    private readonly Subject<int> _leveledUp = new();
    private readonly Subject<float> _gained = new();

    public ReadOnlyReactiveProperty<int> Level => _level;
    public ReadOnlyReactiveProperty<float> Experience => _experience;
    public ReadOnlyReactiveProperty<int> RequiredExperience => _required;
    public Observable<int> LeveledUp => _leveledUp;
    public Observable<float> Gained => _gained;
    public int MaxLevel => _curve.MaxLevel;
    public bool IsMaxLevel => _level.Value >= _curve.MaxLevel;

    public PlayerProgression(ExperienceCurve curve, PlayerStatSheet stats, PlayerProgressionRepository repository)
    {
        _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        _gain = stats.Get(PlayerStatType.ExperienceGain);
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        int level = curve.ClampLevel(repository.Origin.Level);
        int required = curve.RequiredFor(level);
        float experience = required == 0 ? 0f : Math.Clamp(repository.Origin.Experience, 0f, required);
        _level = new ReactiveProperty<int>(level);
        _required = new ReactiveProperty<int>(required);
        _experience = new ReactiveProperty<float>(experience);
        _repository.Write(level, experience);
    }

    public float AddExperience(float amount)
    {
        if (amount <= 0f || IsMaxLevel)
            return 0f;

        float gained = amount * _gain.Value.CurrentValue;
        if (gained <= 0f)
            return 0f;

        int level = _level.Value;
        float experience = _experience.Value + gained;
        int startLevel = level;
        while (level < _curve.MaxLevel && experience >= _curve.RequiredFor(level))
        {
            experience -= _curve.RequiredFor(level);
            level++;
        }

        if (level >= _curve.MaxLevel)
            experience = 0f;

        _required.Value = _curve.RequiredFor(level);
        _experience.Value = experience;
        _level.Value = level;
        _repository.Write(level, experience);

        for (int reached = startLevel + 1; reached <= level; reached++)
            _leveledUp.OnNext(reached);
        _gained.OnNext(gained);
        return gained;
    }

    public void Dispose()
    {
        _level.Dispose();
        _experience.Dispose();
        _required.Dispose();
        _leveledUp.Dispose();
        _gained.Dispose();
    }
}

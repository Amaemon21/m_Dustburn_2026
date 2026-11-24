public sealed class PlayerEffectsRepository
{
    private readonly ISaveService _saves;
    private readonly PlayerEffectsData _data;

    public PlayerEffectsRepository(ISaveService saves)
    {
        _saves = saves;
        _data = saves.Get<PlayerEffectsData>();
    }

    public TimedPlayerEffectData Read(PlayerEffectType type)
    {
        if (_data.Active != null && _data.Active.TryGetValue(type, out TimedPlayerEffectData effect))
            return effect;

        return default;
    }

    public void Write(PlayerEffectType type, TimedPlayerEffectData effect)
    {
        if (effect.Remaining > 0f)
            (_data.Active ??= new())[type] = effect;
        else
            _data.Active?.Remove(type);

        _saves.MarkDirty<PlayerEffectsData>();
    }
}

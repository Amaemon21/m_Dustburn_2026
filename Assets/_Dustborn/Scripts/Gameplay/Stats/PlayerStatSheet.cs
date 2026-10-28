using System;
using System.Collections.Generic;

public sealed class PlayerStatSheet : IDisposable
{
    private readonly Dictionary<PlayerStatType, Stat> _stats = new();

    public IEnumerable<PlayerStatType> Types => _stats.Keys;

    public PlayerStatSheet Add(PlayerStatType type, float baseValue, float minValue = 0f)
    {
        if (_stats.ContainsKey(type))
            throw new InvalidOperationException($"Player stat '{type}' is already added");

        _stats.Add(type, new Stat(baseValue, minValue));
        return this;
    }

    public Stat Get(PlayerStatType type)
    {
        if (_stats.TryGetValue(type, out Stat stat))
            return stat;
        throw new KeyNotFoundException($"Player stat '{type}' is not added to the sheet");
    }

    public bool TryGet(PlayerStatType type, out Stat stat) => _stats.TryGetValue(type, out stat);

    public float ValueOf(PlayerStatType type) => Get(type).Value.CurrentValue;

    public void Dispose()
    {
        foreach (Stat stat in _stats.Values)
            stat.Dispose();
        _stats.Clear();
    }
}

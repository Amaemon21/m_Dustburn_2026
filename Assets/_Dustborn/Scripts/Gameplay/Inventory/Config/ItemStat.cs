public readonly struct ItemStat
{
    public ItemStatType Type { get; }
    public float Value { get; }
    public float Limit { get; }

    public ItemStat(ItemStatType type, float value, float limit = 0f)
    {
        Type = type;
        Value = value;
        Limit = limit;
    }
}

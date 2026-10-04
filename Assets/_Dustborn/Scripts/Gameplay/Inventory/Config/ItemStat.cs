public readonly struct ItemStat
{
    public ItemStatType Type { get; }
    public float Value { get; }

    public ItemStat(ItemStatType type, float value)
    {
        Type = type;
        Value = value;
    }
}

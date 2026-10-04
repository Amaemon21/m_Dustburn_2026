using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

public abstract class InventoryItem : ScriptableObject
{
    [field: SerializeField, BoxGroup("Identity"), Label("Item ID"), HorizontalLine(2f, EColor.Blue)]
    public string ItemId { get; private set; }
    [field: SerializeField, BoxGroup("Identity")] public string ItemName { get; private set; }
    [field: SerializeField, BoxGroup("Identity")] public ItemCategory Category { get; private set; } = ItemCategory.Misc;
    [field: SerializeField, BoxGroup("Identity")] public ItemRarity Rarity { get; private set; } = ItemRarity.None;
    [field: SerializeField, BoxGroup("Identity"), TextArea(3, 8)] public string Description { get; private set; }
    [field: SerializeField, BoxGroup("Appearance"), HorizontalLine(2f, EColor.Green)]
    public Sprite Icon { get; private set; }
    [field: SerializeField, BoxGroup("Stacking"), HorizontalLine(2f, EColor.Gray), Range(1, InventorySlotProxy.MAX_AMOUNT)]
    public int MaxStack { get; private set; } = ItemCatalog.DEFAULT_MAX_STACK;
    [field: SerializeField, BoxGroup("Stacking"), Min(0f)] public float Weight { get; private set; } = 0.1f;

    public virtual bool IsUsable => false;

    protected virtual ItemCategory DefaultCategory => ItemCategory.Misc;

    public virtual void CollectStats(ICollection<ItemStat> stats, int amount)
    {
        AddIfSet(stats, ItemStatType.Weight, Weight * Mathf.Max(1, amount));
    }

    protected static void AddIfSet(ICollection<ItemStat> stats, ItemStatType type, float value)
    {
        if (!Mathf.Approximately(value, 0f))
            stats.Add(new ItemStat(type, value));
    }

    private void Reset()
    {
        Category = DefaultCategory;
    }
}

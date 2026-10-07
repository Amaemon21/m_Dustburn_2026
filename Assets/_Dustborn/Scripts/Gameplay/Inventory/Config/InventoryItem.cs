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
    [field: SerializeField, BoxGroup("Appearance"), HorizontalLine(2f, EColor.Green), ShowAssetPreview]
    public Sprite Icon { get; private set; }
    [field: SerializeField, BoxGroup("Appearance")] public PickUpItemInteractable PickUpPrefab { get; private set; }
    [field: SerializeField, BoxGroup("Hands"), HorizontalLine(2f, EColor.Violet)]
    public HeldItemConfig Held { get; private set; }
    [field: SerializeField, BoxGroup("Stacking"), HorizontalLine(2f, EColor.Gray), Range(1, InventorySlotProxy.MAX_AMOUNT)]
    public int MaxStack { get; private set; } = ItemCatalog.DEFAULT_MAX_STACK;
    [field: SerializeField, BoxGroup("Stacking"), Min(0f)] public float Weight { get; private set; } = 0.1f;
    [field: SerializeField, BoxGroup("Durability"), HorizontalLine(2f, EColor.Orange), Min(0)]
    public int Durability { get; private set; }

    public virtual bool IsUsable => false;
    public bool WearsOut => Durability > 0;

    protected virtual ItemCategory DefaultCategory => ItemCategory.Misc;

    public virtual void CollectStats(ICollection<ItemStat> stats, InventorySlotState state)
    {
        AddIfSet(stats, ItemStatType.Weight, Weight * Mathf.Max(1, state.Amount));
        if (WearsOut)
            stats.Add(new ItemStat(ItemStatType.Durability, Mathf.Max(0, Durability - state.Wear)));
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

    private void OnValidate()
    {
        if (WearsOut)
            MaxStack = 1;
    }
}

using System;
using System.Collections.Generic;

public static class InventorySort
{
    public const InventorySortMode DEFAULT_MODE = InventorySortMode.Type;
    private const char CYRILLIC_YO = 'ё';
    private const char CYRILLIC_YE = 'е';

    private static readonly ItemCategory[] TYPE_ORDER =
    {
        ItemCategory.Weapon,
        ItemCategory.Tool,
        ItemCategory.Ammo,
        ItemCategory.Medicine,
        ItemCategory.Food,
        ItemCategory.Drink,
        ItemCategory.Consumable,
        ItemCategory.Clothing,
        ItemCategory.Building,
        ItemCategory.Resource,
        ItemCategory.Misc
    };

    public static List<InventorySlotState> Order(ItemCatalog catalog, InventorySortMode mode, IReadOnlyList<InventorySlotState> stacks)
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

        Entry[] entries = new Entry[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
            entries[i] = new Entry(catalog, stacks[i], i);
        Array.Sort(entries, (a, b) => Compare(mode, a, b));

        List<InventorySlotState> ordered = new(entries.Length);
        foreach (Entry entry in entries)
            ordered.Add(entry.Stack);
        return ordered;
    }

    private static int Compare(InventorySortMode mode, Entry a, Entry b)
    {
        int result = mode switch
        {
            InventorySortMode.Name => ByName(a, b),
            InventorySortMode.Amount => Then(b.Stack.Amount.CompareTo(a.Stack.Amount), ByType(a, b)),
            InventorySortMode.Weight => Then(b.Weight.CompareTo(a.Weight), ByType(a, b)),
            _ => ByType(a, b)
        };
        return Then(result, a.Index.CompareTo(b.Index));
    }

    private static int ByType(Entry a, Entry b) => Then(a.Type.CompareTo(b.Type), ByName(a, b));

    private static int ByName(Entry a, Entry b)
    {
        int result = string.CompareOrdinal(a.Name, b.Name);
        result = Then(result, string.CompareOrdinal(a.Stack.ItemId, b.Stack.ItemId));
        result = Then(result, b.Stack.Amount.CompareTo(a.Stack.Amount));
        return Then(result, a.Stack.Wear.CompareTo(b.Stack.Wear));
    }

    private static int Then(int first, int second) => first != 0 ? first : second;

    private static int TypeRank(InventoryItem item)
    {
        if (item == null)
            return TYPE_ORDER.Length;

        int rank = Array.IndexOf(TYPE_ORDER, item.Category);
        return rank < 0 ? TYPE_ORDER.Length : rank;
    }

    private static string NameKey(InventoryItem item, string itemId)
    {
        string name = item != null && !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : itemId;
        return name.ToLowerInvariant().Replace(CYRILLIC_YO, CYRILLIC_YE);
    }

    private readonly struct Entry
    {
        public readonly InventorySlotState Stack;
        public readonly int Index;
        public readonly int Type;
        public readonly string Name;
        public readonly float Weight;

        public Entry(ItemCatalog catalog, InventorySlotState stack, int index)
        {
            InventoryItem item = catalog.GetItem(stack.ItemId);
            Stack = stack;
            Index = index;
            Type = TypeRank(item);
            Name = NameKey(item, stack.ItemId);
            Weight = catalog.WeightOf(stack.ItemId) * stack.Amount;
        }
    }
}

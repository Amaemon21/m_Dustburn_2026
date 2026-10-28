using System.Collections.Generic;
using System.Reflection;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyInventorySort()
    {
        ItemCatalog catalog = SortCatalog();
        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);

        string byType = SortedLayout(inventory, InventorySortMode.Type);
        Check(byType == "axe:1:0 axe:1:30 bandage:2:0 apple:5:0 stone:10:0 stone:2:0 - -",
            $"type sort puts tools, medicine, food, then resources, merges stacks and keeps the worn tool apart: {byType}");
        string byName = SortedLayout(inventory, InventorySortMode.Name);
        Check(byName == "bandage:2:0 stone:10:0 stone:2:0 axe:1:0 axe:1:30 apple:5:0 - -",
            $"name sort follows the Russian alphabet, bigger and fresher stacks first: {byName}");
        string byAmount = SortedLayout(inventory, InventorySortMode.Amount);
        Check(byAmount == "stone:10:0 apple:5:0 bandage:2:0 stone:2:0 axe:1:0 axe:1:30 - -",
            $"amount sort puts the biggest stacks first and breaks ties by type: {byAmount}");
        string byWeight = SortedLayout(inventory, InventorySortMode.Weight);
        Check(byWeight == "stone:10:0 axe:1:0 axe:1:30 stone:2:0 apple:5:0 bandage:2:0 - -",
            $"weight sort puts the heaviest stacks first and breaks ties by type: {byWeight}");

        inventory.RegisterInventory(new InventoryGridData { OwnerId = "sort-restricted", Size = new Vector2Int(2, 2) });
        inventory.Restrict("sort-restricted", (coords, itemId) => coords.y == 0 || itemId != "pack");
        inventory.AddItemsToInventory("sort-restricted", new Vector2Int(0, 0), "stone", 1);
        inventory.AddItemsToInventory("sort-restricted", new Vector2Int(1, 0), "pack", 1);
        inventory.AddItemsToInventory("sort-restricted", new Vector2Int(0, 1), "bandage", 1);
        inventory.AddItemsToInventory("sort-restricted", new Vector2Int(1, 1), "apple", 1);
        Check(inventory.Sort("sort-restricted", InventorySortMode.Type) && Layout(inventory, "sort-restricted") == "pack:1:0 bandage:1:0 apple:1:0 stone:1:0",
            $"a stack only some slots accept is placed first when the plain order leaves it nowhere: {Layout(inventory, "sort-restricted")}");

        inventory.RegisterInventory(new InventoryGridData { OwnerId = "sort-locked", Size = new Vector2Int(3, 1) });
        inventory.Lock("sort-locked", coords => coords.x == 0);
        inventory.AddItemsToInventory("sort-locked", new Vector2Int(0, 0), "stone", 1);
        inventory.AddItemsToInventory("sort-locked", new Vector2Int(2, 0), "axe", 1);
        Check(inventory.Sort("sort-locked", InventorySortMode.Type) && Layout(inventory, "sort-locked") == "stone:1:0 axe:1:0 -",
            $"a locked slot keeps its stack and the rest sort around it: {Layout(inventory, "sort-locked")}");

        inventory.RegisterInventory(new InventoryGridData { OwnerId = "sort-hotbar", Size = new Vector2Int(3, 1), Kind = InventoryGridKind.Hotbar });
        inventory.AddItemsToInventory("sort-hotbar", new Vector2Int(2, 0), "stone", 1);
        Check(!inventory.Sort("sort-hotbar", InventorySortMode.Type) && Layout(inventory, "sort-hotbar") == "- - stone:1:0",
            "the hotbar keeps its slots: its order is the number keys");

        using InventoryDragDropViewModel drag = new(inventory);
        using InventoryGridViewModel grid = new(inventory, "sort-locked", catalog, drag);
        using InventoryItemInfoViewModel info = new(catalog, grid);
        grid.SlotAt(new Vector2Int(1, 0)).Select.Execute(Unit.Default);
        grid.Sorting.Sort.Execute(InventorySortMode.Name);
        Check(!info.HasItem && !grid.SelectedSlot.CurrentValue.HasValue, "sorting a grid drops its selection, which would point at another item");
        Check(grid.Sorting.DefaultMode == InventorySortMode.Type && grid.Sorting.Modes.Count == 4, "the sort button offers four modes and sorts by type on a plain click");
    }

    private static string SortedLayout(InventoryService inventory, InventorySortMode mode)
    {
        string ownerId = "sort-" + mode;
        inventory.RegisterInventory(new InventoryGridData { OwnerId = ownerId, Size = new Vector2Int(4, 2), Kind = InventoryGridKind.Container });
        inventory.AddItemsToInventory(ownerId, new Vector2Int(0, 0), "stone", 4);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(2, 0), "apple", 3);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(3, 0), "stone", 8);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(0, 1), "axe", 1);
        inventory.AddWear(ownerId, new Vector2Int(0, 1), "axe", 30);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(1, 1), "axe", 1);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(2, 1), "bandage", 2);
        inventory.AddItemsToInventory(ownerId, new Vector2Int(3, 1), "apple", 2);
        inventory.Sort(ownerId, mode);
        return Layout(inventory, ownerId);
    }

    private static string Layout(IInventoryService inventory, string ownerId)
    {
        IReadOnlyInventoryGrid grid = inventory.GetInventory(ownerId);
        List<string> cells = new();
        for (int y = 0; y < grid.Size.y; y++)
            for (int x = 0; x < grid.Size.x; x++)
            {
                InventorySlotState state = grid.GetSlot(new Vector2Int(x, y)).State.CurrentValue;
                cells.Add(state.IsEmpty ? "-" : $"{state.ItemId}:{state.Amount}:{state.Wear}");
            }
        return string.Join(" ", cells);
    }

    private static ItemCatalog SortCatalog()
    {
        InventoryItem[] items =
        {
            SortItem("stone", "Камень", ItemCategory.Resource, 10, 1f, 0),
            SortItem("axe", "Топор", ItemCategory.Tool, 1, 2f, 100),
            SortItem("bandage", "Бинт", ItemCategory.Medicine, 5, 0.1f, 0),
            SortItem("apple", "Яблоко", ItemCategory.Food, 10, 0.2f, 0),
            SortItem("pack", "Рюкзак", ItemCategory.Clothing, 1, 1f, 0)
        };
        InventoryItemDatabase database = FakeAsset<InventoryItemDatabase>();
        typeof(InventoryItemDatabase).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, items);
        return new ItemCatalog(database);
    }

    private static InventoryItem SortItem(string itemId, string itemName, ItemCategory category, int maxStack, float weight, int durability)
    {
        ResourceInventoryItem item = FakeItem(itemId);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.ItemName), itemName);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Category), category);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.MaxStack), maxStack);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Weight), weight);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Durability), durability);
        return item;
    }
}

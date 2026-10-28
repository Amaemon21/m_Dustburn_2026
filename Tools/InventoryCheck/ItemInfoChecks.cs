using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

internal static partial class Harness
{
    private static void VerifyItemInfoStats()
    {
        MeleeActionConfig swing = new();
        SetBackingField(swing, nameof(MeleeActionConfig.Damage), 30);
        SetBackingField(swing, nameof(MeleeActionConfig.Recovery), 0.2f);
        SetBackingField(swing, nameof(MeleeActionConfig.Reach), 2.4f);
        SetBackingField(swing, nameof(MeleeActionConfig.StaminaCost), 8f);
        SetBackingField(swing, nameof(MeleeActionConfig.WearPerHit), 2);
        SetBackingField(swing, nameof(MeleeActionConfig.Bonuses),
            new[] { new MaterialBonus(FakeAsset<BlockMaterial>(), 2f, 1.5f), new MaterialBonus(FakeAsset<BlockMaterial>(), 0.5f, 1f) });

        HandsMotionClip attack = Motion(HandsMotion.Attack, FakeAsset<AnimationClip>(), 0.05f, 0.8f);
        SetBackingField(attack, nameof(HandsMotionClip.Speed), 2f);
        HeldItemConfig held = FakeAsset<HeldItemConfig>();
        SetBackingField(held, nameof(HeldItemConfig.Animations), AnimationSet(null, attack));
        SetBackingField(held, nameof(HeldItemConfig.Primary), swing);

        ToolInventoryItem axe = FakeAsset<ToolInventoryItem>();
        Describe(axe, ItemCategory.Tool, ItemRarity.Good, 1, 2.5f, 300);
        SetDeclaredField<InventoryItem>(axe, nameof(InventoryItem.Held), held);

        List<ItemStat> stats = StatsOf(axe, new InventorySlotState("axe", 1, 120));
        Check(Types(stats) == "Damage SwingTime StaminaCost Reach Durability Weight",
            $"a tool lists its swing before its wear and weight, without material bonuses or hits left: {Types(stats)}");
        Check(Text(stats[0]) == "Урон: 30" && Text(stats[1]) == "Время удара: 0,6 с" && Text(stats[2]) == "Выносливость за удар: 8" &&
              Text(stats[3]) == "Дальность: 2,4 м", "a swing reads as damage, time from the clip, stamina and reach");
        Check(Text(stats[4]) == "Прочность: 180 / 300" && Text(stats[5]) == "Вес: 2,5 кг", "durability shows what is left of the total");
        Check(ItemInfoText.Subtitle(axe) == "Инструмент · Хорошее качество", "a tool subtitle is its category and quality in Russian");

        ResourceInventoryItem logs = FakeAsset<ResourceInventoryItem>();
        Describe(logs, ItemCategory.Resource, ItemRarity.None, 50, 0.5f, 0);
        stats = StatsOf(logs, new InventorySlotState("logs", 12));
        Check(Types(stats) == "Amount Weight" && Text(stats[0]) == "Количество: 12 / 50" && Text(stats[1]) == "Вес: 6 кг (0,5 за шт.)",
            "a stack shows its size and its weight per piece");
        Check(ItemInfoText.Subtitle(logs) == "Ресурс · Сырьё", "a resource subtitle names its kind and skips an unset quality");
        stats = StatsOf(logs, new InventorySlotState("logs", 1));
        Check(Text(stats[1]) == "Вес: 0,5 кг", "a single piece shows its weight alone");

        FoodInventoryItem stew = FakeAsset<FoodInventoryItem>();
        Describe(stew, ItemCategory.Food, ItemRarity.None, 5, 0.2f, 0);
        SetDeclaredField<ConsumableInventoryItem>(stew, nameof(ConsumableInventoryItem.Food), 25f);
        SetDeclaredField<ConsumableInventoryItem>(stew, nameof(ConsumableInventoryItem.Water), -10f);
        SetDeclaredField<ConsumableInventoryItem>(stew, nameof(ConsumableInventoryItem.Infection), 5f);
        SetDeclaredField<ConsumableInventoryItem>(stew, nameof(ConsumableInventoryItem.Duration), 10f);
        stats = StatsOf(stew, new InventorySlotState("stew", 1));
        Check(Types(stats) == "Food Water Infection Duration Amount Weight", $"a consumable lists its effects first: {Types(stats)}");
        Check(Text(stats[0]) == "Сытость: +25" && ItemStatText.Mood(stats[0]) == ItemStatMood.Good, "a restoring effect is signed and good");
        Check(Text(stats[1]) == "Вода: −10" && ItemStatText.Mood(stats[1]) == ItemStatMood.Bad, "a draining effect carries a real minus and is bad");
        Check(Text(stats[2]) == "Инфекция: +5" && ItemStatText.Mood(stats[2]) == ItemStatMood.Bad, "infection gained is bad");
        Check(ItemStatText.Mood(new ItemStat(ItemStatType.Infection, -5f)) == ItemStatMood.Good, "infection cured is good");
        Check(Text(stats[3]) == "Длительность: 10 с" && ItemStatText.Mood(stats[3]) == ItemStatMood.Neutral, "duration is plain seconds");

        BackpackInventoryItem pack = FakeAsset<BackpackInventoryItem>();
        Describe(pack, ItemCategory.Clothing, ItemRarity.None, 1, 2f, 0);
        SetBackingField(pack, nameof(BackpackInventoryItem.ExtraRows), 1);
        SetBackingField(pack, nameof(BackpackInventoryItem.CarryBonus), 10f);
        stats = StatsOf(pack, new InventorySlotState("pack", 1));
        Check(Types(stats) == "ExtraRows CarryBonus Weight" && Text(stats[0]) == "Ряды рюкзака: +1" &&
              Text(stats[1]) == "Грузоподъёмность: +10 кг" && ItemStatText.Mood(stats[1]) == ItemStatMood.Good, "a backpack shows what it adds");

        EquipmentInventoryItem hat = FakeAsset<EquipmentInventoryItem>();
        Describe(hat, ItemCategory.Clothing, ItemRarity.Excellent, 1, 0f, 0);
        Check(ItemInfoText.Subtitle(hat) == "Одежда · Головной убор · Шедевр", "equipment names its slot between category and quality");
        Check(StatsOf(hat, new InventorySlotState("hat", 1)).Count == 0, "an item with nothing to say lists nothing");
    }

    private static void Describe(InventoryItem item, ItemCategory category, ItemRarity rarity, int maxStack, float weight, int durability)
    {
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Category), category);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Rarity), rarity);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.MaxStack), maxStack);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Weight), weight);
        SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.Durability), durability);
    }

    private static List<ItemStat> StatsOf(InventoryItem item, InventorySlotState state)
    {
        var stats = new List<ItemStat>();
        item.CollectStats(stats, state);
        return stats;
    }

    private static string Types(List<ItemStat> stats) => string.Join(" ", stats.ConvertAll(stat => stat.Type.ToString()));

    private static string Text(ItemStat stat) => $"{ItemStatText.Label(stat)}: {ItemStatText.Value(stat)}";

    private static void VerifyItemNameHover()
    {
        ItemCatalog catalog = NamedCatalog(("wood", "Дерево"), ("stone", "Камень"));
        using InventoryRepository repository = new(new SaveService(InventoryRegistry(), new MemoryStorage(), ProjectSerializer()));
        using InventoryService inventory = new(repository, catalog);
        inventory.RegisterInventory(new InventoryGridData { OwnerId = "hover", Size = new Vector2Int(3, 1) });
        inventory.AddItemsToInventory("hover", new Vector2Int(0, 0), "wood", 2);
        inventory.AddItemsToInventory("hover", new Vector2Int(1, 0), "stone", 1);
        using InventoryDragDropViewModel drag = new(inventory);
        using InventoryGridViewModel grid = new(inventory, "hover", catalog, drag);
        ManualTimers timers = new();
        using ItemNameHoverViewModel hover = new(catalog, drag, timers.Start, grid);
        InventorySlotViewModel wood = grid.SlotAt(new Vector2Int(0, 0));
        InventorySlotViewModel stone = grid.SlotAt(new Vector2Int(1, 0));
        Vector3 woodTop = new(10f, 40f, 0f);
        Vector3 stoneTop = new(50f, 40f, 0f);

        Check(hover.Name.CurrentValue == null, "the hover name starts hidden");
        wood.SetHover(woodTop);
        Check(hover.Name.CurrentValue == null && hover.Anchor.CurrentValue == woodTop, "the name waits out the show delay at the slot's top edge");
        timers.Elapse(ItemNameHoverViewModel.SHOW_DELAY);
        Check(hover.Name.CurrentValue == "Дерево", "the name shows once the pointer rests on the slot");
        wood.SetHover(null);
        Check(hover.Name.CurrentValue == null, "leaving the slot hides the name at once");
        stone.SetHover(stoneTop);
        Check(hover.Name.CurrentValue == "Камень" && hover.Anchor.CurrentValue == stoneTop, "moving straight to the next slot shows its name without a delay");
        wood.SetHover(null);
        Check(hover.Name.CurrentValue == "Камень", "a late exit from another slot keeps the hovered name");
        stone.SetHover(null);
        timers.Elapse(ItemNameHoverViewModel.SWITCH_GRACE);
        wood.SetHover(woodTop);
        Check(hover.Name.CurrentValue == null, "after the grace a new hover waits for the delay again");
        wood.SetHover(null);
        stone.SetHover(stoneTop);
        timers.Elapse(ItemNameHoverViewModel.SHOW_DELAY);
        Check(hover.Name.CurrentValue == "Камень", "a slot crossed before the delay never shows its name");

        inventory.RemoveItemsFromInventory("hover", new Vector2Int(1, 0), "stone", 1);
        Check(hover.Name.CurrentValue == null, "the name hides when the hovered slot empties");
        grid.SlotAt(new Vector2Int(2, 0)).SetHover(stoneTop);
        timers.Elapse(ItemNameHoverViewModel.SHOW_DELAY);
        Check(hover.Name.CurrentValue == null, "an empty slot shows no name");

        timers.Elapse(ItemNameHoverViewModel.SWITCH_GRACE);
        wood.SetHover(woodTop);
        timers.Elapse(ItemNameHoverViewModel.SHOW_DELAY);
        drag.Begin(wood, Vector2.zero);
        Check(hover.Name.CurrentValue == null, "the name hides while an item is dragged");
        drag.Cancel();
        Check(hover.Name.CurrentValue == "Дерево", "the name comes straight back after a short drag over the slot");
        inventory.SetSize("hover", new Vector2Int(4, 1));
        Check(hover.Name.CurrentValue == null, "rebuilt slots drop the hover");
    }

    private sealed class ManualTimers
    {
        private readonly List<(TimeSpan Due, Subject<Unit> Tick)> _pending = new();

        public Observable<Unit> Start(TimeSpan due)
        {
            Subject<Unit> tick = new();
            _pending.Add((due, tick));
            return tick;
        }

        public void Elapse(TimeSpan due)
        {
            foreach ((TimeSpan Due, Subject<Unit> Tick) timer in _pending.ToArray())
            {
                if (timer.Due != due)
                    continue;
                _pending.Remove(timer);
                timer.Tick.OnNext(Unit.Default);
            }
        }
    }

    private static ItemCatalog NamedCatalog(params (string Id, string Name)[] entries)
    {
        InventoryItem[] items = new InventoryItem[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            ResourceInventoryItem item = FakeItem(entries[i].Id);
            SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.ItemName), entries[i].Name);
            SetDeclaredField<InventoryItem>(item, nameof(InventoryItem.MaxStack), 10);
            items[i] = item;
        }

        InventoryItemDatabase database = FakeAsset<InventoryItemDatabase>();
        typeof(InventoryItemDatabase).GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(database, items);
        return new ItemCatalog(database);
    }
}

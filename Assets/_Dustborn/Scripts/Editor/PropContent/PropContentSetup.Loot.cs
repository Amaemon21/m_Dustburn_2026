using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class PropContentSetup
{
    private const string LOOT_FOLDER = "Assets/_Dustborn/Content/Configs/Props/Loot";
    private const string BLOCK_FOLDER = "Assets/_Dustborn/Content/Configs/Props/Blocks";
    private const string MATERIAL_FOLDER = "Assets/_Dustborn/Content/Configs/Block/BlockMaterial";

    private const string SCRAP = "scrap_metal";
    private const string CLOTH = "cloth";
    private const string PAPER = "paper";
    private const string RUBBER = "rubber";
    private const string DUCT_TAPE = "duct_tape";
    private const string BATTERY = "battery";
    private const string CANNED_FOOD = "canned_food";
    private const string WATER_BOTTLE = "water_bottle";
    private const string BANDAGE = "bandage";
    private const string PAINKILLERS = "painkillers";
    private const string WOOD = "wood";
    private const string STONE = "stone";

    private const string LOOT_CAR = "Loot_Car";
    private const string LOOT_CRATE = "Loot_Crate";
    private const string LOOT_CARDBOARD = "Loot_Cardboard";
    private const string LOOT_LUGGAGE = "Loot_Luggage";
    private const string LOOT_MEDICAL = "Loot_Medical";
    private const string LOOT_TOOLBOX = "Loot_Toolbox";
    private const string LOOT_MILITARY = "Loot_Military";
    private const string LOOT_TRASH = "Loot_Trash";
    private const string LOOT_DUMPSTER = "Loot_Dumpster";
    private const string LOOT_SUPPLY = "Loot_Supply";
    private const string LOOT_BRIEFCASE = "Loot_Briefcase";

    private const string BLOCK_METAL_SMALL = "Prop_Metal_Small";
    private const string BLOCK_METAL_LARGE = "Prop_Metal_Large";
    private const string BLOCK_WOOD_SMALL = "Prop_Wood_Small";
    private const string BLOCK_WOOD_LARGE = "Prop_Wood_Large";
    private const string BLOCK_RUBBER = "Prop_Rubber";
    private const string BLOCK_RUBBER_PILE = "Prop_Rubber_Pile";
    private const string BLOCK_CONCRETE = "Prop_Concrete";
    private const string BLOCK_JUNK = "Prop_Junk";
    private const string BLOCK_PLASTIC = "Prop_Plastic";

    private const string MATERIAL_WOOD = "Wood_BlockMaterial";
    private const string MATERIAL_STONE = "Stone_BlockMaterial";
    private const string MATERIAL_METAL = "Metal_BlockMaterial";

    private readonly struct Drop
    {
        public readonly string Item;
        public readonly int Min;
        public readonly int Max;
        public readonly float Chance;

        public Drop(string item, int min, int max, float chance)
        {
            Item = item;
            Min = min;
            Max = max;
            Chance = chance;
        }
    }

    private readonly struct LootRow
    {
        public readonly string Name;
        public readonly string Title;
        public readonly Vector2Int Size;
        public readonly int MaxStacks;
        public readonly Drop[] Drops;

        public LootRow(string name, string title, Vector2Int size, int maxStacks, params Drop[] drops)
        {
            Name = name;
            Title = title;
            Size = size;
            MaxStacks = maxStacks;
            Drops = drops;
        }
    }

    private readonly struct BlockRow
    {
        public readonly string Name;
        public readonly string Material;
        public readonly int Health;
        public readonly Drop[] Harvest;
        public readonly Drop[] Destroy;

        public BlockRow(string name, string material, int health, Drop[] harvest, Drop[] destroy = null)
        {
            Name = name;
            Material = material;
            Health = health;
            Harvest = harvest;
            Destroy = destroy ?? new Drop[0];
        }
    }

    private static readonly LootRow[] LOOT =
    {
        new(LOOT_CAR, "Машина", new Vector2Int(5, 3), 3,
            new Drop(SCRAP, 2, 6, 0.8f), new Drop(RUBBER, 1, 3, 0.4f), new Drop(CLOTH, 1, 2, 0.3f), new Drop(WATER_BOTTLE, 1, 1, 0.25f),
            new Drop(CANNED_FOOD, 1, 1, 0.2f), new Drop(DUCT_TAPE, 1, 1, 0.15f), new Drop(BATTERY, 1, 1, 0.2f), new Drop(PAINKILLERS, 1, 1, 0.1f),
            new Drop("pickaxe", 1, 1, 0.03f), new Drop("axe", 1, 1, 0.03f)),
        new(LOOT_CRATE, "Деревянный ящик", new Vector2Int(5, 4), 3,
            new Drop(WOOD, 3, 10, 0.6f), new Drop(SCRAP, 1, 4, 0.4f), new Drop(CANNED_FOOD, 1, 2, 0.3f), new Drop(CLOTH, 1, 3, 0.3f),
            new Drop(DUCT_TAPE, 1, 1, 0.1f), new Drop("axe", 1, 1, 0.04f), new Drop("pickaxe", 1, 1, 0.04f), new Drop("auger", 1, 1, 0.01f)),
        new(LOOT_CARDBOARD, "Картонная коробка", new Vector2Int(4, 3), 2,
            new Drop(PAPER, 1, 5, 0.7f), new Drop(CLOTH, 1, 2, 0.3f), new Drop(CANNED_FOOD, 1, 1, 0.25f), new Drop(WATER_BOTTLE, 1, 1, 0.2f),
            new Drop(BATTERY, 1, 2, 0.1f)),
        new(LOOT_LUGGAGE, "Чемодан", new Vector2Int(4, 3), 3,
            new Drop(CLOTH, 2, 5, 0.8f), new Drop(PAPER, 1, 3, 0.3f), new Drop(BANDAGE, 1, 2, 0.3f), new Drop(PAINKILLERS, 1, 1, 0.2f),
            new Drop(WATER_BOTTLE, 1, 1, 0.2f)),
        new(LOOT_MEDICAL, "Аптечка", new Vector2Int(3, 3), 2,
            new Drop(BANDAGE, 1, 3, 0.9f), new Drop(PAINKILLERS, 1, 2, 0.6f), new Drop(CLOTH, 1, 1, 0.3f)),
        new(LOOT_TOOLBOX, "Ящик с инструментами", new Vector2Int(4, 2), 2,
            new Drop(SCRAP, 1, 4, 0.7f), new Drop(DUCT_TAPE, 1, 2, 0.4f), new Drop(BATTERY, 1, 2, 0.2f), new Drop("pickaxe", 1, 1, 0.1f),
            new Drop("axe_iron", 1, 1, 0.08f), new Drop("pickaxe_steel", 1, 1, 0.03f), new Drop("axe_steel", 1, 1, 0.03f),
            new Drop("auger", 1, 1, 0.02f), new Drop("chainsaw", 1, 1, 0.01f)),
        new(LOOT_MILITARY, "Армейский ящик", new Vector2Int(4, 3), 3,
            new Drop(CANNED_FOOD, 1, 3, 0.6f), new Drop(WATER_BOTTLE, 1, 2, 0.5f), new Drop(BANDAGE, 1, 2, 0.4f), new Drop(PAINKILLERS, 1, 2, 0.3f),
            new Drop(SCRAP, 1, 3, 0.3f), new Drop(BATTERY, 1, 2, 0.3f), new Drop("axe_iron", 1, 1, 0.05f), new Drop("pickaxe_steel", 1, 1, 0.03f)),
        new(LOOT_TRASH, "Мусор", new Vector2Int(3, 3), 2,
            new Drop(PAPER, 1, 4, 0.7f), new Drop(CLOTH, 1, 2, 0.3f), new Drop(SCRAP, 1, 1, 0.2f), new Drop(RUBBER, 1, 1, 0.1f),
            new Drop(CANNED_FOOD, 1, 1, 0.08f), new Drop(BATTERY, 1, 1, 0.05f)),
        new(LOOT_DUMPSTER, "Мусорный бак", new Vector2Int(5, 4), 3,
            new Drop(PAPER, 2, 6, 0.7f), new Drop(CLOTH, 1, 3, 0.4f), new Drop(SCRAP, 1, 3, 0.4f), new Drop(RUBBER, 1, 2, 0.2f),
            new Drop(CANNED_FOOD, 1, 1, 0.15f), new Drop(DUCT_TAPE, 1, 1, 0.08f), new Drop(BATTERY, 1, 1, 0.08f)),
        new(LOOT_SUPPLY, "Припасы", new Vector2Int(5, 4), 4,
            new Drop(CANNED_FOOD, 1, 3, 0.6f), new Drop(WATER_BOTTLE, 1, 3, 0.6f), new Drop(CLOTH, 1, 4, 0.4f), new Drop(WOOD, 2, 6, 0.4f),
            new Drop(BANDAGE, 1, 2, 0.3f), new Drop(DUCT_TAPE, 1, 1, 0.2f)),
        new(LOOT_BRIEFCASE, "Кейс", new Vector2Int(3, 2), 2,
            new Drop(PAPER, 1, 3, 0.6f), new Drop(PAINKILLERS, 1, 1, 0.3f), new Drop(BATTERY, 1, 2, 0.3f), new Drop(SCRAP, 1, 2, 0.3f))
    };

    private static readonly BlockRow[] BLOCKS =
    {
        new(BLOCK_METAL_SMALL, MATERIAL_METAL, 200, new[] { new Drop(SCRAP, 3, 3, 1f) }),
        new(BLOCK_METAL_LARGE, MATERIAL_METAL, 900, new[] { new Drop(SCRAP, 15, 15, 1f), new Drop(RUBBER, 3, 3, 1f) },
            new[] { new Drop(BATTERY, 1, 1, 0.3f) }),
        new(BLOCK_WOOD_SMALL, MATERIAL_WOOD, 150, new[] { new Drop(WOOD, 6, 6, 1f) }),
        new(BLOCK_WOOD_LARGE, MATERIAL_WOOD, 450, new[] { new Drop(WOOD, 18, 18, 1f) }),
        new(BLOCK_RUBBER, null, 120, new[] { new Drop(RUBBER, 3, 3, 1f) }),
        new(BLOCK_RUBBER_PILE, null, 400, new[] { new Drop(RUBBER, 10, 10, 1f) }),
        new(BLOCK_CONCRETE, MATERIAL_STONE, 1200, new[] { new Drop(STONE, 20, 20, 1f) }),
        new(BLOCK_JUNK, null, 220, new[] { new Drop(SCRAP, 2, 2, 1f), new Drop(PAPER, 2, 2, 1f) },
            new[] { new Drop(CLOTH, 1, 2, 0.5f), new Drop(DUCT_TAPE, 1, 1, 0.1f) }),
        new(BLOCK_PLASTIC, null, 80, new[] { new Drop(RUBBER, 1, 1, 1f) })
    };

    private static Dictionary<string, ContainerConfig> EnsureLoot(Dictionary<string, InventoryItem> items, StringBuilder report)
    {
        var configs = new Dictionary<string, ContainerConfig>();
        EnsureFolder(LOOT_FOLDER);

        foreach (LootRow row in LOOT)
        {
            string path = $"{LOOT_FOLDER}/{row.Name}.asset";
            var config = AssetDatabase.LoadAssetAtPath<ContainerConfig>(path);

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ContainerConfig>();
                config.EditorSetup(row.Title, row.Size, LootOf(row, items, report), row.MaxStacks);
                AssetDatabase.CreateAsset(config, path);
                report.AppendLine($"Created loot table {row.Name}");
            }

            configs[row.Name] = config;
        }

        return configs;
    }

    private static ContainerLoot[] LootOf(LootRow row, Dictionary<string, InventoryItem> items, StringBuilder report)
    {
        var loot = new List<ContainerLoot>();

        foreach (Drop drop in row.Drops)
        {
            if (items.TryGetValue(drop.Item, out InventoryItem item))
                loot.Add(new ContainerLoot(item, drop.Min, drop.Max, drop.Chance));
            else
                report.AppendLine($"{row.Name}: no item '{drop.Item}', the entry is skipped");
        }

        return loot.ToArray();
    }

    private static Dictionary<string, BlockConfig> EnsureBlocks(Dictionary<string, InventoryItem> items, StringBuilder report)
    {
        var configs = new Dictionary<string, BlockConfig>();
        EnsureFolder(BLOCK_FOLDER);

        foreach (BlockRow row in BLOCKS)
        {
            string path = $"{BLOCK_FOLDER}/{row.Name}.asset";
            var config = AssetDatabase.LoadAssetAtPath<BlockConfig>(path);

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BlockConfig>();
                config.EditorSetup(EnsureMaterial(row.Material, report), row.Health, HarvestOf(row, items, report), DropsOf(row, items, report));
                AssetDatabase.CreateAsset(config, path);
                report.AppendLine($"Created block {row.Name}");
            }

            configs[row.Name] = config;
        }

        return configs;
    }

    private static HarvestYield[] HarvestOf(BlockRow row, Dictionary<string, InventoryItem> items, StringBuilder report)
    {
        var harvest = new List<HarvestYield>();

        foreach (Drop drop in row.Harvest)
        {
            if (items.TryGetValue(drop.Item, out InventoryItem item))
                harvest.Add(new HarvestYield(item, drop.Min));
            else
                report.AppendLine($"{row.Name}: no item '{drop.Item}', the harvest entry is skipped");
        }

        return harvest.ToArray();
    }

    private static DestroyDrop[] DropsOf(BlockRow row, Dictionary<string, InventoryItem> items, StringBuilder report)
    {
        var drops = new List<DestroyDrop>();

        foreach (Drop drop in row.Destroy)
        {
            if (items.TryGetValue(drop.Item, out InventoryItem item))
                drops.Add(new DestroyDrop(item, drop.Min, drop.Max, drop.Chance));
            else
                report.AppendLine($"{row.Name}: no item '{drop.Item}', the destroy drop is skipped");
        }

        return drops.ToArray();
    }

    private static BlockMaterial EnsureMaterial(string name, StringBuilder report)
        => name == null ? null : EnsureAsset<BlockMaterial>($"{MATERIAL_FOLDER}/{name}.asset", report);
}

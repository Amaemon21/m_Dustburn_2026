using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class PropContentSetup
{
    private const string ITEM_DATABASE = "Assets/_Dustborn/Content/Configs/Inventory/InventoryItems.asset";
    private const string ITEM_FOLDER = "Assets/_Dustborn/Content/Configs/Inventory/Items";
    private const string PICKUP_FOLDER = "Assets/_Dustborn/Content/Prefabs/PickUpItems";
    private const string SYNTY_PREFABS = "Assets/Assets/Synty/PolygonApocalypse/Prefabs";
    private const float PICKUP_MASS = 1f;

    private enum ItemKind
    {
        Resource,
        Food,
        Medicine
    }

    private readonly struct ItemRow
    {
        public readonly string Id;
        public readonly string Asset;
        public readonly ItemKind Kind;
        public readonly ItemCategory Category;
        public readonly string Name;
        public readonly string Description;
        public readonly float Weight;
        public readonly int MaxStack;
        public readonly float Health;
        public readonly float Food;
        public readonly float Water;
        public readonly float Infection;
        public readonly string Model;
        public readonly float ModelScale;

        public ItemRow(string id, string asset, ItemKind kind, ItemCategory category, string name, string description, float weight,
            int maxStack, string model, float modelScale, float health = 0f, float food = 0f, float water = 0f, float infection = 0f)
        {
            Id = id;
            Asset = asset;
            Kind = kind;
            Category = category;
            Name = name;
            Description = description;
            Weight = weight;
            MaxStack = maxStack;
            Model = model;
            ModelScale = modelScale;
            Health = health;
            Food = food;
            Water = water;
            Infection = infection;
        }
    }

    private static readonly ItemRow[] ITEMS =
    {
        new(SCRAP, "ScrapMetal", ItemKind.Resource, ItemCategory.Resource, "Металлолом", "Ржавое железо с машин и бочек. Пойдёт в переплавку.", 0.5f, 200,
            "Props/SM_Prop_Iron_Sheet_01", 0.25f),
        new(CLOTH, "Cloth", ItemKind.Resource, ItemCategory.Resource, "Ткань", "Обрывки одежды и тряпьё.", 0.1f, 200,
            "Props/SM_Prop_Sheet_01", 0.25f),
        new(PAPER, "Paper", ItemKind.Resource, ItemCategory.Resource, "Бумага", "Старые бумаги. Горят и годятся на растопку.", 0.02f, 500,
            "Props/SM_Prop_Papers_01", 1f),
        new(RUBBER, "Rubber", ItemKind.Resource, ItemCategory.Resource, "Резина", "Куски покрышек.", 0.3f, 200,
            "Props/SM_Prop_Tire_01", 0.35f),
        new(DUCT_TAPE, "DuctTape", ItemKind.Resource, ItemCategory.Resource, "Изолента", "Чинит почти всё.", 0.1f, 50,
            "Item/SM_Item_Tape_01", 1f),
        new(BATTERY, "Battery", ItemKind.Resource, ItemCategory.Resource, "Батарейка", "Ещё держит заряд.", 0.2f, 20,
            "Item/SM_Item_Battery_01", 1f),
        new(CANNED_FOOD, "CannedFood", ItemKind.Food, ItemCategory.Food, "Консервы", "Банка без этикетки. Лучше не думать, что внутри.", 0.4f, 20,
            "Item/SM_Item_Can_01", 1f, food: 25f, water: 5f),
        new(WATER_BOTTLE, "WaterBottle", ItemKind.Food, ItemCategory.Drink, "Бутылка воды", "Запечатанная бутылка чистой воды.", 0.6f, 10,
            "Item/SM_Item_Drink_Bottle_01", 1f, water: 35f),
        new(BANDAGE, "Bandage", ItemKind.Medicine, ItemCategory.Medicine, "Бинт", "Чистая повязка.", 0.05f, 20,
            "Item/SM_Item_ToiletRoll_02", 1f, health: 15f),
        new(PAINKILLERS, "Painkillers", ItemKind.Medicine, ItemCategory.Medicine, "Обезболивающее", "Снимает боль и немного сбивает заражение.", 0.05f, 20,
            "Item/SM_Item_Pills_01", 1f, health: 10f, infection: -5f)
    };

    private static Dictionary<string, InventoryItem> EnsureItems(StringBuilder report)
    {
        var database = AssetDatabase.LoadAssetAtPath<InventoryItemDatabase>(ITEM_DATABASE);
        if (database == null)
            throw new InvalidOperationException($"The inventory item database is missing at {ITEM_DATABASE}");

        var items = new Dictionary<string, InventoryItem>();
        foreach (InventoryItem item in database.Items)
            if (item != null && !string.IsNullOrWhiteSpace(item.ItemId))
                items[item.ItemId] = item;

        foreach (ItemRow row in ITEMS)
        {
            if (items.ContainsKey(row.Id))
                continue;

            InventoryItem item = CreateItem(row, report);
            database.EditorAdd(item);
            items[row.Id] = item;
        }

        EditorUtility.SetDirty(database);
        return items;
    }

    private static InventoryItem CreateItem(ItemRow row, StringBuilder report)
    {
        string path = $"{ITEM_FOLDER}/{row.Asset}_{row.Kind}InventoryItem.asset";
        var existing = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);
        if (existing != null)
            return existing;

        InventoryItem item = row.Kind switch
        {
            ItemKind.Food => ScriptableObject.CreateInstance<FoodInventoryItem>(),
            ItemKind.Medicine => ScriptableObject.CreateInstance<MedicineInventoryItem>(),
            _ => ScriptableObject.CreateInstance<ResourceInventoryItem>()
        };
        AssetDatabase.CreateAsset(item, path);

        var serialized = new SerializedObject(item);
        Set(serialized, "ItemId", row.Id);
        Set(serialized, "ItemName", row.Name);
        Set(serialized, "Description", row.Description);
        serialized.FindProperty("<Category>k__BackingField").intValue = (int)row.Category;
        serialized.FindProperty("<Weight>k__BackingField").floatValue = row.Weight;
        serialized.FindProperty("<MaxStack>k__BackingField").intValue = row.MaxStack;

        if (row.Kind != ItemKind.Resource)
        {
            serialized.FindProperty("<Health>k__BackingField").floatValue = row.Health;
            serialized.FindProperty("<Food>k__BackingField").floatValue = row.Food;
            serialized.FindProperty("<Water>k__BackingField").floatValue = row.Water;
            serialized.FindProperty("<Infection>k__BackingField").floatValue = row.Infection;
        }

        serialized.FindProperty("<PickUpPrefab>k__BackingField").objectReferenceValue = CreatePickup(row, item, report);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"Created item {row.Id} ({row.Name}) without an icon: assign Icon in {path}");
        return item;
    }

    private static PickUpItemInteractable CreatePickup(ItemRow row, InventoryItem item, StringBuilder report)
    {
        string path = $"{PICKUP_FOLDER}/{row.Asset}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<PickUpItemInteractable>(path);
        if (existing != null)
            return existing;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{SYNTY_PREFABS}/{row.Model}.prefab");
        if (model == null)
        {
            report.AppendLine($"No model {row.Model} for the {row.Id} pickup: it can be looted but not dropped until PickUpPrefab is set");
            return null;
        }

        var root = new GameObject(row.Asset);

        try
        {
            var view = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            view.transform.localScale = Vector3.one * row.ModelScale;
            foreach (Collider collider in view.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            BoxCollider box = root.AddComponent<BoxCollider>();
            if (PrefabBounds.TryMeasure(root, out Bounds bounds))
            {
                box.center = bounds.center;
                box.size = bounds.size;
            }

            root.AddComponent<Rigidbody>().mass = PICKUP_MASS;
            var pickup = root.AddComponent<PickUpItemInteractable>();
            var serialized = new SerializedObject(pickup);
            serialized.FindProperty("_item").objectReferenceValue = item;
            serialized.FindProperty("_amount").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            return saved.GetComponent<PickUpItemInteractable>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void Set(SerializedObject serialized, string property, string value)
        => serialized.FindProperty($"<{property}>k__BackingField").stringValue = value;
}

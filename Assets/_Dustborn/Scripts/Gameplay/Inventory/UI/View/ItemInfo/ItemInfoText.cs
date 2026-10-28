using System.Collections.Generic;

public static class ItemInfoText
{
    private const string SEPARATOR = " · ";

    public static string Subtitle(InventoryItem item)
    {
        if (item == null)
            return string.Empty;

        var parts = new List<string>(3) { Category(item.Category) };
        string detail = Detail(item);
        if (!string.IsNullOrEmpty(detail))
            parts.Add(detail);
        string rarity = Rarity(item.Rarity);
        if (!string.IsNullOrEmpty(rarity))
            parts.Add(rarity);
        return string.Join(SEPARATOR, parts);
    }

    public static string Category(ItemCategory category) => category switch
    {
        ItemCategory.Resource => "Ресурс",
        ItemCategory.Consumable => "Расходник",
        ItemCategory.Food => "Еда",
        ItemCategory.Drink => "Напиток",
        ItemCategory.Medicine => "Медицина",
        ItemCategory.Ammo => "Боеприпасы",
        ItemCategory.Weapon => "Оружие",
        ItemCategory.Tool => "Инструмент",
        ItemCategory.Clothing => "Одежда",
        ItemCategory.Building => "Строительство",
        _ => "Разное"
    };

    public static string Rarity(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Faulty => "Брак",
        ItemRarity.Poor => "Низкое качество",
        ItemRarity.Good => "Хорошее качество",
        ItemRarity.Great => "Отличное качество",
        ItemRarity.Superior => "Превосходное качество",
        ItemRarity.Excellent => "Шедевр",
        _ => null
    };

    private static string Detail(InventoryItem item) => item switch
    {
        EquipmentInventoryItem equipment => Slot(equipment.EquipmentSlot),
        ResourceInventoryItem resource => Resource(resource.ResourceCategory),
        _ => null
    };

    private static string Slot(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Headwear => "Головной убор",
        EquipmentSlot.Face => "Лицо",
        EquipmentSlot.TorsoBase => "Нательное",
        EquipmentSlot.TorsoOuter => "Верхняя одежда",
        EquipmentSlot.Backpack => "Рюкзак",
        EquipmentSlot.Hands => "Перчатки",
        EquipmentSlot.Legs => "Штаны",
        EquipmentSlot.Socks => "Носки",
        EquipmentSlot.Footwear => "Обувь",
        _ => null
    };

    private static string Resource(ResourceCategory category) => category switch
    {
        ResourceCategory.Raw => "Сырьё",
        ResourceCategory.Processed => "Обработанное",
        ResourceCategory.Component => "Деталь",
        _ => null
    };
}

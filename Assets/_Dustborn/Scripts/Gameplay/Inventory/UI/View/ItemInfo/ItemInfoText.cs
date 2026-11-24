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
        ItemCategory.Resource => LocalizationService.Text("inventory.category.resource"),
        ItemCategory.Consumable => LocalizationService.Text("inventory.category.consumable"),
        ItemCategory.Food => LocalizationService.Text("inventory.category.food"),
        ItemCategory.Drink => LocalizationService.Text("inventory.category.drink"),
        ItemCategory.Medicine => LocalizationService.Text("inventory.category.medicine"),
        ItemCategory.Ammo => LocalizationService.Text("inventory.category.ammo"),
        ItemCategory.Weapon => LocalizationService.Text("inventory.category.weapon"),
        ItemCategory.Tool => LocalizationService.Text("inventory.category.tool"),
        ItemCategory.Clothing => LocalizationService.Text("inventory.category.clothing"),
        ItemCategory.Building => LocalizationService.Text("inventory.category.building"),
        _ => LocalizationService.Text("inventory.category.misc")
    };

    public static string Rarity(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Faulty => LocalizationService.Text("inventory.rarity.faulty"),
        ItemRarity.Poor => LocalizationService.Text("inventory.rarity.poor"),
        ItemRarity.Good => LocalizationService.Text("inventory.rarity.good"),
        ItemRarity.Great => LocalizationService.Text("inventory.rarity.great"),
        ItemRarity.Superior => LocalizationService.Text("inventory.rarity.superior"),
        ItemRarity.Excellent => LocalizationService.Text("inventory.rarity.excellent"),
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
        EquipmentSlot.Headwear => LocalizationService.Text("inventory.equipment.headwear"),
        EquipmentSlot.Face => LocalizationService.Text("inventory.equipment.face"),
        EquipmentSlot.TorsoBase => LocalizationService.Text("inventory.equipment.torsoBase"),
        EquipmentSlot.TorsoOuter => LocalizationService.Text("inventory.equipment.torsoOuter"),
        EquipmentSlot.Backpack => LocalizationService.Text("inventory.equipment.backpack"),
        EquipmentSlot.Hands => LocalizationService.Text("inventory.equipment.hands"),
        EquipmentSlot.Legs => LocalizationService.Text("inventory.equipment.legs"),
        EquipmentSlot.Socks => LocalizationService.Text("inventory.equipment.socks"),
        EquipmentSlot.Footwear => LocalizationService.Text("inventory.equipment.footwear"),
        _ => null
    };

    private static string Resource(ResourceCategory category) => category switch
    {
        ResourceCategory.Raw => LocalizationService.Text("inventory.resource.raw"),
        ResourceCategory.Processed => LocalizationService.Text("inventory.resource.processed"),
        ResourceCategory.Component => LocalizationService.Text("inventory.resource.component"),
        _ => null
    };
}

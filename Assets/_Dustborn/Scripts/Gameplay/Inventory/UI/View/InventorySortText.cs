public static class InventorySortText
{
    public static string Label(InventorySortMode mode) => mode switch
    {
        InventorySortMode.Name => LocalizationService.Text("inventory.sort.name"),
        InventorySortMode.Amount => LocalizationService.Text("inventory.sort.amount"),
        InventorySortMode.Weight => LocalizationService.Text("inventory.sort.weight"),
        _ => LocalizationService.Text("inventory.sort.category")
    };
}

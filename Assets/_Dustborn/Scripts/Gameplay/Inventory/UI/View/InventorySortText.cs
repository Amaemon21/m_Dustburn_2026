public static class InventorySortText
{
    public static string Label(InventorySortMode mode) => mode switch
    {
        InventorySortMode.Name => "По названию",
        InventorySortMode.Amount => "По количеству",
        InventorySortMode.Weight => "По весу",
        _ => "По типу"
    };
}

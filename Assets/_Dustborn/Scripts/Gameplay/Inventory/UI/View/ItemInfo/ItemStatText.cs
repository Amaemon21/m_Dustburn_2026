using System.Globalization;

public enum ItemStatMood
{
    Neutral,
    Good,
    Bad
}

public static class ItemStatText
{
    private const string NUMBER = "0.##";

    private static readonly NumberFormatInfo FORMAT = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NegativeSign = "−"
    };

    public static string Label(in ItemStat stat) => stat.Type switch
    {
        ItemStatType.Health => LocalizationService.Text("inventory.stats.health"),
        ItemStatType.Bleeding => LocalizationService.Text("inventory.stats.bleeding"),
        ItemStatType.Duration => LocalizationService.Text("inventory.stats.duration"),
        ItemStatType.Weight => LocalizationService.Text("inventory.stats.weight"),
        ItemStatType.Durability => LocalizationService.Text("inventory.stats.durability"),
        ItemStatType.Stamina => LocalizationService.Text("inventory.stats.stamina"),
        ItemStatType.Food => LocalizationService.Text("inventory.stats.food"),
        ItemStatType.Water => LocalizationService.Text("inventory.stats.water"),
        ItemStatType.Infection => LocalizationService.Text("inventory.stats.infection"),
        ItemStatType.Amount => LocalizationService.Text("inventory.stats.amount"),
        ItemStatType.Damage => LocalizationService.Text("inventory.stats.damage"),
        ItemStatType.SwingTime => LocalizationService.Text("inventory.stats.swingTime"),
        ItemStatType.StaminaCost => LocalizationService.Text("inventory.stats.staminaCost"),
        ItemStatType.Reach => LocalizationService.Text("inventory.stats.reach"),
        ItemStatType.ExtraRows => LocalizationService.Text("inventory.stats.extraRows"),
        ItemStatType.CarryBonus => LocalizationService.Text("inventory.stats.carryBonus"),
        ItemStatType.Protection => LocalizationService.Text("inventory.stats.protection"),
        ItemStatType.ProtectionDuration => LocalizationService.Text("inventory.stats.protectionDuration"),
        _ => stat.Type.ToString()
    };

    public static string Value(in ItemStat stat) => stat.Type switch
    {
        ItemStatType.Health or ItemStatType.Stamina or ItemStatType.Food or ItemStatType.Water
            or ItemStatType.Infection or ItemStatType.Bleeding or ItemStatType.ExtraRows => Signed(stat.Value),
        ItemStatType.Duration or ItemStatType.SwingTime or ItemStatType.ProtectionDuration => LocalizationService.Format("units.seconds", Number(stat.Value)),
        ItemStatType.Weight => stat.Limit > 0f ? LocalizationService.Format("inventory.stats.weightPerItem", Number(stat.Value), Number(stat.Limit)) : LocalizationService.Format("units.kilograms", Number(stat.Value)),
        ItemStatType.Durability or ItemStatType.Amount => $"{Number(stat.Value)} / {Number(stat.Limit)}",
        ItemStatType.Reach => LocalizationService.Format("units.metres", Number(stat.Value)),
        ItemStatType.CarryBonus => LocalizationService.Format("units.kilograms", Signed(stat.Value)),
        ItemStatType.Protection => $"{Number(stat.Value)}%",
        _ => Number(stat.Value)
    };

    public static ItemStatMood Mood(in ItemStat stat) => stat.Type switch
    {
        ItemStatType.Health or ItemStatType.Stamina or ItemStatType.Food or ItemStatType.Water
            or ItemStatType.ExtraRows or ItemStatType.CarryBonus or ItemStatType.Protection => Sign(stat.Value),
        ItemStatType.Infection or ItemStatType.Bleeding => Sign(-stat.Value),
        _ => ItemStatMood.Neutral
    };

    public static string Number(float value) => value.ToString(NUMBER, FORMAT);

    private static string Signed(float value) => value > 0f ? "+" + Number(value) : Number(value);

    private static ItemStatMood Sign(float value)
    {
        if (System.Math.Abs(value) < 0.0001f)
            return ItemStatMood.Neutral;
        return value > 0f ? ItemStatMood.Good : ItemStatMood.Bad;
    }
}

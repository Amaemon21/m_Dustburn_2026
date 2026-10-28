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
        ItemStatType.Health => "Здоровье",
        ItemStatType.Bleeding => "Кровотечение",
        ItemStatType.Duration => "Длительность",
        ItemStatType.Weight => "Вес",
        ItemStatType.Durability => "Прочность",
        ItemStatType.Stamina => "Выносливость",
        ItemStatType.Food => "Сытость",
        ItemStatType.Water => "Вода",
        ItemStatType.Infection => "Инфекция",
        ItemStatType.Amount => "Количество",
        ItemStatType.Damage => "Урон",
        ItemStatType.SwingTime => "Время удара",
        ItemStatType.StaminaCost => "Выносливость за удар",
        ItemStatType.Reach => "Дальность",
        ItemStatType.ExtraRows => "Ряды рюкзака",
        ItemStatType.CarryBonus => "Грузоподъёмность",
        _ => stat.Type.ToString()
    };

    public static string Value(in ItemStat stat) => stat.Type switch
    {
        ItemStatType.Health or ItemStatType.Stamina or ItemStatType.Food or ItemStatType.Water
            or ItemStatType.Infection or ItemStatType.Bleeding or ItemStatType.ExtraRows => Signed(stat.Value),
        ItemStatType.Duration or ItemStatType.SwingTime => $"{Number(stat.Value)} с",
        ItemStatType.Weight => stat.Limit > 0f ? $"{Number(stat.Value)} кг ({Number(stat.Limit)} за шт.)" : $"{Number(stat.Value)} кг",
        ItemStatType.Durability or ItemStatType.Amount => $"{Number(stat.Value)} / {Number(stat.Limit)}",
        ItemStatType.Reach => $"{Number(stat.Value)} м",
        ItemStatType.CarryBonus => $"{Signed(stat.Value)} кг",
        _ => Number(stat.Value)
    };

    public static ItemStatMood Mood(in ItemStat stat) => stat.Type switch
    {
        ItemStatType.Health or ItemStatType.Stamina or ItemStatType.Food or ItemStatType.Water
            or ItemStatType.ExtraRows or ItemStatType.CarryBonus => Sign(stat.Value),
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

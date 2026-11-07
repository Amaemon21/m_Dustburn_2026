using System.Globalization;
using UnityEngine;

public static class MainMenuText
{
    private const string DATE_FORMAT = "dd.MM.yyyy HH:mm";
    private const float METRES_PER_KM = 1024f;
    private static readonly NumberFormatInfo DECIMALS = new() { NumberDecimalSeparator = "," };

    public static readonly string[] AMOUNTS = { "\u041d\u0435\u0442", "\u041c\u0430\u043b\u043e", "\u041d\u043e\u0440\u043c\u0430", "\u041c\u043d\u043e\u0433\u043e" };
    public static readonly string[] LAYOUTS = { "\u0421\u043b\u0443\u0447\u0430\u0439\u043d\u043e\u0435", "\u041b\u0435\u0441 \u0432 \u0446\u0435\u043d\u0442\u0440\u0435", "\u041f\u0443\u0441\u0442\u043e\u0448\u044c \u0432 \u0446\u0435\u043d\u0442\u0440\u0435", "\u041a\u043e\u043b\u044c\u0446\u0430", "\u041f\u043e\u043b\u043e\u0441\u044b" };
    public static readonly string[] PREVIEW_QUALITIES = { "\u041d\u0438\u0437\u043a\u043e\u0435", "\u0421\u0440\u0435\u0434\u043d\u0435\u0435", "\u0412\u044b\u0441\u043e\u043a\u043e\u0435" };

    public static string SlotProblem(GameSlotInfo slot)
        => slot.Status == GameSlotStatus.MissingWorld
            ? "\u041a\u0430\u0440\u0442\u0430 \u043e\u0442\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442. \u0412\u043e\u0441\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u0435 \u0438\u0441\u0445\u043e\u0434\u043d\u0443\u044e \u043a\u0430\u0440\u0442\u0443 \u0438\u043b\u0438 \u0443\u0434\u0430\u043b\u0438\u0442\u0435 \u044d\u0442\u043e \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0435."
            : "\u0421\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435 \u043f\u043e\u0432\u0440\u0435\u0436\u0434\u0435\u043d\u043e. \u0412\u043e\u0441\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u0435 \u0440\u0435\u0437\u0435\u0440\u0432\u043d\u0443\u044e \u043a\u043e\u043f\u0438\u044e \u0438\u043b\u0438 \u0443\u0434\u0430\u043b\u0438\u0442\u0435 \u044d\u0442\u043e \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0435.";

    public static string SlotDetails(GameSlotInfo slot)
    {
        string played = slot.LastPlayed.ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        string world = string.IsNullOrEmpty(slot.WorldName) ? string.Empty : $" · {slot.WorldName}";
        string size = slot.WorldSize > 0 ? $" · {Kilometres(slot.WorldSize)}" : string.Empty;
        string status = slot.CanLoad ? string.Empty : slot.Status == GameSlotStatus.MissingWorld
            ? "\n<color=#C89B70>\u041a\u0430\u0440\u0442\u0430 \u043e\u0442\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442</color>" : "\n<color=#C89B70>\u0421\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435 \u043f\u043e\u0432\u0440\u0435\u0436\u0434\u0435\u043d\u043e</color>";
        return $"{played}{world}{size}{status}";
    }

    public static string SlotDescription(GameSlotInfo slot)
    {
        if (slot == null)
            return "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0435 \u0434\u043b\u044f \u0437\u0430\u0433\u0440\u0443\u0437\u043a\u0438.";
        string world = string.IsNullOrEmpty(slot.WorldName) ? "\u041d\u0435\u0438\u0437\u0432\u0435\u0441\u0442\u043d\u0430" : slot.WorldName;
        string availability = slot.CanLoad ? "\u041c\u043e\u0436\u043d\u043e \u043f\u0440\u043e\u0434\u043e\u043b\u0436\u0438\u0442\u044c \u0438\u0433\u0440\u0443." : SlotProblem(slot);
        return $"\u041a\u0430\u0440\u0442\u0430: {world}\n\u041f\u043e\u0441\u043b\u0435\u0434\u043d\u0435\u0435 \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435: {slot.LastPlayed.ToString(DATE_FORMAT, CultureInfo.InvariantCulture)}\n\n{availability}";
    }

    public static string SlotSummary(GameSlotInfo slot)
    {
        if (slot == null)
            return string.Empty;

        string played = slot.LastPlayed.ToString("dd.MM.yyyy '·' HH:mm", CultureInfo.InvariantCulture);
        string world = slot.WorldSize > 0
            ? $"\u041c\u0438\u0440 {Kilometres(slot.WorldSize)} · "
            : string.Empty;
        string name = slot.Name.Replace("<", "\u003c\u200b");

        return "<color=#929695><size=55%>—   <cspace=0.12em>\u041f\u041e\u0421\u041b\u0415\u0414\u041d\u0415\u0415 \u0421\u041e\u0425\u0420\u0410\u041d\u0415\u041d\u0418\u0415</cspace></size></color>\n"
            + $"<indent=28><color=#D9D7CF>{name}</color>\n"
            + $"<size=80%><color=#A9ACAA>{played}\n{world}\u041e\u0434\u0438\u043d\u043e\u0447\u043d\u0430\u044f \u0438\u0433\u0440\u0430</color></size></indent>";
    }

    public static string MapMetadata(WorldInfo world)
    {
        if (world == null)
            return string.Empty;
        int side = Mathf.RoundToInt(world.WorldSize / METRES_PER_KM);
        return $"{side} × {side} \u043a\u043c   ·   \u0421\u0438\u0434 {world.Seed}";
    }

    public static string MapRelief(WorldInfo world)
    {
        WorldVariation variation = world?.Variation;
        return variation == null ? "\u041d\u0435\u0442 \u0434\u0430\u043d\u043d\u044b\u0445 \u043e \u0440\u0435\u043b\u044c\u0435\u0444\u0435" : $"\u0420\u0430\u0432\u043d\u0438\u043d\u044b  {variation.Plains}%    |    \u0425\u043e\u043b\u043c\u044b  {variation.Hills}%    |    \u0413\u043e\u0440\u044b  {variation.Mountains}%";
    }

    public static string MapStats(WorldInfo world)
    {
        WorldStats stats = world?.Stats;
        return stats == null ? "\u041d\u0435\u0442 \u0441\u0442\u0430\u0442\u0438\u0441\u0442\u0438\u043a\u0438 \u0433\u0435\u043d\u0435\u0440\u0430\u0446\u0438\u0438" : $"\u041f\u043e\u0441\u0435\u043b\u0435\u043d\u0438\u044f  {stats.Settlements}    |    \u0417\u0434\u0430\u043d\u0438\u044f  {stats.Buildings}    |    \u0420\u0435\u043a\u0438  {stats.Rivers}    |    \u0412\u043e\u0434\u043e\u0451\u043c\u044b  {stats.Lakes}";
    }

    public static string MapFooter(WorldInfo world)
    {
        WorldStats stats = world?.Stats;
        return stats == null ? string.Empty
            : $"\u0414\u043e\u0440\u043e\u0433\u0438  {Number(stats.HighwayKm + stats.StreetKm + stats.DirtKm)} \u043a\u043c    ·    \u0413\u0435\u043d\u0435\u0440\u0430\u0446\u0438\u044f  {Number(stats.Seconds)} \u0441";
    }

    public static string WorldDetails(WorldInfo world)
    {
        if (world.IsSceneWorld)
            return "BakedWorld \u0438\u0437 \u0438\u0433\u0440\u043e\u0432\u043e\u0439 \u0441\u0446\u0435\u043d\u044b";

        string origin = world.BuiltIn ? "\u0432\u0441\u0442\u0440\u043e\u0435\u043d\u043d\u044b\u0439" : "\u0441\u0433\u0435\u043d\u0435\u0440\u0438\u0440\u043e\u0432\u0430\u043d\u043d\u044b\u0439";
        return $"{Kilometres(world.WorldSize)} · {origin}\n\u0421\u0438\u0434: {world.Seed}";
    }

    public static string StatsSummary(WorldStats stats)
    {
        return $"\u041f\u043e\u0441\u0435\u043b\u0435\u043d\u0438\u0439: {stats.Settlements} (\u0433\u043e\u0440\u043e\u0434\u0430 {stats.Cities}, \u043f\u043e\u0441\u0451\u043b\u043a\u0438 {stats.Towns}, \u0434\u0435\u0440\u0435\u0432\u043d\u0438 {stats.CountryTowns}, \u0437\u0430\u0431\u0440\u043e\u0448\u0435\u043d\u043d\u044b\u0435 {stats.GhostTowns})\n"
            + $"\u0417\u0434\u0430\u043d\u0438\u0439: {stats.Buildings}, \u0438\u0437 \u043d\u0438\u0445 \u0432 \u0433\u043b\u0443\u0448\u0438 {stats.WildernessBuildings} · \u0420\u0435\u043a\u0432\u0438\u0437\u0438\u0442: {stats.Props}\n"
            + $"\u0414\u043e\u0440\u043e\u0433\u0438: \u0448\u043e\u0441\u0441\u0435 {Number(stats.HighwayKm)} \u043a\u043c, \u0443\u043b\u0438\u0446\u044b {Number(stats.StreetKm)} \u043a\u043c, \u0433\u0440\u0443\u043d\u0442\u043e\u0432\u043a\u0438 {Number(stats.DirtKm)} \u043a\u043c\n"
            + $"\u0420\u0435\u043a\u0438: {stats.Rivers} ({Number(stats.RiverKm)} \u043a\u043c) · \u0412\u043e\u0434\u043e\u0451\u043c\u044b: {stats.Lakes}\n"
            + $"\u041a\u0440\u0430\u0442\u0435\u0440\u044b: {stats.Craters} · \u0420\u0430\u0437\u043b\u043e\u043c\u044b: {stats.Rifts} · \u041a\u0430\u043d\u044c\u043e\u043d\u044b: {stats.Canyons}"
            + (stats.Seconds > 0f ? $" · \u0413\u0435\u043d\u0435\u0440\u0430\u0446\u0438\u044f: {Number(stats.Seconds)} \u0441" : string.Empty);
    }

    public static string Amount(WorldAmount amount) => AMOUNTS[Mathf.Clamp((int)amount, 0, AMOUNTS.Length - 1)];

    public static string Layout(BiomeLayout layout) => LAYOUTS[Mathf.Clamp((int)layout, 0, LAYOUTS.Length - 1)].ToLowerInvariant();

    public static string Biome(BiomeType type)
    {
        return type switch
        {
            BiomeType.PineForest => "\u0421\u043e\u0441\u043d\u043e\u0432\u044b\u0439 \u043b\u0435\u0441",
            BiomeType.BurntForest => "\u0412\u044b\u0436\u0436\u0435\u043d\u043d\u044b\u0439 \u043b\u0435\u0441",
            BiomeType.Desert => "\u041f\u0443\u0441\u0442\u044b\u043d\u044f",
            BiomeType.Snow => "\u0421\u043d\u0435\u0433\u0430",
            _ => "\u041f\u0443\u0441\u0442\u043e\u0448\u044c"
        };
    }

    public static string Size(int metres) => $"{Kilometres(metres)} ({metres} \u043c)";

    public static string Kilometres(int metres) => $"{Mathf.RoundToInt(metres / METRES_PER_KM)} \u043a\u043c";

    private static string Number(float value) => value.ToString("0.#", DECIMALS);
}

using System;
using System.Globalization;
using UnityEngine;

public static class MainMenuText
{
    private const string DATE_FORMAT = "dd.MM.yyyy HH:mm";
    private const float METRES_PER_KM = 1024f;
    private static readonly NumberFormatInfo DECIMALS = new() { NumberDecimalSeparator = "," };

    private static readonly string[] AMOUNT_KEYS =
    {
        "menu.options.amountNone",
        "menu.options.amountFew",
        "menu.options.amountNormal",
        "menu.options.amountMany"
    };

    private static readonly string[] LAYOUT_KEYS =
    {
        "menu.options.layoutRandom",
        "menu.options.layoutForestCenter",
        "menu.options.layoutWastelandCenter",
        "menu.options.layoutRings",
        "menu.options.layoutStripes"
    };

    private static readonly string[] PREVIEW_QUALITY_KEYS =
    {
        "menu.options.qualityLow",
        "menu.options.qualityMedium",
        "menu.options.qualityHigh"
    };

    public static string[] AmountOptions => Array.ConvertAll(AMOUNT_KEYS, LocalizationService.Text);
    public static string[] LayoutOptions => Array.ConvertAll(LAYOUT_KEYS, LocalizationService.Text);
    public static string[] PreviewQualityOptions => Array.ConvertAll(PREVIEW_QUALITY_KEYS, LocalizationService.Text);

    public static string SlotProblem(GameSlotInfo slot)
        => slot.Status == GameSlotStatus.MissingWorld
            ? LocalizationService.Text("menu.saves.missingMapHelp")
            : LocalizationService.Text("menu.saves.corruptHelp");

    public static string SlotDetails(GameSlotInfo slot)
    {
        string played = slot.LastPlayed.ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        string world = string.IsNullOrEmpty(slot.WorldName) ? string.Empty : $" · {slot.WorldName}";
        string size = slot.WorldSize > 0 ? $" · {Kilometres(slot.WorldSize)}" : string.Empty;
        string status = slot.CanLoad ? string.Empty : slot.Status == GameSlotStatus.MissingWorld
            ? LocalizationService.Text("menu.saves.missingMapStatus") : LocalizationService.Text("menu.saves.corruptStatus");
        return $"{played}{world}{size}{status}";
    }

    public static string SlotSummary(GameSlotInfo slot)
    {
        if (slot == null)
            return string.Empty;

        string played = slot.LastPlayed.ToString("dd.MM.yyyy '·' HH:mm", CultureInfo.InvariantCulture);
        string world = slot.WorldSize > 0
            ? LocalizationService.Format("menu.saves.worldPrefix", Kilometres(slot.WorldSize))
            : string.Empty;
        string name = slot.Name.Replace("<", "<" + (char)0x200B);

        return LocalizationService.Text("menu.saves.latestTitle")
            + $"<indent=28><color=#D9D7CF>{name}</color>\n"
            + LocalizationService.Format("menu.saves.singlePlayerSummary", played, world);
    }

    public static string MapMetadata(WorldInfo world)
    {
        if (world == null)
            return string.Empty;
        int side = Mathf.RoundToInt(world.WorldSize / METRES_PER_KM);
        return LocalizationService.Format("menu.maps.metadata", side, side, world.Seed);
    }

    public static string MapRelief(WorldInfo world)
    {
        WorldVariation variation = world?.Variation;
        return variation == null ? LocalizationService.Text("menu.maps.noRelief") : LocalizationService.Format("menu.maps.relief", variation.Plains, variation.Hills, variation.Mountains);
    }

    public static string MapStats(WorldInfo world)
    {
        WorldStats stats = world?.Stats;
        return stats == null ? LocalizationService.Text("menu.maps.noStats") : LocalizationService.Format("menu.maps.stats", stats.Settlements, stats.Buildings, stats.Rivers, stats.Lakes);
    }

    public static string MapFooter(WorldInfo world)
    {
        WorldStats stats = world?.Stats;
        return stats == null ? string.Empty
            : LocalizationService.Format("menu.maps.footer", Number(stats.HighwayKm + stats.StreetKm + stats.DirtKm), Number(stats.Seconds));
    }

    public static string WorldDetails(WorldInfo world)
    {
        if (world.IsSceneWorld)
            return LocalizationService.Text("menu.maps.sceneWorld");

        string origin = world.BuiltIn ? LocalizationService.Text("menu.maps.originBuiltIn") : LocalizationService.Text("menu.maps.originGenerated");
        return LocalizationService.Format("menu.maps.details", Kilometres(world.WorldSize), origin, world.Seed);
    }

    public static string StatsSummary(WorldStats stats)
    {
        return LocalizationService.Format("menu.maps.settlementSummary", stats.Settlements, stats.Cities, stats.Towns, stats.CountryTowns, stats.GhostTowns)
            + LocalizationService.Format("menu.maps.buildingSummary", stats.Buildings, stats.WildernessBuildings, stats.Props)
            + LocalizationService.Format("menu.maps.roadSummary", Number(stats.HighwayKm), Number(stats.StreetKm), Number(stats.DirtKm))
            + LocalizationService.Format("menu.maps.waterSummary", stats.Rivers, Number(stats.RiverKm), stats.Lakes)
            + LocalizationService.Format("menu.maps.landformSummary", stats.Craters, stats.Rifts, stats.Canyons)
            + (stats.Seconds > 0f ? LocalizationService.Format("menu.maps.generationTime", Number(stats.Seconds)) : string.Empty);
    }

    public static string Amount(WorldAmount amount)
    {
        int index = Mathf.Clamp((int)amount, 0, AMOUNT_KEYS.Length - 1);
        return LocalizationService.Text(AMOUNT_KEYS[index]);
    }

    public static string Layout(BiomeLayout layout)
    {
        int index = Mathf.Clamp((int)layout, 0, LAYOUT_KEYS.Length - 1);
        return LocalizationService.Text(LAYOUT_KEYS[index]).ToLowerInvariant();
    }

    public static string Biome(BiomeType type)
    {
        return type switch
        {
            BiomeType.PineForest => LocalizationService.Text("biome.pineForest"),
            BiomeType.BurntForest => LocalizationService.Text("biome.burntForest"),
            BiomeType.Desert => LocalizationService.Text("biome.desert"),
            BiomeType.Snow => LocalizationService.Text("biome.snow"),
            _ => LocalizationService.Text("biome.wasteland")
        };
    }

    public static string Size(int metres) => LocalizationService.Format("menu.maps.size", Kilometres(metres), metres);

    public static string Kilometres(int metres) => LocalizationService.Format("units.kilometres", Mathf.RoundToInt(metres / METRES_PER_KM));

    private static string Number(float value) => value.ToString("0.#", DECIMALS);
}

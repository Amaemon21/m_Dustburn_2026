using UnityEngine;

public static class MainMenuText
{
    private const string DATE_FORMAT = "dd.MM.yyyy HH:mm";
    private const float METRES_PER_KM = 1024f;

    public static string SlotDetails(GameSlotInfo slot)
    {
        string played = slot.LastPlayed.ToString(DATE_FORMAT);
        if (!string.IsNullOrEmpty(slot.WorldName))
            played += $" · {slot.WorldName}";

        if (slot.WorldSize <= 0)
            return played;

        return $"{played} · {Kilometres(slot.WorldSize)} · сид {slot.Seed}";
    }

    public static string SlotSummary(GameSlotInfo slot) => slot == null ? string.Empty : $"{slot.Name}\n{SlotDetails(slot)}";

    public static string WorldDetails(WorldInfo world)
    {
        if (world.IsSceneWorld)
            return "BakedWorld из игровой сцены";

        string origin = world.BuiltIn ? "встроенный" : "сгенерированный";
        return $"{Kilometres(world.WorldSize)} · сид {world.Seed} · {origin}";
    }

    private static string Kilometres(int metres) => $"{Mathf.RoundToInt(metres / METRES_PER_KM)} км";
}

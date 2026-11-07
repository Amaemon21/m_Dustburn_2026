using System.Collections.Generic;

public static class GameSlotAvailability
{
    public static GameSlotInfo LatestAvailable(IReadOnlyList<GameSlotInfo> slots)
    {
        GameSlotInfo latest = null;
        foreach (GameSlotInfo slot in slots)
        {
            if (slot.CanLoad && (latest == null || slot.LastPlayed > latest.LastPlayed))
                latest = slot;
        }
        return latest;
    }
}

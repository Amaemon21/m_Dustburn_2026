using System.Collections.Generic;

public static class WorldDeletionPolicy
{
    public static int UsageCount(string worldId, IReadOnlyList<GameSlotInfo> slots)
    {
        int count = 0;
        foreach (GameSlotInfo slot in slots)
        {
            if (slot.WorldId == worldId)
                count++;
        }
        return count;
    }

    public static void EnsureUnused(string worldId, IReadOnlyList<GameSlotInfo> slots)
    {
        foreach (GameSlotInfo slot in slots)
        {
            if (slot.Status == GameSlotStatus.Unreadable)
                throw new WorldDeletionException(worldId, UsageCount(worldId, slots), true);
        }

        int count = UsageCount(worldId, slots);
        if (count > 0)
            throw new WorldDeletionException(worldId, count, false);
    }
}

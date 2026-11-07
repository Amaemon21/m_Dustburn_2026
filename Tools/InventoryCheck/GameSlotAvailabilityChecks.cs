using System;

internal static partial class Harness
{
    private static void VerifyGameSlotAvailability()
    {
        var first = new GameSlotInfo("first", "First", new DateTime(2026, 1, 1), "Map", 17, 8192, "map_a");
        var latest = new GameSlotInfo("latest", "Latest", new DateTime(2026, 2, 1), "Map", 17, 8192, "map_a");
        var missing = latest.WithWorldAvailable(false);
        Check(!missing.CanLoad && missing.Status == GameSlotStatus.MissingWorld, "a missing map disables an existing save");
        Check(latest.CanLoad && latest.Status == GameSlotStatus.Available, "availability updates do not mutate existing snapshots");
        Check(missing.Id == latest.Id && missing.WorldId == latest.WorldId && missing.LastPlayed == latest.LastPlayed
            && missing.Name == latest.Name && missing.Seed == latest.Seed && missing.WorldSize == latest.WorldSize,
            "missing-map saves preserve their metadata for display and deletion");
        Check(missing.WithWorldAvailable(true).CanLoad, "restoring a map makes its save loadable again");
        var unreadable = new GameSlotInfo("broken", "Broken", DateTime.MaxValue, "", 0, 0, "", GameSlotStatus.Unreadable);
        Check(!unreadable.WithWorldAvailable(true).CanLoad, "a readable map cannot repair an unreadable save");
        Check(GameSlotAvailability.LatestAvailable(new[] { missing, first, unreadable }) == first,
            "continue skips missing-map and damaged saves");
        Check(GameSlotAvailability.LatestAvailable(new[] { first, latest }) == latest,
            "continue chooses the newest available save independently of input order");
        Check(GameSlotAvailability.LatestAvailable(new[] { missing, unreadable }) == null,
            "continue is unavailable when every save is broken");
        Check(GameSlotAvailability.LatestAvailable(Array.Empty<GameSlotInfo>()) == null,
            "continue is unavailable before the first playthrough");
        Check(WorldDeletionPolicy.UsageCount("map_a", new[] { first, latest }) == 2,
            "map usage includes every playthrough on the map");
        Expect<InvalidOperationException>(() => WorldDeletionPolicy.EnsureUnused("map_a", new[] { first }),
            "the service policy rejects deletion of a used map");
        Expect<InvalidOperationException>(() => WorldDeletionPolicy.EnsureUnused("map_a", new[] { missing }),
            "missing-map saves still reserve their original map identity");
        WorldDeletionPolicy.EnsureUnused("map_b", new[] { first, latest });
        Check(true, "unrelated playthroughs do not block deleting an unused map");
        WorldDeletionPolicy.EnsureUnused("map_a", Array.Empty<GameSlotInfo>());
        Check(true, "a map can be removed after its last playthrough is deleted");
        Expect<InvalidOperationException>(() => WorldDeletionPolicy.EnsureUnused("map_b", new[] { unreadable }),
            "unreadable save metadata prevents unchecked map deletion");
    }
}

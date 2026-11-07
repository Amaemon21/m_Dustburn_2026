using System;

public sealed class WorldDeletionException : InvalidOperationException
{
    public int SaveCount { get; }
    public bool HasUnreadableSaves { get; }

    public WorldDeletionException(string worldId, int saveCount, bool hasUnreadableSaves)
        : base($"Cannot delete world '{worldId}': {saveCount} dependent saves, unreadable metadata: {hasUnreadableSaves}")
    {
        SaveCount = saveCount;
        HasUnreadableSaves = hasUnreadableSaves;
    }
}

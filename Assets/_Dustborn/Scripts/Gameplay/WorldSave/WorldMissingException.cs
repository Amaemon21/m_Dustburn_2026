using System;

public sealed class WorldMissingException : InvalidOperationException
{
    public string WorldId { get; }

    public WorldMissingException(string worldId)
        : base($"Save slot refers to world '{worldId}', which is neither shipped with the game nor generated on this machine")
    {
        WorldId = worldId;
    }
}

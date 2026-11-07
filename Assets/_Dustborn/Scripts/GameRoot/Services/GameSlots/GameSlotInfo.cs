using System;

public sealed class GameSlotInfo
{
    public string WorldId { get; }
    public GameSlotStatus Status { get; }
    public bool CanLoad => Status == GameSlotStatus.Available;

    public GameSlotInfo WithWorldAvailable(bool available)
        => new(Id, Name, LastPlayed, WorldName, Seed, WorldSize, WorldId,
            Status == GameSlotStatus.Unreadable ? Status : available ? GameSlotStatus.Available : GameSlotStatus.MissingWorld);

    public string Id { get; }
    public string Name { get; }
    public DateTime LastPlayed { get; }
    public string WorldName { get; }
    public int Seed { get; }
    public int WorldSize { get; }

    public GameSlotInfo(string id, string name, DateTime lastPlayed, string worldName, int seed, int worldSize, string worldId = "", GameSlotStatus status = GameSlotStatus.Available)
    {
        WorldId = worldId ?? string.Empty;
        Status = status;
        Id = id;
        Name = name;
        LastPlayed = lastPlayed;
        WorldName = worldName;
        Seed = seed;
        WorldSize = worldSize;
    }
}

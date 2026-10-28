using System;

public sealed class GameSlotInfo
{
    public string Id { get; }
    public string Name { get; }
    public DateTime LastPlayed { get; }
    public string WorldName { get; }
    public int Seed { get; }
    public int WorldSize { get; }

    public GameSlotInfo(string id, string name, DateTime lastPlayed, string worldName, int seed, int worldSize)
    {
        Id = id;
        Name = name;
        LastPlayed = lastPlayed;
        WorldName = worldName;
        Seed = seed;
        WorldSize = worldSize;
    }
}

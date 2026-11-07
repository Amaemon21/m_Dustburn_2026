public sealed class WorldInfo
{
    public const string SCENE_WORLD_ID = "";

    public string Id { get; }
    public string Name { get; }
    public int Seed { get; }
    public int WorldSize { get; }
    public bool BuiltIn { get; }
    public string Directory { get; }
    public WorldVariation Variation { get; }
    public WorldStats Stats { get; }
    public long CreatedUnixTime { get; }
    public bool IsSceneWorld => Id == SCENE_WORLD_ID;
    public string ThumbnailPath => Directory == null ? null : System.IO.Path.Combine(Directory, "thumbnail.png");
    public string PreviewPath => Directory == null ? null : System.IO.Path.Combine(Directory, WorldDiskFormat.PREVIEW_FILE);

    public WorldInfo(string id, string name, int seed, int worldSize, bool builtIn, string directory, WorldVariation variation = null,
        WorldStats stats = null, long createdUnixTime = 0)
    {
        Id = id;
        Name = name;
        Seed = seed;
        WorldSize = worldSize;
        BuiltIn = builtIn;
        Directory = directory;
        Variation = variation;
        Stats = stats;
        CreatedUnixTime = createdUnixTime;
    }

    public static WorldInfo FromManifest(WorldManifest manifest, bool builtIn, string directory)
        => new(manifest.Id, manifest.Name, manifest.Seed, manifest.WorldSize, builtIn, directory,
            manifest.HasVariation ? manifest.Variation : null, manifest.HasVariation ? manifest.Stats : null, manifest.CreatedUnixTime);

    public static WorldInfo SceneWorld(string name) => new(SCENE_WORLD_ID, name, 0, 0, true, null);
}

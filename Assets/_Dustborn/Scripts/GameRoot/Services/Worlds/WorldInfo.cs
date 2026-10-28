public sealed class WorldInfo
{
    public const string SCENE_WORLD_ID = "";

    public string Id { get; }
    public string Name { get; }
    public int Seed { get; }
    public int WorldSize { get; }
    public bool BuiltIn { get; }
    public string Directory { get; }
    public bool IsSceneWorld => Id == SCENE_WORLD_ID;
    public string PreviewPath => Directory == null ? null : System.IO.Path.Combine(Directory, WorldDiskFormat.PREVIEW_FILE);

    public WorldInfo(string id, string name, int seed, int worldSize, bool builtIn, string directory)
    {
        Id = id;
        Name = name;
        Seed = seed;
        WorldSize = worldSize;
        BuiltIn = builtIn;
        Directory = directory;
    }

    public static WorldInfo FromManifest(WorldManifest manifest, bool builtIn, string directory)
        => new(manifest.Id, manifest.Name, manifest.Seed, manifest.WorldSize, builtIn, directory);

    public static WorldInfo SceneWorld(string name) => new(SCENE_WORLD_ID, name, 0, 0, true, null);
}

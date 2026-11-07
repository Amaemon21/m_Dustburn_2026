public sealed class WorldGenerationResult
{
    public WorldInfo World { get; }
    public WorldPreviewSource Preview { get; }
    public WorldPreviewLayers Layers { get; }
    public WorldStats Stats { get; }

    public WorldGenerationResult(WorldInfo world, WorldPreviewSource preview, WorldPreviewLayers layers, WorldStats stats)
    {
        World = world;
        Preview = preview;
        Layers = layers;
        Stats = stats;
    }
}

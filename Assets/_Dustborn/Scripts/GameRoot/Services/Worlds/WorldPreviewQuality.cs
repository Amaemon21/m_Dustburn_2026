public enum WorldPreviewQuality
{
    Low,
    Medium,
    High
}

public static class WorldPreviewQualities
{
    public static int Resolution(WorldPreviewQuality quality)
    {
        return quality switch
        {
            WorldPreviewQuality.Low => 512,
            WorldPreviewQuality.High => 2048,
            _ => 1024
        };
    }
}

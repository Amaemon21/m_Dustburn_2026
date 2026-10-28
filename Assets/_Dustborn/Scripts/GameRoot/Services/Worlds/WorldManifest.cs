using System;

[Serializable]
public sealed class WorldManifest
{
    public const int FORMAT_VERSION = 1;

    public int FormatVersion = FORMAT_VERSION;
    public int GeneratorVersion;
    public string Id;
    public string Name;
    public string Fingerprint;
    public long CreatedUnixTime;
    public int Seed;
    public int WorldSize;
    public int HeightResolution;
    public int BiomeResolution;
    public int RoadMaskResolution;
    public int WaterLength;
    public int ControlResolution;
    public int ControlCount;
    public int LayersCount;
    public int Placements;
    public int Props;
}

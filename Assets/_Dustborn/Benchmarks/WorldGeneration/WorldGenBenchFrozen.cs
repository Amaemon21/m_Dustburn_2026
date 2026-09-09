using System;
using System.Collections.Generic;

public sealed class WorldGenBenchFrozen
{
    public string Stage { get; }

    public BiomeMap Biomes { get; private set; }

    public BiomeWeightField Weights { get; private set; }

    public HeightMap RawHeights { get; private set; }

    public List<Hub> Hubs { get; private set; }

    public RoadNetwork Roads { get; private set; }

    public HeightMap CarvedHeights { get; private set; }

    public float[] RoadMask { get; private set; }

    public List<CityLayout> Cities { get; private set; }

    public HeightMap StreetHeights { get; private set; }

    public RoadProximity Proximity { get; private set; }

    public List<PoiPlacement> Placements { get; private set; }

    public HeightMap FinalHeights { get; private set; }

    private readonly WorldGenBenchProfile _profile;

    public WorldGenBenchFrozen(WorldGenBenchProfile profile, string stage)
    {
        _profile = profile;
        Stage = stage ?? "biomes";
        Build();
    }

    public HeightMap CopyRaw()
    {
        return Copy(RawHeights);
    }

    public HeightMap CopyCarved()
    {
        return Copy(CarvedHeights);
    }

    public HeightMap CopyStreets()
    {
        return Copy(StreetHeights);
    }

    public HeightMap CopyFinal()
    {
        return Copy(FinalHeights);
    }

    public float[] CopyMask()
    {
        return RoadMask == null ? null : (float[])RoadMask.Clone();
    }

    public BiomeMap CopyBiomes()
    {
        var copy = new BiomeMap(Biomes.Resolution, Biomes.WorldSize);
        Array.Copy(Biomes.Cells, copy.Cells, Biomes.Cells.Length);
        return copy;
    }

    public static HeightMap Copy(HeightMap source)
    {
        if (source == null)
            return null;

        var copy = new HeightMap(source.Resolution, source.WorldSize, source.MaxHeight);
        Array.Copy(source.Heights, copy.Heights, source.Heights.Length);
        return copy;
    }

    private void Build()
    {
        WorldGenerationConfig config = _profile.Config;

        Biomes = new BiomeMapGenerator(config, _profile.Biomes).Generate();

        if (Reached("biomes"))
            return;

        Weights = new BiomeWeightField(Biomes, _profile.Biomes.Count, config.BiomeBlendRadius);

        if (Reached("weights"))
            return;

        RawHeights = new HeightMapGenerator(config, _profile.Biomes).Generate(Biomes);

        if (Reached("heights"))
            return;

        Hubs = new HubPlacer(config, RawHeights).Place();

        if (Reached("hubs"))
            return;

        Roads = new RoadNetwork();
        Roads.Hubs.AddRange(Hubs);
        Roads.Roads.AddRange(new RoadPlanner(config, RawHeights).Plan(Roads.Hubs));

        if (Reached("roads"))
            return;

        var carver = new TerrainCarver(config);
        CarvedHeights = carver.Carve(Copy(RawHeights), Roads, out float[] mask);
        RoadMask = mask;

        if (Reached("carved"))
            return;

        Cities = new CityPlanner(config, _profile.Pois, CarvedHeights).Plan(Roads.Hubs, Roads.Roads);

        foreach (CityLayout city in Cities)
            Roads.Streets.AddRange(city.Streets);

        StreetHeights = carver.CarveStreets(Copy(CarvedHeights), Cities, RoadMask);

        Proximity = new RoadProximity(Roads.Roads, config.WorldSize, config.RoadCellSize);
        Proximity.AddRange(Roads.Streets);

        if (Reached("cities"))
            return;

        var placer = new PoiPlacer(config, _profile.Pois, StreetHeights, Proximity);
        Placements = placer.Place(Cities, Roads);
        FinalHeights = carver.CarvePads(Copy(StreetHeights), Placements);
        placer.ApplyHeights(FinalHeights);
    }

    private bool Reached(string stage)
    {
        return string.Equals(Stage, stage, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;

public sealed class WorldGenBenchFrozen
{
    public string Stage { get; }

    public BiomeMap Biomes { get; private set; }

    public BiomeWeightField Weights { get; private set; }

    public HeightMap RawHeights { get; private set; }

    public List<Hub> Hubs { get; private set; }

    public List<(int From, int To)> Links { get; private set; }

    public List<SettlementLayout> Settlements { get; private set; }

    public HeightMap PaddedHeights { get; private set; }

    public RoadNetwork Roads { get; private set; }

    public HeightMap CarvedHeights { get; private set; }

    public float[] RoadMask { get; private set; }

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

    public HeightMap CopyPadded()
    {
        return Copy(PaddedHeights);
    }

    public HeightMap CopyCarved()
    {
        return Copy(CarvedHeights);
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
        Links = RoadGraph.Link(Hubs, config.RoadExtraEdges);

        if (Reached("hubs"))
            return;

        var planner = new SettlementPlanner(config, _profile.Pois, RawHeights);
        var carver = new TerrainCarver(config);

        Settlements = planner.Plan(Hubs, Links);
        PaddedHeights = carver.CarveSettlements(Copy(RawHeights), Settlements);

        if (Reached("settlements"))
            return;

        Roads = new RoadNetwork();
        Roads.Hubs.AddRange(Hubs);
        Roads.Links.AddRange(Links);

        foreach (SettlementLayout settlement in Settlements)
            Roads.Streets.AddRange(settlement.Streets);

        Roads.Roads.AddRange(new RoadPlanner(config, PaddedHeights).Plan(Hubs, Links, Settlements));

        if (Reached("roads"))
            return;

        HeightMap streets = carver.CarveStreets(Copy(PaddedHeights), Settlements, out float[] mask);

        CarvedHeights = carver.CarveHighways(streets, Roads.Roads, mask);
        RoadMask = mask;

        Proximity = new RoadProximity(Roads.Roads, config.WorldSize, config.RoadCellSize);
        Proximity.AddRange(Roads.Streets);

        if (Reached("carved"))
            return;

        planner.CutLots(Settlements, Proximity);

        if (Reached("cities"))
            return;

        var placer = new PoiPlacer(config, _profile.Pois, CarvedHeights, Proximity);
        Placements = placer.Place(Settlements, Roads);
        FinalHeights = carver.CarvePads(Copy(CarvedHeights), Placements);
        placer.ApplyHeights(FinalHeights);
    }

    private bool Reached(string stage)
    {
        return string.Equals(Stage, stage, StringComparison.OrdinalIgnoreCase);
    }
}

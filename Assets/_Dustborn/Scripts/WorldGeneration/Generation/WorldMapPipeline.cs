using System;
using System.Collections.Generic;
using System.Threading;

public sealed class WorldMapResult
{
    public BiomeMap Biomes { get; }
    public HeightMap Heights { get; }
    public RoadNetwork Roads { get; }
    public float[] RoadMask { get; }
    public List<PoiPlacement> Placements { get; }

    public WorldMapResult(BiomeMap biomes, HeightMap heights, RoadNetwork roads, float[] roadMask, List<PoiPlacement> placements)
    {
        Biomes = biomes;
        Heights = heights;
        Roads = roads;
        RoadMask = roadMask;
        Placements = placements;
    }
}

public sealed class WorldMapPipeline
{
    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;
    private readonly PoiDatabase _pois;

    public WorldMapPipeline(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois)
    {
        _config = config;
        _biomes = biomes;
        _pois = pois;
    }

    public WorldMapResult Generate(Action<string, float> progress = null, CancellationToken cancellationToken = default)
    {
        void Report(string stage, float fraction)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(stage, fraction);
            cancellationToken.ThrowIfCancellationRequested();
        }

        Report("Карта биомов", 0.02f);
        BiomeMap biomes = new BiomeMapGenerator(_config, _biomes).Generate();

        Report("Рельеф и эрозия", 0.08f);
        HeightMap heights = new HeightMapGenerator(_config, _biomes).Generate(biomes);

        Report("Поиск мест для поселений", 0.32f);
        var roads = new RoadNetwork();
        roads.Hubs.AddRange(new HubPlacer(_config, heights).Place());

        Report("Дороги и выравнивание земли", 0.38f);
        roads.Roads.AddRange(new RoadPlanner(_config, heights).Plan(roads.Hubs));
        var carver = new TerrainCarver(_config);
        heights = carver.Carve(heights, roads, out float[] mask);

        Report("Улицы и участки под здания", 0.44f);
        List<CityLayout> cities = new CityPlanner(_config, _pois, heights).Plan(roads.Hubs, roads.Roads);

        foreach (CityLayout city in cities)
            roads.Streets.AddRange(city.Streets);

        heights = carver.CarveStreets(heights, cities, mask);
        var proximity = new RoadProximity(roads.Roads, _config.WorldSize, _config.RoadCellSize);
        proximity.AddRange(roads.Streets);

        Report("Здания и площадки POI", 0.49f);
        var placer = new PoiPlacer(_config, _pois, heights, proximity);
        List<PoiPlacement> placements = placer.Place(cities, roads);
        heights = carver.CarvePads(heights, placements);
        placer.ApplyHeights(heights);
        Report("Карты готовы", 0.53f);

        return new WorldMapResult(biomes, heights, roads, mask, placements);
    }
}

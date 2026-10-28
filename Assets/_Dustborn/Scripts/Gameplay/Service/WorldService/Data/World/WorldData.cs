using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WorldData : IDisposable
{
    private readonly List<Object> _owned;
    private byte[] _heights;

    public string Id { get; }
    public string Name { get; }
    public string Fingerprint { get; }
    public WorldGenerationConfig Config { get; }
    public BiomeDatabase Biomes { get; }
    public WaterMap Water { get; }
    public Texture2D BiomeMap { get; }
    public Texture2D RoadMask { get; }
    public RoadNetworkAsset Roads { get; }
    public PoiPlacementAsset Placement { get; }
    public Material Material { get; }

    public byte[] Heights => _heights ?? throw new InvalidOperationException($"World '{Id}': the raw heights were released once the terrain decoded them");

    public WorldData(string id, string name, string fingerprint, WorldGenerationConfig config, BiomeDatabase biomes, byte[] heights,
        WaterMap water, Texture2D biomeMap, Texture2D roadMask, RoadNetworkAsset roads, PoiPlacementAsset placement, Material material,
        IEnumerable<Object> owned = null)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Fingerprint = fingerprint;
        Config = config;
        Biomes = biomes;
        _heights = heights;
        Water = water;
        BiomeMap = biomeMap;
        RoadMask = roadMask;
        Roads = roads;
        Placement = placement;
        Material = material;
        _owned = owned == null ? new List<Object>() : new List<Object>(owned);
    }

    public static WorldData FromBaked(BakedWorld world)
    {
        if (world == null)
            throw new InvalidOperationException("The scene has no baked world to load");

        if (world.Stale)
            throw new InvalidOperationException($"The baked world was made by generator version {world.GeneratorVersion}, this build expects {BakedWorld.GENERATOR_VERSION}: its coast, rivers and roads no longer match. Regenerate the world.");

        if (!world.IsValid)
            throw new InvalidOperationException($"The baked world '{world.name}' is incomplete; regenerate it");

        return new WorldData(string.Empty, world.name, world.Fingerprint(), world.Config, world.Biomes, world.HeightMap.bytes,
            world.LoadWater(), world.BiomeMap, world.RoadMask, world.Roads, world.Placement, world.Material);
    }

    public void ReleaseHeights() => _heights = null;

    public void Dispose()
    {
        foreach (Object owned in _owned)
            Release(owned);

        _owned.Clear();
        _heights = null;
    }

    public static void Release(Object owned)
    {
        if (owned == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(owned);
        else
            Object.DestroyImmediate(owned);
    }
}

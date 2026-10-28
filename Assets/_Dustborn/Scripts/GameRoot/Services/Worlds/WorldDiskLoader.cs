using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WorldDiskLoader
{
    private const string CONTROL_PROPERTY = "_Control{0}";
    private const string LAYERS_PROPERTY = "_LayersCount";
    private const string UV_SPACE_PROPERTY = "_UVSpace";
    private const int MAX_CONTROLS = 8;

    private readonly WorldBuildSettings _settings;
    private readonly Material _groundTemplate;

    public WorldDiskLoader(WorldBuildSettings settings, Material groundTemplate)
    {
        _settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
        _groundTemplate = groundTemplate != null
            ? groundTemplate
            : throw new InvalidOperationException("A saved world is painted onto the shipped ground material, and the scene has none");
    }

    public WorldData Assemble(WorldDiskContent content)
    {
        WorldManifest manifest = content.Manifest;
        CheckLayers(manifest);

        string fingerprint = WorldFingerprint.Of(manifest.GeneratorVersion, manifest.Seed, manifest.WorldSize, content.Heights,
            content.Water, manifest.Placements, manifest.Props);

        if (fingerprint != manifest.Fingerprint)
            throw new InvalidOperationException($"World '{manifest.Id}' is damaged: its files hash to {fingerprint}, the manifest says {manifest.Fingerprint}");

        var owned = new List<Object>();

        try
        {
            WorldGenerationConfig config = WorldConfigJson.Read(content.ConfigJson, _settings.Config);
            owned.Add(config);
            CheckConfig(manifest, config);

            Texture2D biomeMap = Track(owned, CpuTexture(manifest.BiomeResolution, TextureFormat.RGBA32, content.BiomeMap, "BiomeMap"));
            Texture2D roadMask = Track(owned, CpuTexture(manifest.RoadMaskResolution, TextureFormat.R8, content.RoadMask, "RoadMask"));

            var roads = Track(owned, ScriptableObject.CreateInstance<RoadNetworkAsset>());
            JsonUtility.FromJsonOverwrite(content.RoadsJson, roads);

            PoiPlacementAsset placement = Track(owned, WorldPlacementFile.Read(content.PlacementJson, new WorldPrefabIndex(_settings.Pois, _settings.Props)));
            Material material = Track(owned, Ground(manifest, content.Controls, owned));
            WaterMap water = WaterMapFormat.Read(content.Water);

            return new WorldData(manifest.Id, manifest.Name, fingerprint, config, _settings.Biomes, content.Heights, water,
                biomeMap, roadMask, roads, placement, material, owned);
        }
        catch
        {
            foreach (Object created in owned)
                WorldData.Release(created);

            throw;
        }
    }

    private void CheckLayers(WorldManifest manifest)
    {
        int shipped = Mathf.RoundToInt(_groundTemplate.GetFloat(LAYERS_PROPERTY));

        if (manifest.LayersCount != shipped)
            throw new InvalidOperationException($"World '{manifest.Id}' was painted for {manifest.LayersCount} ground layers, the shipped ground material has {shipped}");

        if (manifest.ControlCount > MAX_CONTROLS)
            throw new InvalidOperationException($"World '{manifest.Id}' has {manifest.ControlCount} control textures, the ground shader reads {MAX_CONTROLS}");
    }

    private static void CheckConfig(WorldManifest manifest, WorldGenerationConfig config)
    {
        if (config.Seed != manifest.Seed || config.WorldSize != manifest.WorldSize
            || config.HeightMapResolution != manifest.HeightResolution || config.BiomeMapResolution != manifest.BiomeResolution)
            throw new InvalidOperationException($"World '{manifest.Id}': its stored config does not describe its maps (seed {config.Seed}, size {config.WorldSize}, height map {config.HeightMapResolution}, biome map {config.BiomeMapResolution})");
    }

    private Material Ground(WorldManifest manifest, byte[][] controls, List<Object> owned)
    {
        var material = new Material(_groundTemplate) { name = $"{_groundTemplate.name} ({manifest.Id})" };

        for (int i = 0; i < MAX_CONTROLS; i++)
        {
            string property = string.Format(CONTROL_PROPERTY, i);
            if (!material.HasProperty(property))
                continue;

            Texture2D control = i < controls.Length ? Track(owned, Control(manifest.ControlResolution, controls[i], i)) : Texture2D.blackTexture;
            material.SetTexture(property, control);
        }

        material.SetFloat(LAYERS_PROPERTY, manifest.LayersCount);
        material.SetFloat(UV_SPACE_PROPERTY, 1f);
        return material;
    }

    private static Texture2D Control(int resolution, byte[] pixels, int index)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = $"Control{index}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.LoadRawTextureData(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CpuTexture(int resolution, TextureFormat format, byte[] pixels, string name)
    {
        var texture = new Texture2D(resolution, resolution, format, false, true)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixelData(pixels, 0);
        return texture;
    }

    private static T Track<T>(List<Object> owned, T created) where T : Object
    {
        if (!owned.Contains(created))
            owned.Add(created);

        return created;
    }
}

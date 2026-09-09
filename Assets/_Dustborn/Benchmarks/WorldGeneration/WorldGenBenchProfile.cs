using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WorldGenBenchProfile
{
    public string Id { get; }

    public WorldGenerationConfig Config { get; }

    public BiomeDatabase Biomes { get; }

    public PoiDatabase Pois { get; }

    public VoxelConfig Voxels { get; }

    public WorldBuildSettings Settings { get; }

    public BakedWorld World { get; }

    public string Fingerprint { get; }

    private readonly List<Object> _clones;

    private WorldGenBenchProfile(string id, WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois,
        VoxelConfig voxels, WorldBuildSettings settings, BakedWorld world, List<Object> clones)
    {
        Id = id;
        Config = config;
        Biomes = biomes;
        Pois = pois;
        Voxels = voxels;
        Settings = settings;
        World = world;
        _clones = clones;
        Fingerprint = WorldGenBenchArgs.Hash(Describe());
    }

    public static readonly string[] KNOWN = { "S512", "M2048", "P8192", "SYN" };

    public static WorldGenBenchProfile Create(string id, WorldGenBenchArgs overrides)
    {
        WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

        if (fixture == null || !fixture.HasSources)
            throw new InvalidOperationException("The benchmark fixture is missing. Run the fixture builder in the editor first.");

        var clones = new List<Object>();

        WorldGenerationConfig config = Clone(fixture.Config, clones);
        BiomeDatabase biomes = CloneBiomes(fixture.Biomes, clones);
        VoxelConfig voxels = Clone(fixture.Voxels, clones);
        WorldBuildSettings settings = Clone(fixture.Settings, clones);

        Assign(settings, nameof(settings.Config), config);
        Assign(settings, nameof(settings.Biomes), biomes);
        Assign(settings, nameof(settings.Pois), fixture.Pois);
        Assign(settings, nameof(settings.Voxels), voxels);

        switch ((id ?? "P8192").ToUpperInvariant())
        {
            case "S512":
                ApplySmall(config, biomes);
                break;

            case "M2048":
                ApplyMedium(config);
                break;

            case "SYN":
                ApplySynthetic(config, biomes);
                break;

            case "P8192":
                break;

            default:
                throw new InvalidOperationException($"Unknown benchmark profile '{id}'.");
        }

        if (overrides != null)
            ApplyOverrides(config, biomes, voxels, settings, overrides);

        return new WorldGenBenchProfile(id, config, biomes, fixture.Pois, voxels, settings, fixture.World, clones);
    }

    public void Release()
    {
        foreach (Object clone in _clones)
        {
            if (clone != null)
                Object.DestroyImmediate(clone);
        }

        _clones.Clear();
    }

    private static void ApplySmall(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        Assign(config, nameof(config.WorldSize), 512);
        Assign(config, nameof(config.HeightCellSize), 4);
        Assign(config, nameof(config.BiomeCellSize), 8);
        Assign(config, nameof(config.Seed), 1337);
        Assign(config, nameof(config.HubCount), 4);
        Assign(config, nameof(config.CityCount), 0);
        Assign(config, nameof(config.TownCount), 4);
        Assign(config, nameof(config.HubEdgeMargin), 100f);
        Assign(config, nameof(config.MinHubDistance), 96f);
        Assign(config, nameof(config.MinHubRadius), 64f);
        Assign(config, nameof(config.MaxHubRadius), 80f);
        Assign(config, nameof(config.MaxHubRelief), 360f);
        Assign(config, nameof(config.ErosionPasses), 1);
        Assign(config, nameof(config.HydraulicPasses), 2);
        Assign(config, nameof(config.ContinentAmplitude), 0.01f);
        Assign(config, nameof(config.ReliefScale), 0.2f);
        Assign(config, nameof(config.SeaLevel), 0f);

        foreach (BiomeDefinition biome in biomes.Biomes)
        {
            if (biome == null)
                continue;

            Assign(biome, nameof(biome.BaseHeight), 0.3f);
            Assign(biome, nameof(biome.HillAmplitude), 0.02f);
            Assign(biome, nameof(biome.RidgeAmplitude), 0.001f);
            Assign(biome, nameof(biome.DuneAmplitude), 0f);
            Assign(biome, nameof(biome.DetailAmplitude), 0.001f);
        }
    }

    private static void ApplyMedium(WorldGenerationConfig config)
    {
        Assign(config, nameof(config.WorldSize), 2048);
        Assign(config, nameof(config.HeightCellSize), 2);
        Assign(config, nameof(config.BiomeCellSize), 8);
        Assign(config, nameof(config.HubCount), 9);
        Assign(config, nameof(config.CityCount), 1);
        Assign(config, nameof(config.TownCount), 3);
        Assign(config, nameof(config.MinHubDistance), 300f);
        Assign(config, nameof(config.HubEdgeMargin), 320f);
    }

    private static void ApplySynthetic(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        ApplySmall(config, biomes);
        Assign(config, nameof(config.WorldSize), 512);
        Assign(config, nameof(config.HeightCellSize), 2);
        Assign(config, nameof(config.ErosionPasses), 0);
        Assign(config, nameof(config.HydraulicPasses), 0);
    }

    private static void ApplyOverrides(WorldGenerationConfig config, BiomeDatabase biomes, VoxelConfig voxels,
        WorldBuildSettings settings, WorldGenBenchArgs overrides)
    {
        foreach (KeyValuePair<string, string> pair in overrides.Values)
        {
            string key = pair.Key;

            if (!key.StartsWith("set.", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = key.Substring(4);
            int dot = path.IndexOf('.');
            string target = dot < 0 ? "config" : path.Substring(0, dot);
            string member = dot < 0 ? path : path.Substring(dot + 1);

            switch (target.ToLowerInvariant())
            {
                case "config":
                    AssignText(config, member, pair.Value);
                    break;

                case "voxels":
                    AssignText(voxels, member, pair.Value);
                    break;

                case "settings":
                    AssignText(settings, member, pair.Value);
                    break;

                case "biomes":
                    foreach (BiomeDefinition biome in biomes.Biomes)
                    {
                        if (biome != null)
                            AssignText(biome, member, pair.Value);
                    }

                    break;

                case "cityprofile":
                    AssignText(config.CityProfile, member, pair.Value);
                    break;

                case "townprofile":
                    AssignText(config.TownProfile, member, pair.Value);
                    break;

                case "villageprofile":
                    AssignText(config.VillageProfile, member, pair.Value);
                    break;

                default:
                    AssignText(config, path, pair.Value);
                    break;
            }
        }
    }

    public string Describe()
    {
        var text = new StringBuilder();
        text.Append("profile=").Append(Id).Append('\n');
        Describe(text, "config", Config);
        Describe(text, "voxels", Voxels);
        Describe(text, "settings", Settings);

        for (int index = 0; index < Biomes.Count; index++)
            Describe(text, "biome" + index, Biomes.Get(index));

        return text.ToString();
    }

    private static void Describe(StringBuilder text, string prefix, object target)
    {
        if (target == null)
            return;

        PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(properties, (first, second) => string.CompareOrdinal(first.Name, second.Name));

        foreach (PropertyInfo property in properties)
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                continue;

            Type type = property.PropertyType;

            if (!type.IsPrimitive && type != typeof(string) && !type.IsEnum)
                continue;

            object value;

            try
            {
                value = property.GetValue(target);
            }
            catch (Exception)
            {
                continue;
            }

            text.Append(prefix).Append('.').Append(property.Name).Append('=')
                .Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static T Clone<T>(T source, List<Object> clones) where T : ScriptableObject
    {
        T copy = Object.Instantiate(source);
        copy.name = source.name + " (benchmark clone)";
        copy.hideFlags = HideFlags.HideAndDontSave;
        clones.Add(copy);
        return copy;
    }

    private static BiomeDatabase CloneBiomes(BiomeDatabase source, List<Object> clones)
    {
        BiomeDatabase copy = Clone(source, clones);
        var definitions = new List<BiomeDefinition>();

        foreach (BiomeDefinition biome in source.Biomes)
            definitions.Add(biome == null ? null : Clone(biome, clones));

        Assign(copy, "Biomes", definitions);
        return copy;
    }

    public static void Assign(object target, string member, object value)
    {
        if (target == null)
            return;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);

        if (property != null && property.SetMethod != null)
        {
            property.SetValue(target, value);
            return;
        }

        FieldInfo field = Backing(type, member);

        if (field == null)
            throw new InvalidOperationException($"{type.Name} has no settable member '{member}'.");

        field.SetValue(target, value);
    }

    public static void AssignText(object target, string member, string text)
    {
        if (target == null)
            return;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        Type valueType = property != null ? property.PropertyType : Backing(type, member)?.FieldType;

        if (valueType == null)
            throw new InvalidOperationException($"{type.Name} has no member '{member}'.");

        Assign(target, member, Parse(valueType, text));
    }

    private static object Parse(Type type, string text)
    {
        if (type == typeof(int))
            return int.Parse(text, CultureInfo.InvariantCulture);

        if (type == typeof(float))
            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        if (type == typeof(bool))
            return bool.Parse(text);

        if (type == typeof(string))
            return text;

        if (type.IsEnum)
            return Enum.Parse(type, text, true);

        throw new InvalidOperationException($"Cannot parse '{text}' as {type.Name}.");
    }

    private static FieldInfo Backing(Type type, string member)
    {
        FieldInfo field = type.GetField($"<{member}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (field != null)
            return field;

        field = type.GetField(member, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (field != null)
            return field;

        string camel = "_" + char.ToLowerInvariant(member[0]) + member.Substring(1);
        return type.GetField(camel, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }
}

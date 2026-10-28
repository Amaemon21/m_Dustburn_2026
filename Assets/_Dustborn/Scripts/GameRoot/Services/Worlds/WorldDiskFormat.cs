using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

public static class WorldDiskFormat
{
    public const string MANIFEST_FILE = "world.json";

    private const string CONFIG_FILE = "config.json";
    private const string HEIGHTS_FILE = "heights.bin";
    private const string WATER_FILE = "water.bin";
    private const string BIOMES_FILE = "biomes.bin";
    private const string ROAD_MASK_FILE = "roadmask.bin";
    private const string ROADS_FILE = "roads.json";
    private const string PLACEMENT_FILE = "placement.json";
    private const string CONTROL_FILE = "control{0}.bin";
    public const string PARTIAL_SUFFIX = ".partial";
    public const string PREVIEW_FILE = "preview.png";
    private const int RGBA = 4;

    public static void Write(string directory, WorldDiskContent content)
    {
        string partial = directory + PARTIAL_SUFFIX;

        if (Directory.Exists(partial))
            Directory.Delete(partial, true);

        Directory.CreateDirectory(partial);

        WriteText(partial, CONFIG_FILE, content.ConfigJson);
        WriteCompressed(partial, HEIGHTS_FILE, content.Heights);
        WriteCompressed(partial, WATER_FILE, content.Water);
        WriteCompressed(partial, BIOMES_FILE, content.BiomeMap);
        WriteCompressed(partial, ROAD_MASK_FILE, content.RoadMask);
        WriteText(partial, ROADS_FILE, content.RoadsJson);
        WriteText(partial, PLACEMENT_FILE, content.PlacementJson);

        for (int i = 0; i < content.Controls.Length; i++)
            WriteCompressed(partial, string.Format(CONTROL_FILE, i), content.Controls[i]);

        if (content.Preview != null)
            File.WriteAllBytes(Path.Combine(partial, PREVIEW_FILE), content.Preview);

        WriteText(partial, MANIFEST_FILE, JsonUtility.ToJson(content.Manifest, true));

        if (Directory.Exists(directory))
            Directory.Delete(directory, true);

        Directory.Move(partial, directory);
    }

    public static WorldManifest ReadManifest(string directory)
    {
        string path = Path.Combine(directory, MANIFEST_FILE);
        if (!File.Exists(path))
            return null;

        WorldManifest manifest = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(path, Encoding.UTF8));
        if (manifest == null || string.IsNullOrEmpty(manifest.Id))
            throw new InvalidDataException($"World manifest '{path}' has no id");

        return manifest;
    }

    public static WorldDiskContent Read(string directory)
    {
        WorldManifest manifest = ReadManifest(directory) ?? throw new FileNotFoundException("The world has no manifest", Path.Combine(directory, MANIFEST_FILE));

        if (manifest.FormatVersion != WorldManifest.FORMAT_VERSION)
            throw new InvalidDataException($"World '{manifest.Id}' is stored in format {manifest.FormatVersion}, this build reads {WorldManifest.FORMAT_VERSION}");

        if (manifest.GeneratorVersion != BakedWorld.GENERATOR_VERSION)
            throw new InvalidDataException($"World '{manifest.Id}' was made by generator version {manifest.GeneratorVersion}, this build expects {BakedWorld.GENERATOR_VERSION}");

        int heights = manifest.HeightResolution;
        int biomes = manifest.BiomeResolution;
        int mask = manifest.RoadMaskResolution;
        int control = manifest.ControlResolution;

        var content = new WorldDiskContent
        {
            Manifest = manifest,
            ConfigJson = ReadText(directory, CONFIG_FILE),
            Heights = ReadCompressed(directory, HEIGHTS_FILE, (long)heights * heights * sizeof(ushort)),
            Water = ReadCompressed(directory, WATER_FILE, manifest.WaterLength),
            BiomeMap = ReadCompressed(directory, BIOMES_FILE, (long)biomes * biomes * RGBA),
            RoadMask = ReadCompressed(directory, ROAD_MASK_FILE, (long)mask * mask),
            RoadsJson = ReadText(directory, ROADS_FILE),
            PlacementJson = ReadText(directory, PLACEMENT_FILE),
            Controls = new byte[manifest.ControlCount][]
        };

        for (int i = 0; i < manifest.ControlCount; i++)
            content.Controls[i] = ReadCompressed(directory, string.Format(CONTROL_FILE, i), (long)control * control * RGBA);

        return content;
    }

    private static void WriteText(string directory, string file, string text)
    {
        File.WriteAllText(Path.Combine(directory, file), text, new UTF8Encoding(false));
    }

    private static string ReadText(string directory, string file)
    {
        return File.ReadAllText(Path.Combine(directory, file), Encoding.UTF8);
    }

    private static void WriteCompressed(string directory, string file, byte[] data)
    {
        using FileStream stream = File.Create(Path.Combine(directory, file));
        using var deflate = new DeflateStream(stream, CompressionLevel.Fastest);
        deflate.Write(data, 0, data.Length);
    }

    private static byte[] ReadCompressed(string directory, string file, long expectedLength)
    {
        string path = Path.Combine(directory, file);

        if (expectedLength <= 0 || expectedLength > int.MaxValue)
            throw new InvalidDataException($"'{path}': the manifest gives an impossible length {expectedLength}");

        var data = new byte[expectedLength];

        using FileStream stream = File.OpenRead(path);
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);

        int read = 0;
        while (read < data.Length)
        {
            int chunk = deflate.Read(data, read, data.Length - read);
            if (chunk == 0)
                throw new InvalidDataException($"'{path}' ends after {read} of {data.Length} bytes");

            read += chunk;
        }

        if (deflate.ReadByte() != -1)
            throw new InvalidDataException($"'{path}' is longer than the {data.Length} bytes the manifest gives");

        return data;
    }
}

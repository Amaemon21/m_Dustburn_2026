using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WorldExport
{
    private const string MENU = "Мир/Экспорт мира для игры";
    private const string TITLE = "Экспорт мира";
    private const string BUILT_IN_ID = "builtin_{0}";
    private const string BUILT_IN_NAME = "Мир {0}";
    private const string CONTROL_PROPERTY = "_Control{0}";
    private const string LAYERS_PROPERTY = "_LayersCount";
    private const int MAX_CONTROLS = 8;

    [MenuItem(MENU)]
    private static void Export()
    {
        if (!TryFindWorld(out WorldBuildSettings settings, out BakedWorld baked))
            return;

        string id = string.Format(BUILT_IN_ID, baked.Config.Seed);
        string directory = Path.Combine(WorldCatalog.BuiltInRoot, id);

        try
        {
            EditorUtility.DisplayProgressBar(TITLE, "Сбор карт мира", 0.1f);
            WorldDiskContent content = Collect(settings, baked, id, string.Format(BUILT_IN_NAME, baked.Config.Seed));

            EditorUtility.DisplayProgressBar(TITLE, "Запись на диск", 0.4f);
            WorldDiskFormat.Write(directory, content);

            EditorUtility.DisplayProgressBar(TITLE, "Проверка: чтение обратно", 0.7f);
            Verify(settings, baked, directory);

            Debug.Log($"World '{id}' exported to {directory} and read back identical: {content.Manifest.Fingerprint}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(TITLE, $"Экспорт не удался: {exception.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static bool TryFindWorld(out WorldBuildSettings settings, out BakedWorld baked)
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(WorldBuildSettings)}"))
        {
            settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(AssetDatabase.GUIDToAssetPath(guid));
            if (settings == null || settings.Config == null)
                continue;

            baked = AssetDatabase.LoadAssetAtPath<BakedWorld>(settings.BakedWorldPath);
            if (baked != null && baked.IsValid)
                return true;
        }

        settings = null;
        baked = null;
        EditorUtility.DisplayDialog(TITLE, "Нет сохранённого мира: сначала сгенерируйте его в «Мир → Генерация мира».", "OK");
        return false;
    }

    private static WorldDiskContent Collect(WorldBuildSettings settings, BakedWorld baked, string id, string name)
    {
        WorldGenerationConfig config = baked.Config;

        if (baked.Biomes != settings.Biomes)
            throw new InvalidOperationException($"The bake used biome database '{baked.Biomes.name}', the settings ship '{settings.Biomes.name}'; a saved world is read back with the shipped one");

        byte[][] controls = Controls(baked.Material, out int controlResolution);
        byte[] heights = baked.HeightMap.bytes;
        byte[] water = baked.Water.bytes;

        var manifest = new WorldManifest
        {
            GeneratorVersion = baked.GeneratorVersion,
            Id = id,
            Name = name,
            Fingerprint = baked.Fingerprint(),
            CreatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Seed = config.Seed,
            WorldSize = config.WorldSize,
            HeightResolution = config.HeightMapResolution,
            BiomeResolution = baked.BiomeMap.width,
            RoadMaskResolution = baked.RoadMask.width,
            WaterLength = water.Length,
            ControlResolution = controlResolution,
            ControlCount = controls.Length,
            LayersCount = Mathf.RoundToInt(baked.Material.GetFloat(LAYERS_PROPERTY)),
            Placements = baked.Placement.Placements.Count,
            Props = baked.Placement.Props.Count
        };

        return new WorldDiskContent
        {
            Manifest = manifest,
            ConfigJson = WorldConfigJson.Write(config),
            Heights = heights,
            Water = water,
            BiomeMap = WorldPixels.Rgba(baked.BiomeMap.GetPixels32()),
            RoadMask = WorldPixels.Gray(baked.RoadMask.GetPixels32()),
            RoadsJson = JsonUtility.ToJson(baked.Roads),
            PlacementJson = WorldPlacementFile.Write(baked.Placement.Placements, baked.Placement.Props, new WorldPrefabIndex(settings.Pois, settings.Props)),
            Controls = controls,
            Preview = Preview(settings, baked)
        };
    }

    private static byte[] Preview(WorldBuildSettings settings, BakedWorld baked)
    {
        WorldGenerationConfig config = baked.Config;
        BiomeMap biomes = BiomeMapTexture.Read(baked.BiomeMap, settings.Biomes, config.WorldSize);
        HeightMap heights = HeightMap.FromRaw16(baked.HeightMap.bytes, config.HeightMapResolution, config.WorldSize, config.MaxHeight);

        return WorldPreviewImage.Paint(settings.Biomes, biomes, heights, baked.LoadWater(), baked.Roads.Paved(), baked.Placement.Placements);
    }

    private static byte[][] Controls(Material material, out int resolution)
    {
        var controls = new List<byte[]>();
        resolution = 0;

        for (int i = 0; i < MAX_CONTROLS; i++)
        {
            string property = string.Format(CONTROL_PROPERTY, i);
            if (!material.HasProperty(property) || material.GetTexture(property) is not Texture2D control)
                break;

            string path = AssetDatabase.GetAssetPath(control);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException($"{property} of '{material.name}' is not a saved asset; rebake the ground with «Мир → Перепечь землю мира»");

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);

            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(path)))
                    throw new InvalidOperationException($"'{path}' is not a readable PNG");

                if (resolution != 0 && decoded.width != resolution)
                    throw new InvalidOperationException($"Control textures differ in size: '{path}' is {decoded.width}, the first was {resolution}");

                resolution = decoded.width;
                controls.Add(WorldPixels.Rgba(decoded.GetPixels32()));
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }
        }

        if (controls.Count == 0)
            throw new InvalidOperationException($"Ground material '{material.name}' has no control textures; rebake the ground");

        return controls.ToArray();
    }

    private static void Verify(WorldBuildSettings settings, BakedWorld baked, string directory)
    {
        WorldDiskContent content = WorldDiskFormat.Read(directory);
        WorldData world = new WorldDiskLoader(settings, baked.Material).Assemble(content);

        try
        {
            Expect("fingerprint", baked.Fingerprint(), world.Fingerprint);
            Expect("config", WorldConfigJson.Write(baked.Config), WorldConfigJson.Write(world.Config));
            Expect("roads", JsonUtility.ToJson(baked.Roads), JsonUtility.ToJson(world.Roads));

            var prefabs = new WorldPrefabIndex(settings.Pois, settings.Props);
            Expect("placement",
                WorldPlacementFile.Write(baked.Placement.Placements, baked.Placement.Props, prefabs),
                WorldPlacementFile.Write(world.Placement.Placements, world.Placement.Props, prefabs));
        }
        finally
        {
            world.Dispose();
        }
    }

    private static void Expect(string part, string expected, string actual)
    {
        if (expected != actual)
            throw new InvalidOperationException($"The exported world reads back with a different {part}");
    }
}

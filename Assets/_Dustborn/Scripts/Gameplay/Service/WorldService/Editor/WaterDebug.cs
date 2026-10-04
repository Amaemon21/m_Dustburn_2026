#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Debug = UnityEngine.Debug;

public static class WaterDebug
{
    private const string SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";
    private const string OUTPUT = "Temp/WaterDebug";
    private const int OVERVIEW = 2048;
    private const int DETAIL = 4096;

    [MenuItem("Мир/Предпросмотр воды и штампов")]
    public static void Preview()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(SETTINGS_PATH);

        if (settings == null || !settings.IsValid)
        {
            EditorUtility.DisplayDialog("Вода и штампы", "Настройки мира не найдены или неполные: " + SETTINGS_PATH, "OK");
            return;
        }

        try
        {
            string report = Run(settings);
            EditorUtility.RevealInFinder(Path.Combine(OUTPUT, "overview.png"));
            Debug.Log(report);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Мир/Вода и штампы в сцене")]
    private static void ToggleGizmos()
    {
        WorldFeatureGizmos.Visible = !WorldFeatureGizmos.Visible;
        SceneView.RepaintAll();
    }

    [MenuItem("Мир/Вода и штампы в сцене", true)]
    private static bool ValidateGizmos()
    {
        Menu.SetChecked("Мир/Вода и штампы в сцене", WorldFeatureGizmos.Visible);
        return true;
    }

    private static string Run(WorldBuildSettings settings)
    {
        WorldGenerationConfig config = settings.Config;
        var clock = Stopwatch.StartNew();

        EditorUtility.DisplayProgressBar("Вода и штампы", "Карта биомов", 0.05f);
        BiomeMap biomes = new BiomeMapGenerator(config, settings.Biomes).Generate();

        EditorUtility.DisplayProgressBar("Вода и штампы", "Рельеф, штампы и эрозия", 0.2f);
        var relief = new HeightMapGenerator(config, settings.Biomes);
        HeightMap heights = relief.Generate(biomes);
        long reliefMillis = clock.ElapsedMilliseconds;

        EditorUtility.DisplayProgressBar("Вода и штампы", "Сток, реки и озёра", 0.6f);
        WaterStampLibrary stamps = WaterStampLibrary.Create(config.Water);
        var hydrology = new Hydrology(config, heights, stamps);
        HydrologyGrid grid = hydrology.Analyze();
        WaterMap water = hydrology.Build(grid);
        HeightMap carved = RiverCarver.Carve(heights, water, config.Water.RiverBankWidth);

        if (stamps != null)
            WaterStampCarver.Carve(carved, heights, water, stamps);

        StandingWaterRegions regions = WaterShore.Finish(water, carved);
        long waterMillis = clock.ElapsedMilliseconds - reliefMillis;

        EditorUtility.DisplayProgressBar("Вода и штампы", "Картинки", 0.85f);
        Directory.CreateDirectory(OUTPUT);

        Save("flow_accumulation.png", WaterDebugImages.Accumulation(grid), grid.Resolution);
        Save("flow_direction.png", WaterDebugImages.Direction(grid), grid.Resolution);
        Save("basins.png", WaterDebugImages.Basins(grid), grid.Resolution);
        Save("water_mask.png", WaterDebugImages.Mask(water), water.Resolution);
        Save("overview.png", WaterDebugImages.Overview(carved, water, relief.Stamps, OVERVIEW), OVERVIEW);
        Save("body_id.png", WaterDebugImages.BodyIds(water), water.Resolution);
        Save("refined_water_mask.png", WaterDebugImages.Refined(carved, water, DETAIL), DETAIL);
        Save("coarse_vs_refined.png", WaterDebugImages.Comparison(carved, water, DETAIL), DETAIL);
        Save("shoreline.png", WaterDebugImages.Shoreline(carved, water, DETAIL), DETAIL);
        Save("river_centerlines.png", WaterDebugImages.Rivers(carved, water, OVERVIEW, WaterDebugImages.RiverTint.Centerline), OVERVIEW);
        Save("river_width.png", WaterDebugImages.Rivers(carved, water, OVERVIEW, WaterDebugImages.RiverTint.Width), OVERVIEW);
        Save("river_depth.png", WaterDebugImages.Rivers(carved, water, OVERVIEW, WaterDebugImages.RiverTint.Depth), OVERVIEW);
        File.WriteAllText(Path.Combine(OUTPUT, "stamps.txt"), WaterDebugImages.StampList(relief.Stamps));

        if (stamps != null)
        {
            EditorUtility.DisplayProgressBar("Вода и штампы", "Штампы воды", 0.95f);
            Save("water_stamps_overview.png", WaterDebugImages.Stamps(carved, water, stamps, OVERVIEW, Vector2.zero, carved.WorldSize), OVERVIEW);
            Save("water_stamp_masks.png", WaterDebugImages.StampMasks(water, stamps, OVERVIEW), OVERVIEW);
            File.WriteAllText(Path.Combine(OUTPUT, "water_stamps.txt"), WaterDebugImages.StampReport(water.Stamps));
        }

        string waterStamps = water.Stamps == null ? "water stamps off" : water.Stamps.Summary();

        return $"Water preview in {OUTPUT}: {relief.Stamps.Count} stamps, {waterStamps}, {water.Rivers.Count} rivers, {hydrology.Lakes} lakes, "
            + $"{hydrology.Ponds} ponds, {hydrology.Basins.Count} closed basins, {water.DetailCount} shore cells; {regions.Lowered} lakes lowered to their fine spill, {regions.Dropped} dropped, "
            + $"{regions.Fragments} fragments, {regions.Islets} islets sunk, {regions.SeaPuddles} sea puddles removed. Relief {reliefMillis} ms, hydrology and carving {waterMillis} ms";
    }

    private static void Save(string name, byte[] pixels, int size)
    {
        byte[] png = ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8_UNorm, (uint)size, (uint)size);
        File.WriteAllBytes(Path.Combine(OUTPUT, name), png);
    }
}
#endif

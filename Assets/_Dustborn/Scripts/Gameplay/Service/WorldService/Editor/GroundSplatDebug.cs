using System;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class GroundSplatDebug
{
    private const string SETTINGS_PATH = "Assets/_Dustborn/Content/World/WorldBuildSettings.asset";
    private const string OUTPUT = "Temp/GroundDebug";
    private const int WINDOW = 512;
    private const float SLOPE_SCALE = 60f;

    [MenuItem("Мир/Отладка раскраски земли")]
    public static void Dump()
    {
        WorldBuildSettings settings = AssetDatabase.LoadAssetAtPath<WorldBuildSettings>(SETTINGS_PATH);

        if (settings == null || settings.Biomes == null)
            throw new InvalidOperationException("Assign generation settings and biomes before debugging the ground.");

        BakedWorld world = AssetDatabase.LoadAssetAtPath<BakedWorld>(settings.BakedWorldPath);

        if (world == null || !world.IsValid)
            throw new InvalidOperationException("Bake a complete world before debugging its ground.");

        WorldGenerationConfig config = world.Config;
        BiomeDatabase biomes = settings.Biomes;
        BiomeMap biomeMap = BiomeMapTexture.Read(world.BiomeMap, biomes, config.WorldSize);
        var field = new BiomeWeightField(biomeMap, biomes.Count, config.BiomeBlendRadius);
        HeightMap map = HeightMap.FromRaw16(world.HeightMap.bytes, config.HeightMapResolution, config.WorldSize, config.MaxHeight);

        int resolution = settings.ControlResolution;
        float texel = (float)config.WorldSize / resolution;
        Vector2 centre = Centre(settings);

        int tileX = Mathf.Clamp(Mathf.RoundToInt(centre.x / texel) - WINDOW / 2, 0, resolution - WINDOW);
        int tileZ = Mathf.Clamp(Mathf.RoundToInt(centre.y / texel) - WINDOW / 2, 0, resolution - WINDOW);

        string folder = Path.GetFullPath(OUTPUT);

        if (Directory.Exists(folder))
            Directory.Delete(folder, true);

        Directory.CreateDirectory(folder);

        VoxelSplatBaker.Surface(config, map, resolution, tileX, tileZ, WINDOW, out float[] steepness, out float[] height, out float[] relief);

        using var painter = new GroundSplatPainter(config, biomes, field, world.Roads != null ? world.Roads.Paved() : null);

        NativeArray<float> weights = painter.BakeTile(resolution, WINDOW, tileX, tileZ, steepness, height, relief);

        try
        {
            WriteLayers(folder, painter, weights);
        }
        finally
        {
            weights.Dispose();
        }

        WriteFields(folder, config, biomes, field, painter, tileX, tileZ, texel, steepness, relief);

        Debug.Log($"Ground debug for a {WINDOW * texel:0} m window at ({(tileX + WINDOW / 2) * texel:0}, {(tileZ + WINDOW / 2) * texel:0}) written to {folder}");
        EditorUtility.RevealInFinder(Path.Combine(folder, "biomes.png"));
    }

    private static Vector2 Centre(WorldBuildSettings settings)
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view == null)
            return settings.PreviewCenter;

        Vector3 pivot = view.pivot;

        return new Vector2(pivot.x, pivot.z);
    }

    private static void WriteLayers(string folder, GroundSplatPainter painter, NativeArray<float> weights)
    {
        int layers = painter.Layers.Length;

        for (int layer = 0; layer < layers; layer++)
        {
            var values = new float[WINDOW * WINDOW];
            float peak = 0f;

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = weights[i * layers + layer];
                peak = Mathf.Max(peak, values[i]);
            }

            if (peak <= 0f)
                continue;

            WriteGray(Path.Combine(folder, $"layer_{layer:00}_{painter.Layers[layer].name}.png"), values);
        }
    }

    private static void WriteFields(string folder, WorldGenerationConfig config, BiomeDatabase biomes, BiomeWeightField field,
        GroundSplatPainter painter, int tileX, int tileZ, float texel, float[] steepness, float[] relief)
    {
        GroundNoise noise = GroundNoise.From(config);
        var filter = new DecorFilter(config, field, biomes.Count, null, 0, null, 0f);

        int count = WINDOW * WINDOW;
        var slope = new float[count];
        var shape = new float[count];
        var macro = new float[count];
        var cliff = new float[count];
        var biomeColour = new Color[count];

        var patches = new float[biomes.Count][][];
        var rules = new GroundRule[biomes.Count][];

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            rules[biome] = painter.RulesOf(biome);
            patches[biome] = new float[rules[biome].Length][];

            for (int rule = 1; rule < rules[biome].Length; rule++)
                if (rules[biome][rule].PatchThreshold > 0f)
                    patches[biome][rule] = new float[count];
        }

        for (int z = 0; z < WINDOW; z++)
        {
            for (int x = 0; x < WINDOW; x++)
            {
                int index = z * WINDOW + x;
                float worldX = (tileX + x + 0.5f) * texel;
                float worldZ = (tileZ + z + 0.5f) * texel;

                GroundSample sample = noise.Sample(worldX, worldZ, steepness[index], relief[index]);

                slope[index] = steepness[index] / SLOPE_SCALE;
                shape[index] = sample.Relief * 0.5f + 0.5f;
                macro[index] = sample.Macro * 0.5f + 0.5f;
                cliff[index] = math.smoothstep(config.CliffSlopeStart, config.CliffSlopeFull, noise.CliffSlope(steepness[index], sample));

                Color colour = Color.black;

                var point = new Vector2(worldX, worldZ);

                for (int biome = 0; biome < biomes.Count; biome++)
                {
                    colour += (Color)biomes.Get(biome).MapColor * filter.SurfaceWeight(point, biome);

                    float edge = GroundNoise.Edge(filter.Weight(point, biome));

                    for (int rule = 1; rule < rules[biome].Length; rule++)
                    {
                        if (patches[biome][rule] != null)
                            patches[biome][rule][index] = noise.Patch(rules[biome][rule], sample, edge);
                    }
                }

                biomeColour[index] = colour;
            }
        }

        WriteGray(Path.Combine(folder, "slope.png"), slope);
        WriteGray(Path.Combine(folder, "relief.png"), shape);
        WriteGray(Path.Combine(folder, "macro.png"), macro);
        WriteGray(Path.Combine(folder, "cliff.png"), cliff);
        WriteColour(Path.Combine(folder, "biomes.png"), biomeColour);

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            for (int rule = 1; rule < rules[biome].Length; rule++)
            {
                if (patches[biome][rule] != null)
                    WriteGray(Path.Combine(folder, $"patch_{biomes.Get(biome).Type}_{rule}_{painter.Layers[rules[biome][rule].Layer].name}.png"), patches[biome][rule]);
            }
        }
    }

    private static void WriteGray(string path, float[] values)
    {
        var colours = new Color[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            float value = Mathf.Clamp01(values[i]);
            colours[i] = new Color(value, value, value, 1f);
        }

        WriteColour(path, colours);
    }

    private static void WriteColour(string path, Color[] colours)
    {
        var bytes = new byte[colours.Length * 4];

        for (int i = 0; i < colours.Length; i++)
        {
            Color32 colour = colours[i];

            bytes[i * 4] = colour.r;
            bytes[i * 4 + 1] = colour.g;
            bytes[i * 4 + 2] = colour.b;
            bytes[i * 4 + 3] = 255;
        }

        File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(bytes, GraphicsFormat.R8G8B8A8_UNorm, WINDOW, WINDOW));
    }
}

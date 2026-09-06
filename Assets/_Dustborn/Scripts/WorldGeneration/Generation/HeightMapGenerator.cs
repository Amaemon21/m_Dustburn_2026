using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

public class HeightMapGenerator
{
    private const int BATCH_SIZE = 64;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;

    public HeightMapGenerator(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        _config = config;
        _biomes = biomes;
    }

    public HeightMap Generate(BiomeMap biomeMap)
    {
        var random = new Random(((uint)_config.Seed | 1u) * 747796405u + 1u);

        var weightField = new BiomeWeightField(biomeMap, _biomes.Count, _config.BiomeBlendPasses);
        var map = new HeightMap(_config.HeightMapResolution, _config.WorldSize, _config.MaxHeight);

        NativeArray<float> weights = weightField.ToNativeArray(Allocator.TempJob);
        NativeArray<BiomeHeightProfile> profiles = BuildProfiles(Allocator.TempJob);
        var heights = new NativeArray<float>(map.Heights.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            var job = new HeightMapJob
            {
                BiomeWeights = weights,
                Profiles = profiles,
                Heights = heights,
                Resolution = map.Resolution,
                WeightResolution = weightField.Resolution,
                BiomeCount = _biomes.Count,
                Settings = BuildSettings(),
                ContinentOffset = random.NextFloat2(-100f, 100f),
                HillOffset = random.NextFloat2(-100f, 100f),
                RidgeOffset = random.NextFloat2(-100f, 100f),
                DuneOffset = random.NextFloat2(-100f, 100f),
                DetailOffset = random.NextFloat2(-100f, 100f),
                MaskOffset = random.NextFloat2(-100f, 100f)
            };

            job.Schedule(heights.Length, BATCH_SIZE).Complete();

            HeightMapErosion.Run(_config, heights, map.Resolution);

            heights.CopyTo(map.Heights);
        }
        finally
        {
            weights.Dispose();
            profiles.Dispose();
            heights.Dispose();
        }

        return map;
    }

    private NativeArray<BiomeHeightProfile> BuildProfiles(Allocator allocator)
    {
        var profiles = new NativeArray<BiomeHeightProfile>(_biomes.Count, allocator, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < _biomes.Count; i++)
        {
            BiomeDefinition biome = _biomes.Get(i);

            profiles[i] = new BiomeHeightProfile
            {
                BaseHeight = biome.BaseHeight,
                HillAmplitude = biome.HillAmplitude,
                RidgeAmplitude = biome.RidgeAmplitude,
                DuneAmplitude = biome.DuneAmplitude,
                DetailAmplitude = biome.DetailAmplitude
            };
        }

        return profiles;
    }

    private HeightFieldSettings BuildSettings()
    {
        return new HeightFieldSettings
        {
            ReliefScale = _config.ReliefScale,
            ContinentAmplitude = _config.ContinentAmplitude,
            ContinentFrequency = _config.ContinentFrequency,
            HillFrequency = _config.HillFrequency,
            RidgeFrequency = _config.RidgeFrequency,
            DuneFrequency = _config.DuneFrequency,
            DetailFrequency = _config.DetailFrequency,
            ContinentOctaves = _config.ContinentOctaves,
            HillOctaves = _config.HillOctaves,
            RidgeOctaves = _config.RidgeOctaves,
            DuneOctaves = _config.DuneOctaves,
            DetailOctaves = _config.DetailOctaves,
            MountainMaskFrequency = _config.MountainMaskFrequency,
            MountainMaskLow = _config.MountainMaskLow,
            MountainMaskHigh = _config.MountainMaskHigh
        };
    }
}

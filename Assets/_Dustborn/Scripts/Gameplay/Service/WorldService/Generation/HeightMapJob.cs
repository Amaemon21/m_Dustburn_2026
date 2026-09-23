using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public struct BiomeHeightProfile
{
    public float BaseHeight;
    public float HillAmplitude;
    public float RidgeAmplitude;
    public float DuneAmplitude;
    public float DetailAmplitude;
}

public struct HeightFieldSettings
{
    public float ReliefScale;

    public float ContinentAmplitude;
    public float ContinentFrequency;
    public float HillFrequency;
    public float RidgeFrequency;
    public float DuneFrequency;
    public float DuneWarp;
    public float DetailFrequency;

    public int ContinentOctaves;
    public int HillOctaves;
    public int RidgeOctaves;
    public int DuneOctaves;
    public int DetailOctaves;

    public float MountainMaskFrequency;
    public float MountainMaskLow;
    public float MountainMaskHigh;
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HeightMapJob : IJobParallelFor
{
    private const float DUNE_ABSENT = 1e-5f;
    private const float DUNE_BEND_FREQUENCY = 0.5f;

    [ReadOnly] public NativeArray<float> BiomeWeights;
    [ReadOnly] public NativeArray<BiomeHeightProfile> Profiles;

    [WriteOnly] public NativeArray<float> Heights;

    public int Resolution;
    public int WeightResolution;
    public int BiomeCount;

    public HeightFieldSettings Settings;

    public float2 ContinentOffset;
    public float2 HillOffset;
    public float2 RidgeOffset;
    public float2 DuneOffset;
    public float2 DetailOffset;
    public float2 MaskOffset;
    public float2 DuneBendX;
    public float2 DuneBendZ;
    public float2 DuneAxis;

    public void Execute(int index)
    {
        int last = Resolution - 1;
        int x = index % Resolution;
        int y = index / Resolution;

        float2 uv = new((float)x / last, (float)y / last);

        BlendProfiles(uv, out BiomeHeightProfile blended);

        float continent = FractalNoise.Sample01(uv * Settings.ContinentFrequency, ContinentOffset, Settings.ContinentOctaves, 2f, 0.5f);
        float hills = FractalNoise.Billow(uv * Settings.HillFrequency, HillOffset, Settings.HillOctaves, 2f, 0.5f);
        float ridge = FractalNoise.Ridged(uv * Settings.RidgeFrequency, RidgeOffset, Settings.RidgeOctaves, 2.1f, 0.45f);
        float detail = FractalNoise.Sample01(uv * Settings.DetailFrequency, DetailOffset, Settings.DetailOctaves, 2f, 0.5f);

        float dune = blended.DuneAmplitude > DUNE_ABSENT ? Dune(uv) : 0f;

        float maskNoise = FractalNoise.Sample01(uv * Settings.MountainMaskFrequency, MaskOffset, 3, 2f, 0.5f);
        float mask = math.smoothstep(Settings.MountainMaskLow, Settings.MountainMaskHigh, maskNoise);

        float relief = Settings.ReliefScale;

        float height = blended.BaseHeight
                       + Settings.ContinentAmplitude * continent
                       + blended.HillAmplitude * hills * relief
                       + blended.RidgeAmplitude * ridge * mask * relief
                       + blended.DuneAmplitude * dune * relief
                       + blended.DetailAmplitude * detail * relief;

        Heights[index] = math.saturate(height);
    }

    private float Dune(float2 uv)
    {
        float2 slow = uv * (Settings.DuneFrequency * DUNE_BEND_FREQUENCY);
        float2 bend = new float2(noise.snoise(slow + DuneBendX), noise.snoise(slow + DuneBendZ)) * (Settings.DuneWarp / Settings.DuneFrequency);

        float2 turned = FractalNoise.Turn(uv + bend, DuneAxis);
        float2 duneUv = new(turned.x * Settings.DuneFrequency * 0.35f, turned.y * Settings.DuneFrequency * 2.2f);

        return FractalNoise.Ridged(duneUv, DuneOffset, Settings.DuneOctaves, 2f, 0.5f);
    }

    private void BlendProfiles(float2 uv, out BiomeHeightProfile blended)
    {
        blended = default;

        BiomeWeightSampler sampler = BiomeWeightSampler.At(uv.x, uv.y, WeightResolution);

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            float weight = sampler.Sample(BiomeWeights, biome, WeightResolution);

            BiomeHeightProfile profile = Profiles[biome];

            blended.BaseHeight += weight * profile.BaseHeight;
            blended.HillAmplitude += weight * profile.HillAmplitude;
            blended.RidgeAmplitude += weight * profile.RidgeAmplitude;
            blended.DuneAmplitude += weight * profile.DuneAmplitude;
            blended.DetailAmplitude += weight * profile.DetailAmplitude;
        }
    }
}

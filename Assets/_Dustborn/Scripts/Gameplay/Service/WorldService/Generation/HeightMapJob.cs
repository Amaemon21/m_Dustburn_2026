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

        float2 duneUv = new(uv.x * Settings.DuneFrequency * 0.35f, uv.y * Settings.DuneFrequency * 2.2f);
        float dune = FractalNoise.Ridged(duneUv, DuneOffset, Settings.DuneOctaves, 2f, 0.5f);

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

    private void BlendProfiles(float2 uv, out BiomeHeightProfile blended)
    {
        blended = default;

        int last = WeightResolution - 1;

        float fx = math.clamp(uv.x * WeightResolution - 0.5f, 0f, last);
        float fy = math.clamp(uv.y * WeightResolution - 0.5f, 0f, last);

        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = math.min(x0 + 1, last);
        int y1 = math.min(y0 + 1, last);

        float tx = Fade(fx - x0);
        float ty = Fade(fy - y0);

        int cellCount = WeightResolution * WeightResolution;

        int bottomLeft = y0 * WeightResolution + x0;
        int bottomRight = y0 * WeightResolution + x1;
        int topLeft = y1 * WeightResolution + x0;
        int topRight = y1 * WeightResolution + x1;

        for (int biome = 0; biome < BiomeCount; biome++)
        {
            int offset = biome * cellCount;

            float bottom = math.lerp(BiomeWeights[offset + bottomLeft], BiomeWeights[offset + bottomRight], tx);
            float top = math.lerp(BiomeWeights[offset + topLeft], BiomeWeights[offset + topRight], tx);
            float weight = math.lerp(bottom, top, ty);

            BiomeHeightProfile profile = Profiles[biome];

            blended.BaseHeight += weight * profile.BaseHeight;
            blended.HillAmplitude += weight * profile.HillAmplitude;
            blended.RidgeAmplitude += weight * profile.RidgeAmplitude;
            blended.DuneAmplitude += weight * profile.DuneAmplitude;
            blended.DetailAmplitude += weight * profile.DetailAmplitude;
        }
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}

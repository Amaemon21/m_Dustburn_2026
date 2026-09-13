using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public struct BiomeSeed
{
    public float2 Position;
    public float InverseWeightSqr;
    public byte Biome;
}

[BurstCompile(CompileSynchronously = true)]
public struct BiomeClassifyJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<BiomeSeed> Seeds;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Cells;

    public int Resolution;
    public float2 WarpOffset;
    public float WarpStrength;
    public float WarpFrequency;
    public int WarpOctaves;
    public float WarpLacunarity;
    public float WarpPersistence;

    public void Execute(int row)
    {
        int origin = row * Resolution;
        float v = (row + 0.5f) / Resolution;

        for (int x = 0; x < Resolution; x++)
        {
            var uv = new float2((x + 0.5f) / Resolution, v);

            Cells[origin + x] = Pick(uv + Warp(uv));
        }
    }

    private float2 Warp(float2 uv)
    {
        if (WarpStrength <= 0f)
            return float2.zero;

        float2 position = uv * WarpFrequency;

        float offsetX = FractalNoise.Sample(position, WarpOffset, WarpOctaves, WarpLacunarity, WarpPersistence);
        float offsetY = FractalNoise.Sample(position + new float2(17.31f, 9.17f), WarpOffset, WarpOctaves, WarpLacunarity, WarpPersistence);

        return new float2(offsetX, offsetY) * WarpStrength;
    }

    private byte Pick(float2 position)
    {
        byte best = 0;
        float bestScore = float.MaxValue;

        for (int i = 0; i < Seeds.Length; i++)
        {
            BiomeSeed seed = Seeds[i];
            float score = math.distancesq(position, seed.Position) * seed.InverseWeightSqr;

            if (score >= bestScore)
                continue;

            bestScore = score;
            best = seed.Biome;
        }

        return best;
    }
}

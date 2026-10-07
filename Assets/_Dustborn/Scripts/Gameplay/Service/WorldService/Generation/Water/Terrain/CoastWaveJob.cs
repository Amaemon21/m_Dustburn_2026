using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict, CompileSynchronously = true)]
public struct CoastWaveJob : IJobParallelFor
{
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Wander;

    public int Size;
    public float Step;
    public float Period;
    public float2 Offset;
    public float2 Axis;

    public void Execute(int row)
    {
        for (int column = 0; column < Size; column++)
        {
            float2 position = FractalNoise.Turn(new float2(column * Step, row * Step) / Period, Axis);
            Wander[row * Size + column] = FractalNoise.Sample(position, Offset, CoastShaper.WAVE_OCTAVES, CoastShaper.WAVE_LACUNARITY, CoastShaper.WAVE_PERSISTENCE);
        }
    }
}

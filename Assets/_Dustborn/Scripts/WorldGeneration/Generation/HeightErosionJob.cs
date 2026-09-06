using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct ErosionFlowJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Heights;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Loss;
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Scale;

    public int Resolution;
    public float Talus;
    public float Strength;

    public void Execute(int row)
    {
        for (int x = 0; x < Resolution; x++)
        {
            float height = Heights[row * Resolution + x];

            float total = 0f;
            float peak = 0f;

            Drop(height, x - 1, row, ref total, ref peak);
            Drop(height, x + 1, row, ref total, ref peak);
            Drop(height, x, row - 1, ref total, ref peak);
            Drop(height, x, row + 1, ref total, ref peak);

            float loss = total <= 0f ? 0f : Strength * peak;

            Loss[row * Resolution + x] = loss;
            Scale[row * Resolution + x] = loss <= 0f ? 0f : loss / total;
        }
    }

    private void Drop(float height, int x, int y, ref float total, ref float peak)
    {
        int column = math.clamp(x, 0, Resolution - 1);
        int line = math.clamp(y, 0, Resolution - 1);

        float excess = height - Heights[line * Resolution + column] - Talus;

        if (excess <= 0f)
            return;

        total += excess;
        peak = math.max(peak, excess);
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct ErosionSettleJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Heights;
    [ReadOnly] public NativeArray<float> Loss;
    [ReadOnly] public NativeArray<float> Scale;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Target;

    public int Resolution;
    public float Talus;

    public void Execute(int row)
    {
        for (int x = 0; x < Resolution; x++)
        {
            int index = row * Resolution + x;

            float height = Heights[index];

            float gain = Gain(height, x - 1, row)
                         + Gain(height, x + 1, row)
                         + Gain(height, x, row - 1)
                         + Gain(height, x, row + 1);

            Target[index] = math.saturate(height - Loss[index] + gain);
        }
    }

    private float Gain(float height, int x, int y)
    {
        int column = math.clamp(x, 0, Resolution - 1);
        int line = math.clamp(y, 0, Resolution - 1);

        int index = line * Resolution + column;

        float excess = Heights[index] - height - Talus;

        return excess <= 0f ? 0f : Scale[index] * excess;
    }
}

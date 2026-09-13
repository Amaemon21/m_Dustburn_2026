using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct ErosionPassJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Heights;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Target;

    public int Resolution;
    public float Talus;
    public float Strength;

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

            Target[index] = math.saturate(height - Loss(x, row, out _) + gain);
        }
    }

    private float Gain(float height, int x, int y)
    {
        int column = math.clamp(x, 0, Resolution - 1);
        int line = math.clamp(y, 0, Resolution - 1);

        float excess = Heights[line * Resolution + column] - height - Talus;

        if (excess <= 0f)
            return 0f;

        float loss = Loss(column, line, out float total);
        float scale = loss <= 0f ? 0f : loss / total;

        return scale * excess;
    }

    private float Loss(int x, int y, out float total)
    {
        float height = Heights[y * Resolution + x];

        total = 0f;
        float peak = 0f;

        Drop(height, x - 1, y, ref total, ref peak);
        Drop(height, x + 1, y, ref total, ref peak);
        Drop(height, x, y - 1, ref total, ref peak);
        Drop(height, x, y + 1, ref total, ref peak);

        return total <= 0f ? 0f : Strength * peak;
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

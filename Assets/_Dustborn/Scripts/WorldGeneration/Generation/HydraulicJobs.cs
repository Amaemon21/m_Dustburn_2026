using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HydraulicSampleJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Heights;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Coarse;

    public int Resolution;
    public int CoarseResolution;
    public int Step;

    public void Execute(int row)
    {
        int source = math.min(row * Step, Resolution - 1) * Resolution;

        for (int x = 0; x < CoarseResolution; x++)
            Coarse[row * CoarseResolution + x] = Heights[source + math.min(x * Step, Resolution - 1)];
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HydraulicOutflowJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Coarse;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Outflow;

    public int Resolution;

    public void Execute(int row)
    {
        for (int x = 0; x < Resolution; x++)
        {
            float height = Coarse[row * Resolution + x];
            float total = 0f;

            for (int step = 0; step < 8; step++)
            {
                total += Drop(height, x + HydraulicNeighbours.OffsetX(step), row + HydraulicNeighbours.OffsetY(step),
                    HydraulicNeighbours.Length(step));
            }

            Outflow[row * Resolution + x] = total;
        }
    }

    private float Drop(float height, int x, int y, float length)
    {
        int column = math.clamp(x, 0, Resolution - 1);
        int line = math.clamp(y, 0, Resolution - 1);

        return math.max(0f, height - Coarse[line * Resolution + column]) / length;
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HydraulicFlowJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Coarse;
    [ReadOnly] public NativeArray<float> Outflow;
    [ReadOnly] public NativeArray<float> Flow;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Target;

    public int Resolution;

    public void Execute(int row)
    {
        for (int x = 0; x < Resolution; x++)
        {
            float height = Coarse[row * Resolution + x];
            float gain = 1f;

            for (int step = 0; step < 8; step++)
            {
                gain += Share(height, x + HydraulicNeighbours.OffsetX(step), row + HydraulicNeighbours.OffsetY(step),
                    HydraulicNeighbours.Length(step));
            }

            Target[row * Resolution + x] = gain;
        }
    }

    private float Share(float height, int x, int y, float length)
    {
        int column = math.clamp(x, 0, Resolution - 1);
        int line = math.clamp(y, 0, Resolution - 1);

        int index = line * Resolution + column;

        float drop = (Coarse[index] - height) / length;

        if (drop <= 0f)
            return 0f;

        float total = Outflow[index];

        return total <= 0f ? 0f : Flow[index] * drop / total;
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HydraulicCarveJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Coarse;
    [ReadOnly] public NativeArray<float> Flow;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Cut;

    public int Resolution;
    public float CellSize;
    public float MaxHeight;
    public float Strength;
    public float FlowExponent;
    public float FlowReference;
    public float MaxCut;

    public void Execute(int row)
    {
        for (int x = 0; x < Resolution; x++)
        {
            int index = row * Resolution + x;

            float west = Coarse[row * Resolution + math.max(x - 1, 0)];
            float east = Coarse[row * Resolution + math.min(x + 1, Resolution - 1)];
            float south = Coarse[math.max(row - 1, 0) * Resolution + x];
            float north = Coarse[math.min(row + 1, Resolution - 1) * Resolution + x];

            float dx = (east - west) * MaxHeight / (2f * CellSize);
            float dy = (north - south) * MaxHeight / (2f * CellSize);

            float slope = math.sqrt(dx * dx + dy * dy);
            float water = math.pow(math.max(1f, Flow[index]) / FlowReference, FlowExponent);

            Cut[index] = math.min(MaxCut, Strength * water * slope) / MaxHeight;
        }
    }
}

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
public struct HydraulicApplyJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float> Cut;

    public NativeArray<float> Heights;

    public int Resolution;
    public int CoarseResolution;
    public float Step;

    public void Execute(int row)
    {
        float v = math.clamp(row / Step, 0f, CoarseResolution - 1.001f);

        int y0 = (int)v;
        int y1 = math.min(y0 + 1, CoarseResolution - 1);
        float ty = v - y0;

        for (int x = 0; x < Resolution; x++)
        {
            float u = math.clamp(x / Step, 0f, CoarseResolution - 1.001f);

            int x0 = (int)u;
            int x1 = math.min(x0 + 1, CoarseResolution - 1);
            float tx = u - x0;

            float bottom = math.lerp(Cut[y0 * CoarseResolution + x0], Cut[y0 * CoarseResolution + x1], tx);
            float top = math.lerp(Cut[y1 * CoarseResolution + x0], Cut[y1 * CoarseResolution + x1], tx);

            int index = row * Resolution + x;

            Heights[index] = math.saturate(Heights[index] - math.lerp(bottom, top, ty));
        }
    }
}

public static class HydraulicNeighbours
{
    public static int OffsetX(int step)
    {
        return step switch
        {
            0 => -1, 1 => 1, 2 => 0, 3 => 0,
            4 => -1, 5 => 1, 6 => -1, _ => 1
        };
    }

    public static int OffsetY(int step)
    {
        return step switch
        {
            0 => 0, 1 => 0, 2 => -1, 3 => 1,
            4 => -1, 5 => -1, 6 => 1, _ => 1
        };
    }

    public static float Length(int step)
    {
        return step < 4 ? 1f : 1.41421356f;
    }
}

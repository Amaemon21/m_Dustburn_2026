using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Strict, CompileSynchronously = true, DisableSafetyChecks = true)]
public struct ErosionPassJob : IJobParallelFor
{
    public const int TILE = 16;

    [ReadOnly] public NativeArray<float> Heights;
    [ReadOnly] public NativeArray<byte> Active;

    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> Target;
    [WriteOnly] public NativeArray<byte> Changed;

    public int Resolution;
    public int Tiles;
    public float Talus;
    public float Strength;

    public void Execute(int tile)
    {
        if (Active[tile] == 0)
        {
            Changed[tile] = 0;
            return;
        }

        int fromX = tile % Tiles * TILE;
        int fromY = tile / Tiles * TILE;
        int toX = math.min(fromX + TILE, Resolution);
        int toY = math.min(fromY + TILE, Resolution);
        bool changed = false;

        for (int row = fromY; row < toY; row++)
        {
            for (int x = fromX; x < toX; x++)
            {
                int index = row * Resolution + x;

                float height = Heights[index];

                float gain = Gain(height, x - 1, row)
                             + Gain(height, x + 1, row)
                             + Gain(height, x, row - 1)
                             + Gain(height, x, row + 1);

                float result = math.saturate(height - Loss(x, row, out _) + gain);

                Target[index] = result;
                changed |= result != height;
            }
        }

        Changed[tile] = changed ? (byte)1 : (byte)0;
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

[BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
public struct ErosionActiveJob : IJob
{
    [ReadOnly] public NativeArray<byte> Changed;

    public NativeArray<byte> Active;
    public NativeArray<int> Count;

    public int Tiles;
    public bool All;

    public void Execute()
    {
        int count = 0;

        for (int tileY = 0; tileY < Tiles; tileY++)
        {
            for (int tileX = 0; tileX < Tiles; tileX++)
            {
                byte active = All ? (byte)1 : (byte)0;

                for (int y = math.max(0, tileY - 1); y <= math.min(Tiles - 1, tileY + 1) && active == 0; y++)
                    for (int x = math.max(0, tileX - 1); x <= math.min(Tiles - 1, tileX + 1) && active == 0; x++)
                        active = Changed[y * Tiles + x];

                Active[tileY * Tiles + tileX] = active;
                count += active;
            }
        }

        Count[0] = count;
    }
}

using Unity.Collections;

public static class VoxelCellTables
{
    public const int CORNERS = 8;

    public static readonly int[] CornerX = { 0, 1, 1, 0, 0, 1, 1, 0 };
    public static readonly int[] CornerY = { 0, 0, 0, 0, 1, 1, 1, 1 };
    public static readonly int[] CornerZ = { 0, 0, 1, 1, 0, 0, 1, 1 };
}

public struct VoxelCellLayout
{
    [ReadOnly] public NativeArray<int> CornerX;
    [ReadOnly] public NativeArray<int> CornerY;
    [ReadOnly] public NativeArray<int> CornerZ;

    public static VoxelCellLayout Create(Allocator allocator)
    {
        return new VoxelCellLayout
        {
            CornerX = new NativeArray<int>(VoxelCellTables.CornerX, allocator),
            CornerY = new NativeArray<int>(VoxelCellTables.CornerY, allocator),
            CornerZ = new NativeArray<int>(VoxelCellTables.CornerZ, allocator)
        };
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref CornerX);
        NativeBuffer.Release(ref CornerY);
        NativeBuffer.Release(ref CornerZ);
    }
}

using Unity.Collections;

public static class VoxelCellTables
{
    public const int CORNERS = 8;
    public const int EDGES = 12;

    public static readonly int[] CornerX = { 0, 1, 1, 0, 0, 1, 1, 0 };
    public static readonly int[] CornerY = { 0, 0, 0, 0, 1, 1, 1, 1 };
    public static readonly int[] CornerZ = { 0, 0, 1, 1, 0, 0, 1, 1 };

    public static readonly int[] EdgeFrom = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3 };
    public static readonly int[] EdgeTo = { 1, 2, 3, 0, 5, 6, 7, 4, 4, 5, 6, 7 };
}

public struct VoxelCellLayout
{
    [ReadOnly] public NativeArray<int> CornerX;
    [ReadOnly] public NativeArray<int> CornerY;
    [ReadOnly] public NativeArray<int> CornerZ;
    [ReadOnly] public NativeArray<int> EdgeFrom;
    [ReadOnly] public NativeArray<int> EdgeTo;

    public static VoxelCellLayout Create(Allocator allocator)
    {
        return new VoxelCellLayout
        {
            CornerX = new NativeArray<int>(VoxelCellTables.CornerX, allocator),
            CornerY = new NativeArray<int>(VoxelCellTables.CornerY, allocator),
            CornerZ = new NativeArray<int>(VoxelCellTables.CornerZ, allocator),
            EdgeFrom = new NativeArray<int>(VoxelCellTables.EdgeFrom, allocator),
            EdgeTo = new NativeArray<int>(VoxelCellTables.EdgeTo, allocator)
        };
    }

    public void Dispose()
    {
        NativeBuffer.Release(ref CornerX);
        NativeBuffer.Release(ref CornerY);
        NativeBuffer.Release(ref CornerZ);
        NativeBuffer.Release(ref EdgeFrom);
        NativeBuffer.Release(ref EdgeTo);
    }
}

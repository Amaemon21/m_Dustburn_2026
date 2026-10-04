using Unity.Collections;

public static class NativeBuffer
{
    public static NativeArray<float> From(float[] values)
    {
        if (values == null || values.Length == 0)
            return new NativeArray<float>(1, Allocator.Persistent);

        return new NativeArray<float>(values, Allocator.Persistent);
    }

    public static void Release<T>(ref NativeArray<T> array) where T : struct
    {
        if (!array.IsCreated)
            return;

        array.Dispose();
        array = default;
    }
}

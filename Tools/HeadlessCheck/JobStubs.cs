using System;

namespace Unity.Burst
{
    public enum FloatPrecision { Standard, High, Medium, Low }

    public enum FloatMode { Default, Strict, Deterministic, Fast }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Class)]
    public class BurstCompileAttribute : Attribute
    {
        public bool CompileSynchronously { get; set; }

        public BurstCompileAttribute() { }
        public BurstCompileAttribute(FloatPrecision precision, FloatMode mode) { }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }

    public enum NativeArrayOptions { UninitializedMemory, ClearMemory }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public class WriteOnlyAttribute : Attribute { }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        private T[] _values;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            _values = new T[length];
        }

        public NativeArray(T[] source, Allocator allocator)
        {
            _values = (T[])source.Clone();
        }

        public int Length => _values == null ? 0 : _values.Length;

        public bool IsCreated => _values != null;

        public T this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        public void CopyTo(T[] destination) => Array.Copy(_values, destination, _values.Length);

        public void CopyTo(NativeArray<T> destination) => Array.Copy(_values, destination._values, _values.Length);

        public void Dispose() => _values = null;

        public static void Copy(T[] source, int sourceIndex, NativeArray<T> destination, int destinationIndex, int length)
        {
            Array.Copy(source, sourceIndex, destination._values, destinationIndex, length);
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)]
    public class NativeDisableParallelForRestrictionAttribute : Attribute { }
}

namespace Unity.Jobs
{
    public struct JobHandle
    {
        public void Complete() { }

        public static void ScheduleBatchedJobs() { }
    }

    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T job, int length, int batch, JobHandle dependency) where T : struct, IJobParallelFor
        {
            return job.Schedule(length, batch);
        }

        public static JobHandle Schedule<T>(this T job, int length, int batch) where T : struct, IJobParallelFor
        {
            for (int i = 0; i < length; i++)
                job.Execute(i);

            return default;
        }
    }
}

namespace Unity.Collections
{
    using System.Collections;
    using System.Collections.Generic;

    public struct NativeList<T> : IEnumerable<T> where T : unmanaged
    {
        private List<T> _values;

        public NativeList(Allocator allocator)
        {
            _values = new List<T>();
        }

        public NativeList(int capacity, Allocator allocator)
        {
            _values = new List<T>(capacity);
        }

        public int Length => _values == null ? 0 : _values.Count;

        public bool IsCreated => _values != null;

        public T this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        public void Add(T value) => _values.Add(value);

        public void Clear() => _values.Clear();

        public void Dispose() => _values = null;

        public NativeArray<T> AsArray() => new NativeArray<T>(_values.ToArray(), Allocator.Temp);

        public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }
}

namespace Unity.Jobs
{
    public interface IJob
    {
        void Execute();
    }

    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T job, JobHandle dependency = default) where T : struct, IJob
        {
            job.Execute();

            return default;
        }

        public static void Run<T>(this T job) where T : struct, IJob
        {
            job.Execute();
        }
    }
}

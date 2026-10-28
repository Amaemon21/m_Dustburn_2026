using System;

namespace Unity.Burst
{
    public enum FloatPrecision { Standard }
    public enum FloatMode { Strict }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class BurstCompileAttribute : Attribute
    {
        public BurstCompileAttribute(FloatPrecision precision, FloatMode mode) { }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Persistent }
    public enum NativeArrayOptions { UninitializedMemory }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class WriteOnlyAttribute : Attribute { }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        private T[] _values;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            _values = new T[length];
        }

        public NativeArray(T[] values, Allocator allocator)
        {
            _values = (T[])values.Clone();
        }

        public int Length => _values.Length;

        public T this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        public void Dispose() => _values = null;
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { }
}

namespace Unity.Jobs
{
    public interface IJobParallelFor
    {
        void Execute(int index);
    }
}

namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte red, byte green, byte blue, byte alpha)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }
    }
}

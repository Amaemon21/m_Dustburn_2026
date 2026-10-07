using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class GenerationChecksum
{
    private const ulong OFFSET = 14695981039346656037UL;
    private const ulong PRIME = 1099511628211UL;
    private const int BLOCK = 1 << 20;

    public static string Of(float[] values)
    {
        var blocks = new ulong[Blocks(values.Length)];

        Parallel.For(0, blocks.Length, block =>
        {
            int end = Math.Min(values.Length, (block + 1) * BLOCK);
            ulong hash = OFFSET;

            for (int i = block * BLOCK; i < end; i++)
                hash = Mix(hash, (uint)BitConverter.SingleToInt32Bits(values[i]));

            blocks[block] = hash;
        });

        return Format(Combine(blocks));
    }

    public static string Of(NativeArray<float> values)
    {
        var blocks = new NativeArray<ulong>(Blocks(values.Length), Allocator.TempJob);

        try
        {
            new BlockHashJob { Values = values, Blocks = blocks }.Schedule(blocks.Length, 1).Complete();

            var hashes = new ulong[blocks.Length];
            blocks.CopyTo(hashes);

            return Format(Combine(hashes));
        }
        finally
        {
            blocks.Dispose();
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct BlockHashJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Values;
        public NativeArray<ulong> Blocks;

        public void Execute(int block)
        {
            int end = math.min(Values.Length, (block + 1) * BLOCK);
            ulong hash = OFFSET;

            for (int i = block * BLOCK; i < end; i++)
                hash = Mix(hash, math.asuint(Values[i]));

            Blocks[block] = hash;
        }
    }

    private static int Blocks(int length)
    {
        return Math.Max(1, (length + BLOCK - 1) / BLOCK);
    }

    private static ulong Combine(ulong[] blocks)
    {
        ulong hash = OFFSET;

        foreach (ulong block in blocks)
        {
            hash = Mix(hash, (uint)block);
            hash = Mix(hash, (uint)(block >> 32));
        }

        return hash;
    }

    public static string Of(byte[] values)
    {
        ulong hash = OFFSET;

        foreach (byte value in values)
            hash = Mix(hash, value);

        return Format(hash);
    }

    public static string Of(int[] values)
    {
        ulong hash = OFFSET;

        foreach (int value in values)
            hash = Mix(hash, (uint)value);

        return Format(hash);
    }

    public static string Of(short[] values)
    {
        ulong hash = OFFSET;

        foreach (short value in values)
            hash = Mix(hash, (ushort)value);

        return Format(hash);
    }

    public static string Of(IEnumerable<List<Vector2>> paths)
    {
        ulong hash = OFFSET;

        foreach (List<Vector2> path in paths)
        {
            hash = Mix(hash, (uint)path.Count);

            foreach (Vector2 point in path)
            {
                hash = Mix(hash, (uint)BitConverter.SingleToInt32Bits(point.x));
                hash = Mix(hash, (uint)BitConverter.SingleToInt32Bits(point.y));
            }
        }

        return Format(hash);
    }

    public static string Of(IEnumerable<List<int>> lists)
    {
        ulong hash = OFFSET;

        foreach (List<int> list in lists)
        {
            hash = Mix(hash, (uint)list.Count);

            foreach (int value in list)
                hash = Mix(hash, (uint)value);
        }

        return Format(hash);
    }

    private static ulong Mix(ulong hash, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (value >> shift) & 0xFF;
            hash *= PRIME;
        }

        return hash;
    }

    private static string Format(ulong hash)
    {
        return (hash & 0xFFFFFFFFUL).ToString("X8");
    }
}
